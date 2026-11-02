using TMPro;
using UnityEngine;

/// <summary>
/// 交互提示视图：进入 NPC 交互范围时显示在头顶的小气泡（例如一个写着 E 的小框）。
/// 同样只负责显示，是否显示由 <see cref="NpcDialogue"/> 决定。
/// </summary>
public class DialoguePromptView : MonoBehaviour
{
    [Header("结构")]
    [Tooltip("提示底板（Image / SpriteRenderer 都可，留空则只显示文字）。")]
    public UnityEngine.UI.Image panel;

    [Tooltip("提示文本。默认显示按键名，例如 E。")]
    public TextMeshProUGUI promptText;

    [Tooltip("Show() 未传文本时使用的默认文本。")]
    public string defaultText = "E";

    private void Awake()
    {
        if (promptText != null)
            promptText.text = defaultText;
    }

    /// <summary>显示提示（可临时覆盖文本）。</summary>
    public void Show(string overrideText = null)
    {
        gameObject.SetActive(true);

        if (promptText != null)
            promptText.text = string.IsNullOrEmpty(overrideText) ? defaultText : overrideText;
    }

    /// <summary>隐藏提示。</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
