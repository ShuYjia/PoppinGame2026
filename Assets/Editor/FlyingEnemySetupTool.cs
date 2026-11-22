using System.Collections.Generic;
using System.Text;
using Poppin.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 飞行敌人（蝙蝠）的一次性装配工具，菜单 Poppin → Flying Enemy → Build All。
///
/// 两件事：
/// 1. 修复 FlyEnemy.controller：代码 FSM 直驱要求「4 个状态 / 0 参数 / 0 过渡」，
///    目前缺 BatIsHurt 状态；同时把 BatDeath 的 Loop Time 关掉，免得死亡动画循环播放。
/// 2. 把场景里那个只有 SpriteRenderer + Animator 的 FlyEnemy 空壳补全成完整敌人，
///    并就地保存为 Assets/Prefabs/Level/FlyEnemy.prefab（保留原位置、名字与 Animator 引用）。
///
/// 幂等：状态和组件都按「有则复用、无则新建」处理，可重复执行。
/// 不保存场景：场景是否落盘由使用者决定（现场可能有未保存的编辑）。
/// 注意：保存 AnimatorController 后**不要**立刻 ImportAsset(ForceUpdate)，
/// 实测会把内存里的控制器换成空壳（states=0）。
/// </summary>
public static class FlyingEnemySetupTool
{
    private const string ControllerPath = "Assets/Art/Hallowing/BatAnimate/FlyEnemy.controller";
    private const string ClipFolder = "Assets/Art/Hallowing/BatAnimate/";
    private const string PrefabPath = "Assets/Prefabs/Level/FlyEnemy.prefab";
    private const string ProjectilePath = "Assets/Prefabs/Level/EnemyProjectile.prefab";
    private const string SceneObjectName = "FlyEnemy";
    private const int EnemyLayer = 7;

    private static readonly KeyValuePair<string, string>[] States =
    {
        new KeyValuePair<string, string>("BatFly", "BatFly.anim"),
        new KeyValuePair<string, string>("BatFlyChase", "BatFlyChase.anim"),
        new KeyValuePair<string, string>("BatIsHurt", "BatIsHurt.anim"),
        new KeyValuePair<string, string>("BatDeath", "BatDeath.anim"),
    };

    [MenuItem("Poppin/Flying Enemy/Build All (controller + prefab)")]
    public static string BuildAll()
    {
        StringBuilder log = new StringBuilder();
        log.AppendLine(BuildController());
        log.AppendLine(BuildPrefabFromScene());
        string report = log.ToString();
        Debug.Log(report);
        return report;
    }

    [MenuItem("Poppin/Flying Enemy/1. Fix FlyEnemy animator controller")]
    private static void BuildControllerFromMenu()
    {
        string report = BuildController();
        Debug.Log(report);
        EditorUtility.DisplayDialog("FlyEnemy animator", report, "好");
    }

    [MenuItem("Poppin/Flying Enemy/2. Build prefab from scene FlyEnemy")]
    private static void BuildPrefabFromMenu()
    {
        string report = BuildPrefabFromScene();
        Debug.Log(report);
        EditorUtility.DisplayDialog("FlyEnemy prefab", report, "好");
    }

    /// <summary>补齐控制器状态并关闭 BatDeath 循环。返回可读报告。</summary>
    public static string BuildController()
    {
        StringBuilder log = new StringBuilder("=== FlyEnemy.controller ===\n");

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            return log.AppendLine("失败：找不到 " + ControllerPath).ToString();

        AnimationClip[] clips = new AnimationClip[States.Length];
        for (int i = 0; i < States.Length; i++)
        {
            clips[i] = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipFolder + States[i].Value);
            if (clips[i] == null)
                return log.AppendLine("失败：找不到片段 " + ClipFolder + States[i].Value).ToString();
        }

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        for (int i = 0; i < States.Length; i++)
        {
            string stateName = States[i].Key;
            AnimatorState state = FindState(machine, stateName);
            bool created = state == null;
            if (created)
                state = machine.AddState(stateName);

            state.motion = clips[i];
            state.speed = 1f;
            state.writeDefaultValues = true;
            state.transitions = new AnimatorStateTransition[0];
            log.AppendLine((created ? "+ " : "= ") + stateName + " ← " + clips[i].name);
        }

        AnimatorState fly = FindState(machine, "BatFly");
        if (fly != null)
            machine.defaultState = fly;
        machine.anyStateTransitions = new AnimatorStateTransition[0];
        machine.entryTransitions = new AnimatorTransition[0];

        // BatDeath 是一次性表现：clip 原本勾了 Loop Time，会让死亡序列循环播放。
        AnimationClipSettings deathSettings = AnimationUtility.GetAnimationClipSettings(clips[3]);
        if (deathSettings.loopTime)
        {
            deathSettings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clips[3], deathSettings);
            EditorUtility.SetDirty(clips[3]);
            log.AppendLine("BatDeath: Loop Time 已关闭");
        }
        else
        {
            log.AppendLine("BatDeath: Loop Time 本来就是关闭的");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        int count = controller.layers[0].stateMachine.states.Length;
        log.AppendLine("回读：状态数=" + count + "（期望 " + States.Length + "）"
                       + " 参数=" + controller.parameters.Length
                       + " AnyState 过渡=" + controller.layers[0].stateMachine.anyStateTransitions.Length);
        return log.ToString();
    }

    /// <summary>把场景里的 FlyEnemy 空壳补全、保存为预制体并就地连接。不保存场景。</summary>
    public static string BuildPrefabFromScene()
    {
        StringBuilder log = new StringBuilder("=== FlyEnemy.prefab ===\n");

        Scene scene = SceneManager.GetActiveScene();
        log.AppendLine("场景=" + scene.name + " dirty=" + scene.isDirty);

        GameObject go = GameObject.Find(SceneObjectName);
        if (go == null)
            return log.AppendLine("失败：当前场景里没有名为 " + SceneObjectName + " 的物体").ToString();

        GameObject projectile = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath);
        if (projectile == null)
            return log.AppendLine("失败：找不到 " + ProjectilePath).ToString();

        Animator animator = go.GetComponent<Animator>();
        if (animator == null)
            animator = go.AddComponent<Animator>();
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        go.layer = EnemyLayer;

        SpriteRenderer sprite = go.GetComponent<SpriteRenderer>();
        if (sprite == null)
        {
            sprite = go.AddComponent<SpriteRenderer>();
            Sprite first = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Art/Hallowing/Bat1.png");
            if (first == null)
            {
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Hallowing/Bat1.png");
                for (int i = 0; i < assets.Length; i++)
                {
                    Sprite candidate = assets[i] as Sprite;
                    if (candidate != null)
                    {
                        first = candidate;
                        break;
                    }
                }
            }
            sprite.sprite = first;
        }
        sprite.sortingLayerName = "Middle";
        sprite.sortingOrder = 6;

        Rigidbody2D rb = go.GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // 实体碰撞体：让蝙蝠被墙 / 地形挡住，不穿模。
        CircleCollider2D body = null;
        Collider2D[] existing = go.GetComponents<Collider2D>();
        for (int i = 0; i < existing.Length; i++)
        {
            CircleCollider2D circle = existing[i] as CircleCollider2D;
            if (circle != null && !circle.isTrigger && body == null)
                body = circle;
        }
        if (body == null)
            body = go.AddComponent<CircleCollider2D>();
        body.isTrigger = false;
        body.radius = 0.35f;
        body.offset = Vector2.zero;

        // 受击触发器：玩家音波 / 敌方投射物都靠触发器事件结算。
        CapsuleCollider2D hurtBox = go.GetComponent<CapsuleCollider2D>();
        if (hurtBox == null)
            hurtBox = go.AddComponent<CapsuleCollider2D>();
        hurtBox.isTrigger = true;
        hurtBox.direction = CapsuleDirection2D.Horizontal;
        hurtBox.size = new Vector2(1.4f, 0.8f);
        hurtBox.offset = Vector2.zero;

        CharacterState stats = go.GetComponent<CharacterState>();
        if (stats == null)
            stats = go.AddComponent<CharacterState>();
        stats.maxHP = 100f;
        stats.maxSP = 100f;
        stats.spRegenPerFrame = 0f;
        stats.invincibleTime = 0.5f;
        stats.CurrentHP = stats.maxHP;
        stats.CurrentSP = stats.maxSP;

        EnemyAnimationStateMachine fsm = go.GetComponent<EnemyAnimationStateMachine>();
        if (fsm == null)
            fsm = go.AddComponent<EnemyAnimationStateMachine>();
        SerializedObject fsmSo = new SerializedObject(fsm);
        fsmSo.FindProperty("sequence").objectReferenceValue = null;
        fsmSo.FindProperty("animator").objectReferenceValue = animator;
        SerializedProperty bindings = fsmSo.FindProperty("bindings");
        AnimationStateBinding[] defaults = FlyingEnemyBindings();
        bindings.arraySize = defaults.Length;
        for (int i = 0; i < defaults.Length; i++)
        {
            SerializedProperty element = bindings.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("stateId").stringValue = defaults[i].stateId;
            element.FindPropertyRelative("playbackName").stringValue = defaults[i].playbackName;
            element.FindPropertyRelative("loop").boolValue = defaults[i].loop;
            element.FindPropertyRelative("exitNormalizedTime").floatValue = defaults[i].exitNormalizedTime;
        }
        fsmSo.ApplyModifiedPropertiesWithoutUndo();

        FlyingEnemyController controller2 = go.GetComponent<FlyingEnemyController>();
        if (controller2 == null)
            controller2 = go.AddComponent<FlyingEnemyController>();
        SerializedObject controllerSo = new SerializedObject(controller2);
        controllerSo.FindProperty("projectilePrefab").objectReferenceValue = projectile;
        controllerSo.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, PrefabPath,
            InteractionMode.AutomatedAction);
        if (prefab == null)
            return log.AppendLine("失败：保存预制体失败 " + PrefabPath).ToString();

        AssetDatabase.SaveAssets();

        log.AppendLine("预制体=" + PrefabPath);
        log.AppendLine("layer=" + go.layer + " sorting=" + sprite.sortingLayerName + "/" + sprite.sortingOrder);
        log.AppendLine("controller=" + (animator.runtimeAnimatorController == null ? "null" : animator.runtimeAnimatorController.name));
        log.AppendLine("components=" + ComponentNames(go));
        log.AppendLine("绑定=" + defaults.Length + " 条；投射物=" + projectile.name);
        log.AppendLine("注意：场景未保存，请在确认后手动 Ctrl+S。");
        return log.ToString();
    }

    private static AnimationStateBinding[] FlyingEnemyBindings()
    {
        return new[]
        {
            Bind(EnemyAnimationState.Idle, "BatFly"),
            Bind(EnemyAnimationState.Walk, "BatFly"),
            Bind(EnemyAnimationState.Run, "BatFlyChase"),
            Bind(EnemyAnimationState.Attack, "BatFlyChase"),
            Bind(EnemyAnimationState.Hurt, "BatIsHurt", false),
            Bind(EnemyAnimationState.Dead, "BatDeath", false),
        };
    }

    private static AnimationStateBinding Bind(EnemyAnimationState state, string playbackName, bool loop = true)
    {
        return new AnimationStateBinding
        {
            stateId = state.ToString(),
            playbackName = playbackName,
            loop = loop,
            exitNormalizedTime = 1f,
        };
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        ChildAnimatorState[] states = machine.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state != null && states[i].state.name == name)
                return states[i].state;
        }
        return null;
    }

    private static string ComponentNames(GameObject go)
    {
        StringBuilder sb = new StringBuilder();
        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(components[i] == null ? "MISSING" : components[i].GetType().Name);
        }
        return sb.ToString();
    }
}
