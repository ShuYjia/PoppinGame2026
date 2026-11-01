using TMPro;
using UnityEngine;

/// <summary>
/// 告示牌 / 有咲的纸条（策划案 2.2 与 7.1 的教学点）：玩家走近时在牌子上方淡入提示文字，走远淡出。
/// 第一次读到时计 signScore 分（策划案 4.3.2：纸条 / 告示板 10 分）。
///
/// 需要：本物体上一个 isTrigger 的 Collider2D（就是「走近」的范围），子物体上一个 TextMeshPro 文本。
/// 注意：项目当前的 TMP 字体（LiberationSans）不含中文，提示先用英文；
/// 定了像素中文字体（策划案 8.3 待补）之后换字体资产即可直接写中文。
/// </summary>
public class TutorialSign : MonoBehaviour
{
    [Tooltip("提示文字（支持 TMP 富文本）。")]
    [TextArea(2, 5)]
    public string message = "Hello!";

    [Tooltip("显示提示的文本组件（通常是子物体）。")]
    public TMP_Text label;

    [Tooltip("淡入淡出速度（每秒透明度变化）。")]
    public float fadeSpeed = 6f;

    private bool playerNear;
    private bool counted;
    private float alpha;

    private void Awake()
    {
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = message;
            label.alpha = 0f;
        }
    }

    private void OnValidate()
    {
        if (label != null)
            label.text = message;
    }

    private void Update()
    {
        float target = playerNear ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
        if (label != null)
            label.alpha = alpha;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() == null)
            return;

        playerNear = true;
        if (!counted)
        {
            counted = true;
            if (LevelManager.Instance != null)
                LevelManager.Instance.AddSign();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() != null)
            playerNear = false;
    }
}
