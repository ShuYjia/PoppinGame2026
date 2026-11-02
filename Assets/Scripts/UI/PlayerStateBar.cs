using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家状态条渲染：把 UIManager 转发来的数值画到条上，只管画，不订阅事件、不读游戏状态。
/// 血条两层：HealthFillRed（红条，恒定落后绿条 1 个 Time.deltaTime×delaySpeed 的距离）/ HealthFillGreen（绿条，当前 HP）；
/// 体力条：PowerFillYellow（当前 SP）。
/// </summary>
public class PlayerStateBar : MonoBehaviour
{
    [Header("血条")]
    public Image healthImage;        // 绿条：当前 HP
    public Image healthDelayImage;   // 红条：受击延迟结算，慢半拍追上绿条
    [Tooltip("红条追赶绿条的速度（每秒的填充比例）。红条恒定落后绿条 1 个 Time.deltaTime×本值 的距离：\n1 = 每秒追满整条（30% 的落差 0.3 秒追完）；调大更快、调小更慢。")]
    public float delaySpeed = 1f;

    [Header("体力条")]
    public Image powerImage;         // 黄条：当前 SP

    [Header("血量数值")]
    [Tooltip("显示「当前血量 / 最大血量」的 TMP 文本（HealthBar/AccurateHealth）。\n未赋值时静默跳过——FirstLevel 场景（合作者在改）里就是这个状态，不能因为少一个引用让每次掉血都抛异常。")]
    public TextMeshProUGUI accurateHealth;

    /// <summary>UIManager 在 HP 变化时调用：更新绿条与数值文本，红条由 Update 每帧自行收敛过去。</summary>
    public void SetHealth(float current, float max)
    {
        healthImage.fillAmount = max <= 0f ? 0f : Mathf.Clamp01(current / max);

        // 数值文本：显示「当前 / 上限」。判空是因为 FirstLevel 场景（合作者在改）里这个字段尚未接线，
        // 少一个引用不该让每次掉血都抛 NullReferenceException。
        if (accurateHealth != null)
            accurateHealth.text = Mathf.RoundToInt(current) + " / " + Mathf.RoundToInt(max);
    }

    /// <summary>UIManager 在 SP 变化时调用：传当前值与上限。</summary>
    public void SetPower(float current, float max)
    {
        powerImage.fillAmount = max <= 0f ? 0f : Mathf.Clamp01(current / max);
    }

    private void Update()
    {
        // 红条每帧朝绿条移动固定步长（deltaTime × delaySpeed），因此恒定落后绿条一个步长：
        // 掉血时慢一拍往下追、回血时慢一拍往上追，不会卡住也不会穿过绿条。
        healthDelayImage.fillAmount = Mathf.MoveTowards(
            healthDelayImage.fillAmount, healthImage.fillAmount, Time.deltaTime * delaySpeed);
    }
}
