using UnityEditor;
using UnityEngine;

/// <summary>
/// NpcDialogue 的 Inspector 增强：只加按钮与配置体检，不改任何序列化布局。
/// - 「重置对话进度」：清掉运行时保存的中断句索引，下次交互从第一句开始。
/// - 配置自检：台词为空、触发器缺失、气泡与预制体都没给时给出可操作的提示。
/// </summary>
[CustomEditor(typeof(NpcDialogue))]
public class NpcDialogueEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        NpcDialogue npc = (NpcDialogue)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("调试", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        if (GUILayout.Button("重置对话进度"))
        {
            Undo.RecordObject(npc, "Reset Dialogue Progress");
            npc.ResetProgress();
            EditorUtility.SetDirty(npc);
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox(
            "运行时状态：State = " + npc.State +
            "，句索引 = " + npc.CurrentIndex +
            "，有中断进度 = " + npc.HasInterruptedProgress,
            MessageType.None);

        DrawConfigurationIssues(npc);
    }

    private static void DrawConfigurationIssues(NpcDialogue npc)
    {
        if (npc.LineCount > 0 && npc.interactionTrigger != null && (npc.bubbleView != null || npc.bubblePrefab != null))
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("配置自检", EditorStyles.boldLabel);

        if (npc.LineCount == 0)
            EditorGUILayout.HelpBox("台词列表为空：交互会安全退出（不抛异常），但玩家按了没反应。请在 Lines 里至少配一句。", MessageType.Warning);

        if (npc.interactionTrigger == null)
            EditorGUILayout.HelpBox("没有交互触发器：需要一个 isTrigger 的 Collider2D 才能检测玩家进出范围。建议做成 NPC 的子物体单独摆放范围。", MessageType.Warning);

        if (npc.promptView == null && npc.promptPrefab == null)
            EditorGUILayout.HelpBox("交互提示视图与预制体都没给：进入范围不会显示「按 E 交谈」这类提示。", MessageType.Info);
    }
}
