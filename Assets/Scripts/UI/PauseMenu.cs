using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 暂停菜单（策划案 8.1 界面清单 / 8.2：Esc = 暂停菜单）。
///
/// 打开 / 关闭：Esc / 手柄 Start（PS: Options，Xbox: Menu）。通关结算面板出现后不再响应。
/// 菜单里：↑↓ / W S / 十字键 / 左摇杆 选择；Enter / 空格 / 手柄南键（Ⓐ / ×）确认；
///         手柄东键（Ⓑ / ○）直接继续。
/// 菜单项：继续游戏 / 重新开始 / 退出游戏。还没有标题画面和设置界面，做好之后在 <see cref="Option"/> 里加项。
///
/// 暂停 = Time.timeScale 归零（计时、物理、敌人、动画全部停住）+ 锁玩家输入 + 暂停音频。
/// 输入直接读设备，不依赖 EventSystem。确认键不用 J：J 是攻击键，按 J 关菜单会顺手打出一发攻击。
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Tooltip("整个暂停界面（打开时显示，平时隐藏）。")]
    public GameObject panel;

    [Tooltip("菜单项文本，顺序与 Option 一致：继续游戏 / 重新开始 / 退出游戏。")]
    public TMP_Text[] items;

    public Color normalColor = new Color(0.78f, 0.78f, 0.84f);
    public Color selectedColor = new Color(1f, 0.85f, 0.3f);

    private enum Option
    {
        Resume,
        Restart,
        Quit,
    }

    private static readonly string[] Labels = { "继续游戏", "重新开始", "退出游戏" };

    // 未选中的项两侧放透明的箭头占位，选中时箭头显形，文字位置不会左右跳。
    private const string Hidden = "<color=#00000000>";

    public bool IsPaused { get; private set; }

    private int selected;
    private bool stickHeld;
    private bool playerWasLocked;
    private PlayerController lockedPlayer;

    private void Start()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    private void OnDestroy()
    {
        // 暂停中切场景（暂停时按 R 重开等）：别把 timeScale = 0 带进下一个场景。
        if (IsPaused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }

    private void Update()
    {
        if (!IsPaused)
        {
            LevelManager level = LevelManager.Instance;
            if (WasTogglePressed() && (level == null || !level.IsCompleted))
                Pause();
            return;
        }

        if (WasTogglePressed() || WasBackPressed())
        {
            Resume();
            return;
        }

        int move = ReadNavigation();
        if (move != 0 && items != null && items.Length > 0)
        {
            selected = (selected + move + items.Length) % items.Length;
            Refresh();
        }

        if (WasConfirmPressed())
            Activate((Option)selected);
    }

    public void Pause()
    {
        if (IsPaused)
            return;

        IsPaused = true;
        selected = 0;
        stickHeld = true; // 摇杆要先回中，才开始算一次「拨动」
        Refresh();
        if (panel != null)
            panel.SetActive(true);

        Time.timeScale = 0f;
        AudioListener.pause = true;

        LevelManager level = LevelManager.Instance;
        lockedPlayer = level != null && level.player != null ? level.player : FindObjectOfType<PlayerController>();
        if (lockedPlayer != null)
        {
            playerWasLocked = lockedPlayer.InputLocked;
            lockedPlayer.SetInputLocked(true);
        }
    }

    public void Resume()
    {
        if (!IsPaused)
            return;

        IsPaused = false;
        if (panel != null)
            panel.SetActive(false);

        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (lockedPlayer != null)
            lockedPlayer.SetInputLocked(playerWasLocked);
        lockedPlayer = null;
    }

    private void Activate(Option option)
    {
        switch (option)
        {
            case Option.Resume:
                Resume();
                break;

            case Option.Restart:
                Resume();
                if (LevelManager.Instance != null)
                    LevelManager.Instance.RestartLevel();
                break;

            case Option.Quit:
                Resume();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
        }
    }

    private void Refresh()
    {
        if (items == null)
            return;

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null)
                continue;
            string label = i < Labels.Length ? Labels[i] : items[i].text;
            bool on = i == selected;
            items[i].text = on
                ? "▶  " + label + "  ◀"
                : Hidden + "▶</color>  " + label + "  " + Hidden + "◀</color>";
            items[i].color = on ? selectedColor : normalColor;
        }
    }

    // ───────────────────────────── 输入 ─────────────────────────────

    private static bool WasTogglePressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.startButton.wasPressedThisFrame;
    }

    private static bool WasBackPressed()
    {
        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonEast.wasPressedThisFrame;
    }

    private static bool WasConfirmPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                                 || keyboard.spaceKey.wasPressedThisFrame))
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
    }

    /// <summary>本帧的上下选择：-1 = 上一项，1 = 下一项，0 = 没动。</summary>
    private int ReadNavigation()
    {
        int move = 0;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
                move = -1;
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
                move = 1;
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            if (gamepad.dpad.up.wasPressedThisFrame)
                move = -1;
            else if (gamepad.dpad.down.wasPressedThisFrame)
                move = 1;

            // 摇杆：推过 0.6 算拨一下，回到 0.3 以内才能拨下一下（不做按住连跳）。
            float y = gamepad.leftStick.ReadValue().y;
            if (!stickHeld && Mathf.Abs(y) > 0.6f)
            {
                stickHeld = true;
                move = y > 0f ? -1 : 1;
            }
            else if (stickHeld && Mathf.Abs(y) < 0.3f)
            {
                stickHeld = false;
            }
        }

        return move;
    }
}
