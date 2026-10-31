using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 气泡视图：只负责**显示**，不持台词、不判断流程。
/// 挂在 World Space Canvas 上（NPC 头顶独立画布，不塞进 HUD 的 Screen Space Overlay Canvas）。
/// 由 <see cref="NpcDialogue"/> 调用：设置说话者/正文、按可见字符数推进逐字显示、显示/隐藏。
/// </summary>
public class DialogueBubbleView : MonoBehaviour
{
    [Header("结构")]
    [Tooltip("气泡所在的 Canvas。显示/隐藏直接开关它的 GameObject。")]
    public Canvas canvas;

    [Tooltip("气泡底板 Image（九宫格像素气泡）。可留空（那就只有文字）。")]
    public Image panel;

    [Tooltip("说话者名字文本。可留空。")]
    public TextMeshProUGUI speakerText;

    [Tooltip("台词正文文本。逐字显示通过 TMP 的可见字符数实现，不会把富文本标签打碎。")]
    public TextMeshProUGUI lineText;

    [Header("继续提示")]
    /// <summary>「可以继续」的提示对象（例如一个小三角/箭头）。本句完整显示后才显示，打字中隐藏。</summary>
    public RectTransform continuePrompt;

    [Tooltip("继续提示是否做上下轻微浮动。")]
    public bool animateContinuePrompt = true;

    [Tooltip("继续提示浮动幅度（世界单位）。")]
    public float continuePromptBobHeight = 0.05f;

    [Tooltip("继续提示浮动速度（次 / 秒）。")]
    public float continuePromptBobSpeed = 2f;

    private Vector2 continuePromptBasePosition;

    private void Awake()
    {
        if (canvas == null)
            canvas = GetComponentInChildren<Canvas>(true);

        if (continuePrompt != null)
            continuePromptBasePosition = continuePrompt.anchoredPosition;

        SetContinuePromptVisible(false);
    }

    private void Update()
    {
        if (!animateContinuePrompt || continuePrompt == null || !continuePrompt.gameObject.activeSelf)
            return;

        float offset = Mathf.Sin(Time.time * continuePromptBobSpeed * Mathf.PI * 2f) * continuePromptBobHeight;
        continuePrompt.anchoredPosition = continuePromptBasePosition + new Vector2(0f, offset);
    }

    /// <summary>显示/隐藏整个气泡。</summary>
    public void SetVisible(bool visible)
    {
        if (canvas != null)
            canvas.gameObject.SetActive(visible);
        else
            gameObject.SetActive(visible);
    }

    /// <summary>设置说话者名字。</summary>
    public void SetSpeaker(string speaker)
    {
        if (speakerText != null)
            speakerText.text = speaker;
    }

    /// <summary>设置正文，并把可见字符数重置为「全部可见」。</summary>
    public void SetText(string text)
    {
        if (lineText == null)
            return;

        lineText.text = text;
        lineText.maxVisibleCharacters = 99999;
    }

    /// <summary>设置可见字符数（逐字显示用）。</summary>
    public void SetVisibleCharacters(int count)
    {
        if (lineText != null)
            lineText.maxVisibleCharacters = count;
    }

    /// <summary>立即显示全部字符（打字补全）。</summary>
    public void RevealAllCharacters()
    {
        if (lineText != null)
            lineText.maxVisibleCharacters = 99999;
    }

    /// <summary>当前正文的可见字符总数（供打字机计算步长）。文本为空时 TMP 还没建 textInfo，返回 0。</summary>
    public int VisibleCharacterCount
    {
        get
        {
            if (lineText == null || string.IsNullOrEmpty(lineText.text))
                return 0;

            lineText.ForceMeshUpdate();
            return lineText.textInfo == null ? 0 : lineText.textInfo.characterCount;
        }
    }

    /// <summary>让 TMP 立刻完成文本解析（读取可见字符数前调用）。</summary>
    public void ForceMeshUpdate()
    {
        if (lineText != null)
            lineText.ForceMeshUpdate();
    }

    /// <summary>显示/隐藏「可以继续」提示。</summary>
    public void SetContinuePromptVisible(bool visible)
    {
        if (continuePrompt == null)
            return;

        if (visible && !continuePrompt.gameObject.activeSelf)
            continuePrompt.anchoredPosition = continuePromptBasePosition;

        continuePrompt.gameObject.SetActive(visible);
    }

    /// <summary>设置气泡底色（不同 NPC 可各用一套配色）。</summary>
    public void SetPanelColor(Color color)
    {
        if (panel != null)
            panel.color = color;
    }
}
