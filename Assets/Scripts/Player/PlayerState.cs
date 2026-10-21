using System;
using UnityEngine;

/// <summary>
/// 玩家的双资源状态组件：与 PlayerController / PlayerGroundSensor / PlayerAnimatorView
/// 挂在同一个 GameObject（Player）上，只负责「数值」与「数值变化的事件」，不定任何动作规则。
///
/// 双资源模型（策划案 3.1 定稿）：
/// - HP（生存资源）：上限 maxHP，关卡内不自动回复（策划案：仅在 Saya 处回满）。
///   受伤 / 死亡 / 读档恢复等规则由将来的受伤系统接入，本组件先托管数值与事件。
/// - SP（操作资源）：上限 maxSP，每秒自动恢复 spRegenPerSecond；消耗由动作门控发起，
///   归零后果是「无法执行对应操作」，不会死亡。
///
/// 消耗口径（策划案 3.1 待确认段建议）：当前 SP &lt; 消耗量时动作不执行。
/// 一次性动作（跳跃 / 闪避 / 攻击）用 <see cref="TryConsumeStamina"/>；
/// 持续动作（地面奔跑）用 <see cref="DrainStamina"/> 按秒扣，扣到 0 为止
/// （由 PlayerController 在 SP 归零时把奔跑降级为走路）。
///
/// HUD / 音效等表现层订阅 HealthChanged / StaminaChanged / Died 即可，本组件不直接碰 UI。
/// </summary>
public class PlayerState : MonoBehaviour
{
    [Header("生命 HP")]
    [Tooltip("生命上限。策划案定稿 100；关卡内不自动回复（仅在 Saya 处回满）。")]
    public float maxHP = 100f;

    [Header("体力 SP")]
    [Tooltip("体力上限。策划案定稿 100。")]
    public float maxSP = 100f;
    [Tooltip("体力每秒自动恢复量。策划案 v0.1 的「每秒 +1」因对核心跳跃动词惩罚过重已弃用；当前 8 为调校建议值（策划案方案 A 同值）。")]
    public float spRegenPerSecond = 8f;

    /// <summary>当前生命值。</summary>
    public float CurrentHP { get; private set; }

    /// <summary>当前体力值。</summary>
    public float CurrentSP { get; private set; }

    /// <summary>生命归一化值（0~1），供 HUD 血条使用。</summary>
    public float HP01 => maxHP <= 0f ? 0f : Mathf.Clamp01(CurrentHP / maxHP);

    /// <summary>体力归一化值（0~1），供 HUD 体力条使用。</summary>
    public float SP01 => maxSP <= 0f ? 0f : Mathf.Clamp01(CurrentSP / maxSP);

    /// <summary>体力是否已耗尽（PlayerController 把奔跑降级为走路的判定用）。</summary>
    public bool IsExhausted => CurrentSP <= 0f;

    /// <summary>是否已死亡（HP 归零）。死亡后的行为由受伤系统定义，本组件只标记。</summary>
    public bool IsDead { get; private set; }

    /// <summary>生命变化时触发，参数为（当前值, 上限）。</summary>
    public event Action<float, float> HealthChanged;

    /// <summary>体力变化时触发，参数为（当前值, 上限）。</summary>
    public event Action<float, float> StaminaChanged;

    /// <summary>HP 归零瞬间触发一次（复活后解除标记，直到再次死亡）。</summary>
    public event Action Died;

    private void Awake()
    {
        CurrentHP = maxHP;
        CurrentSP = maxSP;
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        maxHP = Mathf.Max(1f, maxHP);
        maxSP = Mathf.Max(1f, maxSP);
    }

    private void Update()
    {
        // 体力自动恢复：按秒补，补满即停（数值没变就不触发事件，免去 HUD 无谓刷新）。
        if (spRegenPerSecond > 0f && CurrentSP < maxSP)
            RestoreStamina(spRegenPerSecond * Time.deltaTime);
    }

    /// <summary>当前体力是否足够支付 cost（cost &lt;= 0 时恒为 true）。</summary>
    public bool HasStamina(float cost)
    {
        return CurrentSP >= cost;
    }

    /// <summary>
    /// 尝试一次性消耗 cost 点体力：足够则扣除并返回 true；不够则什么都不做并返回 false
    /// （不会部分扣除——策划案口径：当前 SP &lt; 消耗量时动作不执行）。
    /// </summary>
    public bool TryConsumeStamina(float cost)
    {
        if (CurrentSP < cost)
            return false;

        DrainStamina(cost);
        return true;
    }

    /// <summary>持续消耗（奔跑按秒扣）：扣到 0 为止，不会变负。</summary>
    public void DrainStamina(float amount)
    {
        if (amount <= 0f)
            return;

        float before = CurrentSP;
        CurrentSP = Mathf.Max(0f, CurrentSP - amount);
        if (!Mathf.Approximately(before, CurrentSP))
            StaminaChanged?.Invoke(CurrentSP, maxSP);
    }

    /// <summary>恢复体力（道具 / 技能等来源），不超过上限。</summary>
    public void RestoreStamina(float amount)
    {
        if (amount <= 0f)
            return;

        float before = CurrentSP;
        CurrentSP = Mathf.Min(maxSP, CurrentSP + amount);
        if (!Mathf.Approximately(before, CurrentSP))
            StaminaChanged?.Invoke(CurrentSP, maxSP);
    }

    /// <summary>
    /// 受到伤害：扣 HP 并触发 HealthChanged；HP 归零时把 IsDead 置真并触发一次 Died。
    /// 受伤无敌 / 击退 / 死亡行为留待受伤系统，本组件只出数值和事件。
    /// </summary>
    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsDead)
            return;

        float before = CurrentHP;
        CurrentHP = Mathf.Max(0f, CurrentHP - amount);
        if (!Mathf.Approximately(before, CurrentHP))
            HealthChanged?.Invoke(CurrentHP, maxHP);

        if (CurrentHP <= 0f)
        {
            IsDead = true;
            Died?.Invoke();
        }
    }

    /// <summary>恢复生命（如 Saya 泉水），不超过上限；从死亡状态恢复时解除死亡标记。</summary>
    public void Heal(float amount)
    {
        if (amount <= 0f)
            return;

        float before = CurrentHP;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
        if (!Mathf.Approximately(before, CurrentHP))
            HealthChanged?.Invoke(CurrentHP, maxHP);

        if (CurrentHP > 0f)
            IsDead = false;
    }

    /// <summary>HP / SP 全部回满并清除死亡标记（复活 / 回存档点时由外部调用）。</summary>
    public void ResetToFull()
    {
        bool hpChanged = !Mathf.Approximately(CurrentHP, maxHP);
        bool spChanged = !Mathf.Approximately(CurrentSP, maxSP);

        CurrentHP = maxHP;
        CurrentSP = maxSP;
        IsDead = false;

        if (hpChanged)
            HealthChanged?.Invoke(CurrentHP, maxHP);
        if (spChanged)
            StaminaChanged?.Invoke(CurrentSP, maxSP);
    }
}
