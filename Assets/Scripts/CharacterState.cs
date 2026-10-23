using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 角色状态组件：**玩家与敌人共用同一套血量（HP）计算**。
///
/// 挂法：谁要「有血、能被打、能死」，就给谁挂一个本组件
/// （玩家挂 Player 上，Boar / 敌人挂自己身上）。
/// 它只负责「数值」与「数值变化的事件」，不定任何动作规则——
/// 谁打谁由 <see cref="Attack"/> 决定，被打之后怎么办（击退、闪白、死亡表现）由各自的控制器决定。
///
/// 双资源模型（策划案 3.1 定稿）：
/// - HP（生存资源）：上限 maxHP，关卡内不自动回复（策划案：仅在 Saya 处回满）。
/// - SP（操作资源）：上限 maxSP，**每帧**自动恢复 spRegenPerFrame（数值按 60fps 基准填，
///   运行时乘 Time.deltaTime × ReferenceFps 换算，帧率高低下手感一致）；
///   蹲下时恢复速度再乘 crouchRegenMultiplier（玩家可通过蹲下加速回体力）。
///   消耗由动作门控发起，归零后果是「无法执行对应操作」，不会死亡。
///   **SP 是玩家侧的资源**：敌人挂本组件时不参与任何 SP 逻辑（没有消耗体力的动作会去读它），
///   字段留着即可，也可以拿来当敌人的耐力 / 硬直条。
///
/// 消耗口径（策划案 3.1 待确认段建议）：当前 SP &lt; 消耗量时动作不执行。
/// 一次性动作（跳跃 / 闪避 / 攻击）用 <see cref="TryConsumeStamina"/>；
/// 持续动作（地面奔跑）用 <see cref="DrainStamina"/> 按帧扣，扣到 0 为止
/// （由 PlayerController 在奔跑中 SP 归零时锁定为走路，直到玩家重新从走路进入奔跑）。
///
/// 受伤无敌（按来源，敌我通用）：
/// 受到「某个来源」的伤害后，在 <see cref="invincibleTime"/> 秒内免疫**同一来源**的再次伤害；
/// 换一个来源（另一只 Boar、另一颗子弹）可以立刻打中。这样既不会出现「贴着 Boar 每帧掉血」，
/// 也不会让玩家在怪堆里白嫖全局无敌。
/// 来源按 GameObject 归一（传 Component 时取它所在的物体），所以同一只 Boar 身上挂的多个
/// Attack 组件算同一来源，共用一份冷却。
/// 战斗伤害统一走 <see cref="TakeDamage(Attack)"/> 或 <see cref="TakeDamage(float, UnityEngine.Object)"/>；
/// 敌人若希望「每次被打都生效」，把 invincibleTime 设为 0。
///
/// 全局无敌（闪避无敌帧）：
/// 与按来源无敌相互独立，共两条互不相同的路径——
/// 1. **持有型**（闪避专用，<see cref="HoldGlobalInvincible"/>）：纯开关、无计时器。
///    PlayerController 在闪避开始时持有、IsDodging 结束瞬间同步解除——无敌帧就是
///    「闪避进行中」本身，与闪避状态同生同灭，不存在两个倒计时源的帧级不同步。
/// 2. **计时器型**（<see cref="SetGlobalInvincible"/>，预留）：duration 秒内免疫所有来源、
///    所有形式的伤害（连无来源的绝对伤害也一并挡掉），到点自动解除，不依赖外部调用收尾。
///    留给将来的技能 / 道具 / 过场，**闪避不走这条路**。
/// 两条路径互不影响，与按来源无敌字典三者彻底分离，结构上不可能共用同一个计时器。
///
/// 蹲下状态由 PlayerController 每帧通过 <see cref="SetCrouching"/> 同步（本组件不反向查询控制器）。
/// 上限 / 恢复量的成长接口见文件末尾「成长扩展点」（占位，逻辑留空）。
///
/// HUD / 音效等表现层订阅 HealthChanged / StaminaChanged / Died 即可，本组件不直接碰 UI。
/// 受伤表现（动画 / 击退）订阅 <see cref="OnTakeDamage"/>：只有真正扣血才触发，参数即攻击者 Transform。
/// </summary>
public class CharacterState : MonoBehaviour
{
    [Header("生命 HP")]
    [Tooltip("生命上限。策划案定稿 100；关卡内不自动回复（仅在 Saya 处回满）。")]
    public float maxHP = 100f;

    [Header("体力 SP")]
    [Tooltip("体力上限。策划案定稿 100。")]
    public float maxSP = 100f;
    [Tooltip("每帧自动恢复的体力（60fps 基准，运行时按帧时长换算，60Hz / 144Hz 手感一致）。0.1 ≈ 6/秒。")]
    public float spRegenPerFrame = 0.1f;
    [Tooltip("蹲下时的恢复倍率：蹲下每帧恢复 spRegenPerFrame × 本值。1 表示蹲下不额外加成。")]
    public float crouchRegenMultiplier = 2f;

    /// <summary>
    /// 「每帧数值」的基准帧率：Inspector 里填的数字都是 60fps 下的每帧增量。
    /// 运行时按 Time.deltaTime × 本值换算，避免高刷新率（如 144Hz）把恢复 / 消耗放大 2 倍以上。
    /// </summary>
    public const float ReferenceFps = 60f;

    [Header("受伤无敌")]
    [Tooltip("受到某个来源的伤害后，免疫「同一来源」再次伤害的时长（秒）。\n0 = 关闭无敌（每次接触都掉血）。敌人若希望每次被打都掉血，把本值设为 0。")]
    public float invincibleTime = 1f;

    [Header("运行时数值（Play 中可直接修改，用于调试）")]
    [Tooltip("当前生命值。Play 中直接改这里即可构造受伤 / 死亡测试；直接改字段不会触发 HealthChanged 事件（调试面板每帧读取，显示会立即跟上），退出 Play 后还原为进 Play 时的值。")]
    public float CurrentHP;

    [Tooltip("当前体力值。Play 中直接改这里即可构造体力门控测试（如设成 5 再跳跃 / 闪避 / 攻击）；直接改字段不会触发 StaminaChanged 事件（调试面板每帧读取，显示会立即跟上），退出 Play 后还原为进 Play 时的值。")]
    public float CurrentSP;

    [Header("受伤事件")]
    [Tooltip("「真正扣到血」的瞬间触发，参数是伤害来源的 Transform（攻击者的位置，用于算击退方向）。\n在 Inspector 里挂监听即可扩展表现：播放受伤动画、触发受击技能、播放音效、屏幕震动等。\n注意三种情况不会触发：①被无敌帧 / 同一来源无敌挡下；②已死亡；③无来源的绝对伤害（没有攻击者可传）。\n触发顺序：先扣血并触发 HealthChanged（致死时再触发 Died），最后才触发本事件——监听者读到的是扣血之后的 HP。")]
    public UnityEvent<Transform> OnTakeDamage;

    /// <summary>生命归一化值（0~1），供 HUD 血条使用。</summary>
    public float HP01 => maxHP <= 0f ? 0f : Mathf.Clamp01(CurrentHP / maxHP);

    /// <summary>体力归一化值（0~1），供 HUD 体力条使用。</summary>
    public float SP01 => maxSP <= 0f ? 0f : Mathf.Clamp01(CurrentSP / maxSP);

    /// <summary>体力是否已耗尽（PlayerController 在奔跑中据此把奔跑档锁定为走路）。</summary>
    public bool IsExhausted => CurrentSP <= 0f;

    /// <summary>是否已死亡（HP 归零）。死亡后的行为由各自控制器定义，本组件只标记。</summary>
    public bool IsDead { get; private set; }

    /// <summary>是否蹲下（由 PlayerController 每帧同步，决定恢复倍率）。</summary>
    public bool IsCrouching { get; private set; }

    /// <summary>生命变化时触发，参数为（当前值, 上限）。</summary>
    public event Action<float, float> HealthChanged;

    /// <summary>体力变化时触发，参数为（当前值, 上限）。</summary>
    public event Action<float, float> StaminaChanged;

    /// <summary>HP 归零瞬间触发一次（复活后解除标记，直到再次死亡）。</summary>
    public event Action Died;

    /// <summary>伤害来源 ID → 该来源剩余的无敌时间（秒）。来源按 GameObject 归一，见 <see cref="GetSourceKey"/>。</summary>
    private readonly Dictionary<int, float> invincibilityTimers = new Dictionary<int, float>();

    // 每帧清理到期项时的暂存列表（避免在遍历中改字典）。
    private readonly List<int> expiredSources = new List<int>();

    // 每帧推进计时前的键快照列表（避免在遍历字典时用索引器改值——
    // Mono 的 Dictionary 索引器赋值同样会使枚举器失效，下一轮 MoveNext 抛
    // "Collection was modified" 异常，导致计时/清理整套逻辑被整帧打断）。
    private readonly List<int> activeSources = new List<int>();

    /// <summary>
    /// 全局无敌剩余时间（秒）：&gt; 0 时免疫所有来源、所有形式的伤害（计时器型全局无敌）。
    /// 与按来源无敌相互独立，两者互不影响；由 <see cref="SetGlobalInvincible"/> 开启，
    /// 每帧倒计时，到点自动归零（见 <see cref="Update"/>）。
    /// 注意：**闪避无敌帧不走这条路**（闪避用 <see cref="globalInvincibleHold"/> 持有型开关），
    /// 计时器型留给将来的技能 / 道具。
    /// </summary>
    private float globalInvincibleTimer;

    /// <summary>
    /// 持有型全局无敌（闪避无敌帧专用）：纯开关，无计时器、无时长参数。
    /// true 期间免疫所有来源、所有形式的伤害；由 PlayerController 在闪避期间持有、
    /// 闪避结束（IsDodging = false）瞬间同步解除——无敌帧与闪避状态同生同灭。
    /// 与 <see cref="globalInvincibleTimer"/>（计时器型）、按来源无敌字典三套机制相互独立。
    /// </summary>
    private bool globalInvincibleHold;

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
        invincibleTime = Mathf.Max(0f, invincibleTime);

        // 编辑模式下把当前值同步成上限，只为 Inspector 观感与直觉一致：
        // CurrentHP / CurrentSP 是序列化字段，首次保存时会是 0，显示「0 / 100」容易误以为坏了。
        // 进 Play 时 Awake 本来也会重置为满值，所以编辑模式下这两个值不承载状态。
        // 必须在 Application.isPlaying 早退之后——Play 中手动改当前值不能被覆盖，
        // 否则调试改动会被立刻抹掉。
        CurrentHP = maxHP;
        CurrentSP = maxSP;
    }

    private void Update()
    {
        // 体力自动恢复：每帧结算一次（帧口径），并按 60fps 基准换算时长。
        // 注意：不能只按帧数硬加——高刷新率会等比放大恢复量（144Hz 下 0.15/帧 = 21.6/秒），
        // 会直接盖过奔跑消耗（12/秒），表现为「一直跑体力却是满的」。
        // 蹲下时按 crouchRegenMultiplier 加速；补满即停，数值没变就不触发事件。
        if (spRegenPerFrame > 0f && CurrentSP < maxSP)
        {
            float multiplier = IsCrouching ? Mathf.Max(0f, crouchRegenMultiplier) : 1f;
            RestoreStamina(spRegenPerFrame * multiplier * (Time.deltaTime * ReferenceFps));
        }

        TickInvincibility(Time.deltaTime);

        // 全局无敌（闪避无敌帧）倒计时：到点自动解除，即使外部忘记收尾也不会永久无敌。
        if (globalInvincibleTimer > 0f)
            globalInvincibleTimer = Mathf.Max(0f, globalInvincibleTimer - Time.deltaTime);
    }

    // ───────────────────────── 受伤无敌（按来源） ─────────────────────────

    /// <summary>
    /// 推进所有来源的无敌计时，并清掉到期项。
    /// 来源物体被销毁后，它的计时会在到期时自然移除，字典不会无限膨胀。
    /// 注意：先在快照列表里收集键，再在遍历之外更新/删除——字典的索引器赋值
    /// 在 Mono 上也会废掉正在进行的枚举器（实测每帧抛 InvalidOperationException），
    /// 所以绝不能在 foreach 里写 invincibilityTimers[key] = ...。
    /// </summary>
    private void TickInvincibility(float deltaTime)
    {
        if (invincibilityTimers.Count == 0)
            return;

        expiredSources.Clear();
        activeSources.Clear();
        foreach (KeyValuePair<int, float> entry in invincibilityTimers)
            activeSources.Add(entry.Key);

        for (int i = 0; i < activeSources.Count; i++)
        {
            int key = activeSources[i];
            float remaining = invincibilityTimers[key] - deltaTime;
            if (remaining <= 0f)
                expiredSources.Add(key);
            else
                invincibilityTimers[key] = remaining;
        }

        for (int i = 0; i < expiredSources.Count; i++)
            invincibilityTimers.Remove(expiredSources[i]);
        expiredSources.Clear();
    }

    /// <summary>
    /// 是否正处于对 source 的无敌期（source 为 null 或 invincibleTime 为 0 时恒为 false）。
    /// </summary>
    public bool IsInvincibleAgainst(UnityEngine.Object source)
    {
        if (invincibleTime <= 0f || source == null)
            return false;

        return invincibilityTimers.ContainsKey(GetSourceKey(source));
    }

    /// <summary>清除所有来源的无敌计时（复活 / 过场等需要立刻解除保护时调用）。</summary>
    public void ClearInvincibility()
    {
        invincibilityTimers.Clear();
    }

    // ───────────────────────── 全局无敌（闪避无敌帧） ─────────────────────────

    /// <summary>
    /// 是否处于全局无敌：持有型开关（闪避）或计时器型（技能 / 道具）任一命中即免疫所有来源的伤害。
    /// 三个 TakeDamage 入口统一走本判断，无需各自分支。
    /// </summary>
    public bool IsGloballyInvincible => globalInvincibleHold || globalInvincibleTimer > 0f;

    /// <summary>
    /// 持有 / 解除持有型全局无敌（**闪避无敌帧专用**，不用计时器）。
    /// hold = true：持有期间无法掉血——免疫所有来源、所有形式的伤害（不扣血、不触发事件、
    /// 不给来源挂冷却）；hold = false：立即解除。
    /// 由 PlayerController 驱动：闪避开始（<see cref="PlayerController.RequestDodge"/>）时置 true，
    /// 闪避结束（IsDodging = false）瞬间置 false——无敌帧与闪避状态严格同生同灭，
    /// 玩家在闪避过程中不可能掉血，闪避一结束立刻恢复可受伤。
    /// </summary>
    public void HoldGlobalInvincible(bool hold)
    {
        globalInvincibleHold = hold;
    }

    /// <summary>
    /// 开启一段计时器型全局无敌：duration 秒内免疫所有来源、所有形式的伤害
    /// （不扣血、不触发事件、不给来源挂冷却）。持续调用会刷新剩余时间；
    /// duration &lt;= 0 等价于立即解除（见 <see cref="ClearGlobalInvincibility"/>）。
    /// 预留给技能 / 道具 / 过场；**闪避无敌帧请用 <see cref="HoldGlobalInvincible"/>**，
    /// 两条路径互不影响。
    /// </summary>
    public void SetGlobalInvincible(float duration)
    {
        globalInvincibleTimer = Mathf.Max(0f, duration);
    }

    /// <summary>立即解除全局无敌（过场 / 调试用；正常解锁靠计时自然到期）。</summary>
    public void ClearGlobalInvincibility()
    {
        globalInvincibleTimer = 0f;
    }

    /// <summary>
    /// 伤害来源的归一键：Component 与 GameObject 归到同一个 GameObject 上，
    /// 这样「同一只 Boar 身上挂的多个 Attack」会共用一份无敌冷却，符合「同一来源」的语义。
    /// </summary>
    private static int GetSourceKey(UnityEngine.Object source)
    {
        if (source == null)
            return 0;

        Component component = source as Component;
        if (component != null)
            return component.gameObject.GetInstanceID();

        return source.GetInstanceID();
    }

    // ───────────────────────── 蹲下 / 体力 ─────────────────────────

    /// <summary>
    /// 同步蹲下状态（PlayerController 每帧调用）：蹲下时体力恢复按 crouchRegenMultiplier 加速。
    /// 由控制器推送而不是本组件反向查询，保持「状态组件只被查询、不反向依赖」的分层。
    /// </summary>
    public void SetCrouching(bool crouching)
    {
        IsCrouching = crouching;
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
    /// 本重载**不受受伤无敌保护**（无来源 = 绝对伤害）：剧情扣血、环境伤害、调试直接扣血走这里。
    /// 但全局无敌（闪避无敌帧）期间连绝对伤害也一并免疫——无敌帧的语义就是「这一刻不该掉血」。
    /// 战斗伤害请用 <see cref="TakeDamage(Attack)"/> 或 <see cref="TakeDamage(float, UnityEngine.Object)"/>。
    /// 击退 / 受击表现留待受伤系统，本组件只出数值和事件。
    /// </summary>
    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsDead)
            return;

        // 全局无敌（含闪避持有型开关）：连无来源的绝对伤害也一并免疫。
        if (IsGloballyInvincible)
            return;

        ApplyDamage(amount);
    }

    /// <summary>
    /// 受到 attacker 造成的伤害（**战斗伤害的统一入口**：<c>TakeDamage(this)</c> 这样调用即可）。
    /// 伤害值取 attacker.damage，来源取 attacker 所在的 GameObject，因此同一只 Boar 身上
    /// 挂的所有 Attack 共用一份无敌冷却。
    /// 若正处于对该来源的无敌期、或处于全局无敌（闪避无敌帧 / 技能无敌）中，本次伤害
    /// **完全不发生**（不扣血、不触发 HealthChanged、不触发 <see cref="OnTakeDamage"/>）并返回 false。
    /// </summary>
    /// <returns>是否真的造成了伤害。</returns>
    public bool TakeDamage(Attack attacker)
    {
        if (attacker == null)
            return false;

        // 事件由带来源重载统一触发（真正扣血之后），这里不再单独 Invoke——
        // 否则无敌帧挡下的伤害也会播受伤动画 / 触发击退。
        return TakeDamage(attacker.damage, attacker);
    }

    /// <summary>
    /// 受到 source 造成的伤害（通用的带来源重载）。
    /// 若正处于对该来源的无敌期，本次伤害**完全不发生**（不扣血、不触发事件）并返回 false；
    /// 否则扣血、触发 <see cref="OnTakeDamage"/>（参数为来源 Transform），
    /// 再给该来源挂上 invincibleTime 秒的无敌，返回 true。
    /// 换一个来源不受本冷却影响——这正是「同一来源无敌」的口径。
    /// </summary>
    /// <param name="amount">伤害数值。</param>
    /// <param name="source">伤害来源（传 GameObject 或 Component 都行，内部归一到 GameObject）。
    /// 传 null 等价于调用无来源重载（不受无敌保护，也不触发 <see cref="OnTakeDamage"/>——没有攻击者可传）。</param>
    /// <returns>是否真的造成了伤害。</returns>
    public bool TakeDamage(float amount, UnityEngine.Object source)
    {
        if (amount <= 0f || IsDead)
            return false;

        // 全局无敌（闪避持有型开关或计时器型）：拒绝一切战斗伤害——不扣血、不触发事件、不给来源挂冷却。
        if (IsGloballyInvincible)
            return false;

        if (source == null)
        {
            ApplyDamage(amount);
            return true;
        }

        if (IsInvincibleAgainst(source))
            return false;

        ApplyDamage(amount);

        // 受伤事件：走到这里说明伤害真的生效了（无敌没挡住、且确实扣了血）。
        // 触发顺序在 HealthChanged / Died 之后——监听者读到的 CurrentHP 已经是扣血后的值。
        OnTakeDamage?.Invoke(ResolveSourceTransform(source));

        if (invincibleTime > 0f)
            invincibilityTimers[GetSourceKey(source)] = invincibleTime;

        return true;
    }

    /// <summary>
    /// 把伤害来源还原成 Transform（供 <see cref="OnTakeDamage"/> 传出攻击者位置，用于算击退方向）：
    /// Component 与 GameObject 都取它自己的 transform，其余类型取不到就返回 null
    /// （监听方需要对 null 做兜底，见 <see cref="PlayerController.GetHurt"/>）。
    /// </summary>
    private static Transform ResolveSourceTransform(UnityEngine.Object source)
    {
        Component component = source as Component;
        if (component != null)
            return component.transform;

        GameObject gameObject = source as GameObject;
        return gameObject != null ? gameObject.transform : null;
    }

    /// <summary>实际扣血 + 事件 + 死亡判定的内部实现（不判无敌）。</summary>
    private void ApplyDamage(float amount)
    {
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

    /// <summary>HP / SP 全部回满、清除死亡标记与所有无敌计时（复活 / 回存档点时由外部调用）。</summary>
    public void ResetToFull()
    {
        bool hpChanged = !Mathf.Approximately(CurrentHP, maxHP);
        bool spChanged = !Mathf.Approximately(CurrentSP, maxSP);

        CurrentHP = maxHP;
        CurrentSP = maxSP;
        IsDead = false;
        invincibilityTimers.Clear();
        globalInvincibleTimer = 0f;
        globalInvincibleHold = false;

        if (hpChanged)
            HealthChanged?.Invoke(CurrentHP, maxHP);
        if (spChanged)
            StaminaChanged?.Invoke(CurrentSP, maxSP);
    }

    // ───────────────────────── 成长扩展点（占位，逻辑留空） ─────────────────────────
    // 玩家后续可能通过技能 / 道具 / 存档加成提升上限与恢复量，统一从这三个方法接入。
    // 现在按要求留空：接入时在方法体内实现（建议同时触发对应事件，让 HUD 立即刷新；
    // 提升上限时还要决定当前值是保持绝对值还是按比例放大）。
    // 动作消耗量（跳跃 / 闪避 / 攻击 / 奔跑）在 PlayerController 的「体力消耗」节，不在这里。

    /// <summary>【占位】提升生命上限（amount 可正可负）。</summary>
    public void UpgradeMaxHP(float amount)
    {
    }

    /// <summary>【占位】提升体力上限（amount 可正可负）。</summary>
    public void UpgradeMaxSP(float amount)
    {
    }

    /// <summary>【占位】提升每帧体力恢复量（amount 可正可负）。</summary>
    public void UpgradeRegen(float amount)
    {
    }
}
