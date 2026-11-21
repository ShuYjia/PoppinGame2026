using System.Collections.Generic;
using System.Text;
using Poppin.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 生成 Player 用的「纯片段」Animator Controller（菜单 Poppin → Animation → …），
/// 以及把已经存在的敌人预制体补上动画 FSM 组件。
///
/// 为什么要有这个工具：动画已经改成代码有限状态机直驱，Animator 只负责播片段。
/// 这种控制器有 13 个状态、零参数、零过渡，手工在 Animator 窗口里摆一遍既慢又容易漏，
/// 而手工写 .controller 的 YAML 需要猜 fileID / 状态机结构（项目约定也明确不这么做），
/// 所以统一用 AssetDatabase + AnimatorController API 生成。
///
/// 幂等：控制器已存在时复用同一个资产（GUID 不变，预制体引用不会断），只补齐缺失状态并重设片段。
/// </summary>
public static class AnimationFsmSetupTool
{
    private const string ControllerPath = "Assets/animate1/charartor/ksm/ksm_fsm.controller";

    /// <summary>状态名 → 动画片段路径。状态名必须与 PlayerAnimatorView 里的默认绑定一致。</summary>
    private static readonly KeyValuePair<string, string>[] PlayerStates =
    {
        new KeyValuePair<string, string>("idle", "Assets/animate1/charartor/ksm/idle.anim"),
        new KeyValuePair<string, string>("walk", "Assets/animate1/charartor/ksm/walk.anim"),
        new KeyValuePair<string, string>("run", "Assets/animate1/charartor/ksm/run.anim"),
        new KeyValuePair<string, string>("crouchDown", "Assets/animate1/charartor/ksm/crouchDown.anim"),
        new KeyValuePair<string, string>("crouchIdle", "Assets/animate1/charartor/ksm/crouchIdle.anim"),
        new KeyValuePair<string, string>("JumpStart", "Assets/AnimateScript/JumpStart.anim"),
        new KeyValuePair<string, string>("JumpRise", "Assets/AnimateScript/JumpRise.anim"),
        new KeyValuePair<string, string>("JumpHighest", "Assets/AnimateScript/JumpHighest.anim"),
        new KeyValuePair<string, string>("JumpFall", "Assets/AnimateScript/JumpFall.anim"),
        new KeyValuePair<string, string>("normalAttack", "Assets/animate1/charartor/ksm/normalAttack.anim"),
        new KeyValuePair<string, string>("sprint", "Assets/animate1/charartor/ksm/sprint.anim"),

        // 受伤要的是「有帧变化」的那一段：hurt.anim 带 m_Sprite 曲线（12 帧 @60fps，中间夹一次红闪，
        // 首末颜色都是白）。ksmHurt2.anim 只有 material._Color 曲线，播出来就是「人不变、只变红」，
        // 所以只留作备份，绑定不再指向它。
        new KeyValuePair<string, string>("hurt", "Assets/AnimateScript/hurt.anim"),
        new KeyValuePair<string, string>("ksmHurt2", "Assets/AnimateScript/ksmHurt2.anim"),
        new KeyValuePair<string, string>("ksmDie1", "Assets/AnimateScript/ksmDie1.anim"),
    };

    [MenuItem("Poppin/Animation/Build Player Animation FSM Controller")]
    private static void BuildFromMenu()
    {
        string report = Run();
        Debug.Log(report);
        EditorUtility.DisplayDialog("Player Animation FSM", report, "好");
    }

    /// <summary>生成（或补齐）Player 的 FSM 控制器并返回报告。供菜单与批处理 / MCP 调用。</summary>
    public static string Run()
    {
        StringBuilder log = new StringBuilder();

        // 先把片段全部解析一遍：缺任何一个都直接中止，避免生成一个半残的控制器。
        List<string> missing = new List<string>();
        AnimationClip[] clips = new AnimationClip[PlayerStates.Length];
        for (int i = 0; i < PlayerStates.Length; i++)
        {
            clips[i] = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerStates[i].Value);
            if (clips[i] == null)
                missing.Add(PlayerStates[i].Key + " → " + PlayerStates[i].Value);
        }

        if (missing.Count > 0)
            return "AnimationFsmSetupTool: 缺少动画片段，已中止：\n" + string.Join("\n", missing);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        bool created = controller == null;
        if (created)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            log.AppendLine("新建控制器：" + ControllerPath);
        }
        else
        {
            log.AppendLine("复用已有控制器：" + ControllerPath);
        }

        // 单层、零参数：代码直接 Play 状态名，不需要任何参数或过渡。
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        log.AppendLine("层：" + controller.layers[0].name + "（参数 " + controller.parameters.Length + " 个）");

        for (int i = 0; i < PlayerStates.Length; i++)
        {
            string stateName = PlayerStates[i].Key;
            AnimatorState state = FindState(machine, stateName);
            bool stateCreated = state == null;
            if (stateCreated)
                state = machine.AddState(stateName);

            state.motion = clips[i];
            state.speed = 1f;
            state.writeDefaultValues = true;

            log.AppendLine((stateCreated ? "  + " : "  = ") + stateName + " ← " + clips[i].name);
        }

        // 默认状态设为 idle：Animator 启用的一瞬间就是站立姿势，不会停在第一个添加的状态上。
        AnimatorState idle = FindState(machine, "idle");
        if (idle != null)
            machine.defaultState = idle;

        // 兜底：把工具不认识的残留状态也报出来（例如有人手工加了过渡状态），提醒人工确认。
        foreach (ChildAnimatorState child in machine.states)
        {
            if (!Contains(PlayerStates, child.state.name))
                log.AppendLine("  ! 发现未在清单中的状态：" + child.state.name);
        }

        if (machine.anyStateTransitions.Length > 0)
            log.AppendLine("  ! 控制器里还有 " + machine.anyStateTransitions.Length + " 条 AnyState 过渡（应为 0）");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // 注意：这里**不要**紧接着 ImportAsset 强制重导入。
        // 实测（2026-11）保存后立刻 ForceUpdate 重导入，会把内存里的资产换成磁盘上刚写入的
        // 半成品，导致运行时 Animator 报 layerCount=1 但 states=0、HasState 全 false。
        // 保存即可；Unity 自己会在需要时同步。

        string guid = AssetDatabase.AssetPathToGUID(ControllerPath);

        // 回读校验：状态数必须与清单一致，否则明确报错（避免生成一个空壳控制器却静默通过）。
        int stateCount = controller.layers[0].stateMachine.states.Length;
        if (stateCount != PlayerStates.Length)
        {
            log.AppendLine("  ! 回读校验失败：内存中只有 " + stateCount + " 个状态（期望 " +
                           PlayerStates.Length + "），请重新执行本工具。");
        }

        log.AppendLine("GUID：" + guid);
        log.AppendLine("完成。预制体里的 Animator 需要指向它（m_Controller guid = " + guid + "）。");
        return log.ToString();
    }

    [MenuItem("Poppin/Animation/Add Enemy Animation FSM To Enemy Prefabs")]
    private static void SetupEnemyPrefabsFromMenu()
    {
        string report = SetupEnemyPrefabs();
        Debug.Log(report);
        EditorUtility.DisplayDialog("Enemy Animation FSM", report, "好");
    }

    /// <summary>
    /// 给敌人预制体补上 <see cref="EnemyAnimationStateMachine"/>、接好同物体的 SpriteSequencePlayer、
    /// 并写入与代码一致的一套默认绑定。幂等：已有组件时只重写绑定，不重复添加。
    /// </summary>
    public static string SetupEnemyPrefabs()
    {
        string[] prefabPaths =
        {
            "Assets/Prefabs/Level/Boar.prefab",
            "Assets/Prefabs/Level/RangedBoar.prefab",
        };

        StringBuilder log = new StringBuilder();
        AnimationStateBinding[] defaults = EnemyAnimationStateMachine.CreateDefaultBindings();

        foreach (string path in prefabPaths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                log.AppendLine(path + "：加载失败，跳过");
                continue;
            }

            SpriteSequencePlayer sequence = root.GetComponent<SpriteSequencePlayer>();
            if (sequence == null)
            {
                log.AppendLine(path + "：没有 SpriteSequencePlayer，跳过");
                PrefabUtility.UnloadPrefabContents(root);
                continue;
            }

            EnemyAnimationStateMachine machine = root.GetComponent<EnemyAnimationStateMachine>();
            bool added = machine == null;
            if (added)
                machine = root.AddComponent<EnemyAnimationStateMachine>();

            SerializedObject so = new SerializedObject(machine);
            so.FindProperty("sequence").objectReferenceValue = sequence;

            SerializedProperty bindings = so.FindProperty("bindings");
            bindings.arraySize = defaults.Length;
            for (int i = 0; i < defaults.Length; i++)
            {
                SerializedProperty element = bindings.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("stateId").stringValue = defaults[i].stateId;
                element.FindPropertyRelative("playbackName").stringValue = defaults[i].playbackName;
                element.FindPropertyRelative("loop").boolValue = defaults[i].loop;
                element.FindPropertyRelative("exitNormalizedTime").floatValue = defaults[i].exitNormalizedTime;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            log.AppendLine(path + "：" + (added ? "已添加" : "已存在，仅刷新绑定")
                                      + "，绑定 " + defaults.Length + " 条");
        }

        AssetDatabase.SaveAssets();
        return "AnimationFsmSetupTool（敌人预制体）：\n" + log;
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null && child.state.name == stateName)
                return child.state;
        }

        return null;
    }

    private static bool Contains(KeyValuePair<string, string>[] pairs, string stateName)
    {
        foreach (KeyValuePair<string, string> pair in pairs)
        {
            if (pair.Key == stateName)
                return true;
        }

        return false;
    }
}
