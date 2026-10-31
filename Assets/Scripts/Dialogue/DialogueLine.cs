using System;
using UnityEngine;

/// <summary>
/// 一句台词的序列化数据：说话者、正文、可选语音。
/// 纯数据，不含任何播放逻辑——播放由 <see cref="NpcDialogue"/> 驱动。
/// </summary>
[Serializable]
public class DialogueLine
{
    [Tooltip("说话者名字。留空时 NpcDialogue 会退回使用该 NPC 的显示名。")]
    public string speakerName = "";

    [Tooltip("台词正文。支持 TextMeshPro 富文本标签（逐字显示按可见字符数推进，不会把标签打碎）。")]
    [TextArea(2, 5)]
    public string text = "";

    [Tooltip("本句的可选语音。未指定时该句不出声，也不会影响流程（末句停留只看最短时间）。")]
    public AudioClip voiceClip;
}
