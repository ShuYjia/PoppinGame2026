using UnityEngine;

/// <summary>
/// 【临时调试用】在 Game 视图左上角显示玩家 HP / SP 与当前运动状态，方便在正式 HUD 做好之前
/// 直接观察体力消耗是否符合预期。
///
/// 只读取 PlayerState / PlayerController，不参与任何玩法逻辑；正式 HUD 接入后
/// 删除本脚本并移除 Player 上的该组件即可，不影响其它系统。
/// </summary>
[RequireComponent(typeof(PlayerState))]
public class PlayerStateDebugView : MonoBehaviour
{
    [Tooltip("是否显示调试面板。")]
    public bool visible = true;
    [Tooltip("面板左上角偏移（像素，相对屏幕左上角）。")]
    public Vector2 screenOffset = new Vector2(12f, 12f);

    private PlayerState state;
    private PlayerController controller;
    private GUIStyle boldStyle;
    private GUIStyle smallStyle;

    private void Awake()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (state == null)
            state = GetComponent<PlayerState>();
        if (controller == null)
            controller = GetComponent<PlayerController>();
    }

    private void OnGUI()
    {
        if (!visible)
            return;

        Refresh();
        if (state == null)
            return;

        EnsureStyles();

        const float width = 264f;
        const float height = 100f;
        Rect panel = new Rect(screenOffset.x, screenOffset.y, width, height);
        GUI.Box(panel, GUIContent.none);

        GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, width - 20f, 20f),
            "HP " + state.CurrentHP.ToString("F0") + " / " + state.maxHP.ToString("F0"), boldStyle);

        // 体力条：耗尽时变红，便于确认「跑空 → 锁定走路」。
        Rect barBackground = new Rect(panel.x + 10f, panel.y + 31f, width - 20f, 12f);
        Rect barFill = new Rect(barBackground.x, barBackground.y,
            barBackground.width * state.SP01, barBackground.height);
        Color previous = GUI.color;
        GUI.color = new Color(0.15f, 0.15f, 0.18f, 0.9f);
        GUI.DrawTexture(barBackground, Texture2D.whiteTexture);
        GUI.color = state.IsExhausted ? new Color(0.85f, 0.3f, 0.25f) : new Color(0.35f, 0.85f, 0.5f);
        GUI.DrawTexture(barFill, Texture2D.whiteTexture);
        GUI.color = previous;

        GUI.Label(new Rect(panel.x + 10f, panel.y + 46f, width - 20f, 20f),
            "SP " + state.CurrentSP.ToString("F1") + " / " + state.maxSP.ToString("F0"), boldStyle);

        GUI.Label(new Rect(panel.x + 10f, panel.y + 70f, width - 20f, 22f), DescribeState(), smallStyle);
    }

    /// <summary>一行摘要：运动状态 · 当前恢复速度（蹲下时显示倍率）。</summary>
    private string DescribeState()
    {
        string motion = "-";
        if (controller != null)
        {
            if (controller.IsDodging)
                motion = "闪避";
            else if (!controller.IsGrounded)
                motion = "空中";
            else if (controller.IsCrouching)
                motion = "蹲下";
            else if (controller.IsRunning)
                motion = "奔跑（扣体力）";
            else if (controller.IsWalking)
                motion = "走路（不扣）";
            else
                motion = "静止";
        }

        string regen = "恢复 +" + state.spRegenPerFrame.ToString("F2") + "/帧";
        if (state.IsCrouching)
            regen += " ×" + state.crouchRegenMultiplier.ToString("F1");
        if (state.IsExhausted)
            regen += "  · 体力耗尽";

        return motion + "  ·  " + regen;
    }

    private void EnsureStyles()
    {
        if (boldStyle != null)
            return;

        boldStyle = new GUIStyle(GUI.skin.label);
        boldStyle.fontSize = 15;
        boldStyle.fontStyle = FontStyle.Bold;
        boldStyle.normal.textColor = Color.white;

        smallStyle = new GUIStyle(GUI.skin.label);
        smallStyle.fontSize = 12;
        smallStyle.normal.textColor = new Color(0.85f, 0.85f, 0.9f);
    }
}
