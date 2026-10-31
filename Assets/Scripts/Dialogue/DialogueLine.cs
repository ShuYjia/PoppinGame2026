using System;
using UnityEngine;

/// <summary>这句台词是谁在说话。决定气泡飘到谁头顶，也决定说话者名字的默认取值。</summary>
public enum DialogueSpeaker
{
    /// <summary>场景里的 NPC（对话发起者）。气泡挂在 NPC 头顶锚点上。</summary>
    Npc,

    /// <summary>玩家角色（ksm/香澄）。气泡改挂到玩家头顶锚点上，NPC 的气泡此时隐藏。</summary>
    Player
}

/// <summary>
/// 一句台词的序列化数据：说话者、正文、可选语音。
/// 纯数据，不含任何播放逻辑——播放由 <see cref="NpcDialogue"/> 驱动。
/// </summary>
[Serializable]
public class DialogueLine
{
    [Tooltip("这句是谁在说话。Npc = 挂在 NPC 上的对话组件；Player = 玩家角色。\n" +
             "选 Player 时气泡会改挂到玩家头顶，NPC 的气泡同时隐藏，玩家说完再切回来。")]
    public DialogueSpeaker speaker = DialogueSpeaker.Npc;

    [Tooltip("说话者名字。留空时：Npc 用该 NPC 的显示名，Player 用 NpcDialogue 上的「玩家说话名」。")]
    public string speakerName = "";

    [Tooltip("台词正文。支持 TextMeshPro 富文本标签（逐字显示按可见字符数推进，不会把标签打碎）。")]
    [TextArea(2, 5)]
    public string text = "";

    [Tooltip("本句的可选语音。未指定时该句不出声，也不会影响流程（末句停留只看最短时间）。")]
    public AudioClip voiceClip;
}
