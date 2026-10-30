using TMPro;
using UnityEngine;

/// <summary>
/// 关卡 HUD：星屑数量、当前分数、计时（策划案 8.1 HUD 清单），以及通关后的结算面板
/// （策划案 4.3.6 建议：三块分数分开显示，玩家一眼能看出慢在哪、漏了什么）。
///
/// 只读 <see cref="LevelManager"/> 的公开数值来显示，不改任何游戏状态。
/// 文本先用英文：当前 TMP 字体不含中文（见 TutorialSign 的说明）。
/// </summary>
public class LevelHUD : MonoBehaviour
{
    [Header("常驻 HUD")]
    public TMP_Text starsText;
    public TMP_Text scoreText;
    public TMP_Text timeText;

    [Header("结算面板")]
    public GameObject resultPanel;
    public TMP_Text resultBody;
    public TMP_Text resultTotal;
    public TMP_Text resultHint;

    [Header("提示条")]
    [Tooltip("复活 / 存档时短暂显示的一行字（可留空）。")]
    public TMP_Text toastText;
    public float toastDuration = 1.5f;

    private LevelManager level;
    private float toastTimer;
    private bool subscribed;

    private void Start()
    {
        level = LevelManager.Instance;
        if (resultPanel != null)
            resultPanel.SetActive(false);
        if (toastText != null)
            toastText.alpha = 0f;

        if (level != null)
        {
            level.Completed += ShowResult;
            level.CheckpointReached += OnCheckpoint;
            subscribed = true;
        }
    }

    private void OnDestroy()
    {
        if (subscribed && level != null)
        {
            level.Completed -= ShowResult;
            level.CheckpointReached -= OnCheckpoint;
        }
    }

    private void Update()
    {
        if (level == null)
            return;

        if (starsText != null)
            starsText.text = $"STARS  {level.Stars} / {level.TotalStars}";
        if (scoreText != null)
            scoreText.text = $"SCORE  {level.RunningScore}";
        if (timeText != null)
            timeText.text = $"TIME  {FormatTime(level.ElapsedTime)}";

        if (toastText != null)
        {
            if (level.IsRespawning)
            {
                toastText.text = "Respawning...";
                toastTimer = 0.3f;
            }
            toastTimer -= Time.deltaTime;
            toastText.alpha = Mathf.Clamp01(toastTimer / 0.3f);
        }
    }

    private void OnCheckpoint(Vector2 position)
    {
        if (toastText == null)
            return;
        toastText.text = "Checkpoint!";
        toastTimer = toastDuration;
    }

    private void ShowResult(LevelManager.LevelResult r)
    {
        if (resultPanel == null)
            return;

        resultPanel.SetActive(true);

        if (resultBody != null)
        {
            // <pos> 做列对齐：比例字体下用空格对不齐。
            string combo = r.comboBonus > 0 ? $"  <size=80%>(combo +{r.comboBonus})</size>" : string.Empty;
            resultBody.text =
                $"Kills<pos=34%>{r.kills}{combo}<pos=78%>{r.killPoints}\n" +
                $"Stars<pos=34%>{r.stars} / {r.totalStars}<pos=78%>{r.starPoints}\n" +
                $"Signs<pos=34%>{r.signs}<pos=78%>{r.signPoints}\n" +
                $"Time<pos=34%>{FormatTime(r.timeSeconds)}<pos=78%>{r.timeBonus}\n" +
                $"<size=80%><color=#B0B0B0>Deaths<pos=34%>{r.deaths}</color></size>";
        }
        if (resultTotal != null)
            resultTotal.text = $"TOTAL  {r.total}";
        if (resultHint != null)
            resultHint.text = "Press  Enter / A (Cross)  to play again";
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}
