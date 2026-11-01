using UnityEngine;

/// <summary>
/// 攻击组件：挂在「能造成伤害的物体」上（Boar 的接触伤害触发器、玩家的音波子弹等），
/// 对碰到的 <see cref="CharacterState"/> 结算伤害。谁挂本组件，谁就是伤害来源。
///
/// 两种触发语义由同一个组件覆盖：
/// - OnTriggerEnter2D（进入即一次）：子弹类一次性命中。造成实际伤害后若
///   destroyHostOnHit 为 true，销毁携带本组件的物体（子弹消失）。
/// - OnTriggerStay2D（持续接触）：Boar 类接触伤害。每个物理帧都会尝试结算，但目标方的
///   「按来源无敌」会把同一物体的连续接触自动限成每 invincibleTime 秒一次
///   （来源 = 本组件所在的 GameObject），因此这里不需要自备计时器。
///
/// 来源归一：<see cref="CharacterState.TakeDamage(Attack)"/> 内部把本组件归一到所在
/// GameObject，因此同一物体上挂的多个 Attack 共用一份无敌冷却（「同一来源」语义）。
///
/// owner：命中 owner 自身或其子物体上的 CharacterState 时跳过——玩家的音波不会
/// 打中玩家自己。不需要排除的物体（如 Boar）留空即可。
/// </summary>
public class Attack : MonoBehaviour
{
    [Tooltip("造成的伤害值。")]
    public float damage = 10f;

    [Tooltip("伤害来源的排除对象（通常是发射者）：命中它自身或其子物体上的 CharacterState 时跳过。留空 = 不排除任何目标。")]
    public Transform owner;

    [Tooltip("造成实际伤害后是否销毁携带本组件的物体（子弹 = true；持续接触类如 Boar = false）。")]
    public bool destroyHostOnHit;

    // 携带本组件的角色自己的状态（Boar 身上那份）；没有就是 null（音波、尖刺等）。
    private CharacterState hostState;
    private bool hostResolved;

    /// <summary>进入触发区即尝试结算一次（子弹类一次性命中）。</summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        TryHit(collision);
    }

    /// <summary>停留在触发区内每物理帧尝试结算（接触伤害；目标方无敌门控自动限频）。</summary>
    private void OnTriggerStay2D(Collider2D collision)
    {
        TryHit(collision);
    }

    /// <summary>
    /// 对碰撞体所属的 CharacterState 结算一次伤害。
    /// 目标不存在 / 被 owner 排除 / 处于无敌 / 已死亡时不会造成伤害。
    /// </summary>
    /// <returns>是否真的造成了伤害。</returns>
    private bool TryHit(Collider2D collision)
    {
        if (collision == null || damage <= 0f || !enabled)
            return false;

        // 携带者自己已经死了（比如被打死的 Boar）：尸体不再造成伤害。
        // 注意禁用组件拦不住 OnTriggerStay2D（Unity 照样会发给禁用的脚本），所以这里要显式判断。
        if (!hostResolved)
        {
            hostState = GetComponentInParent<CharacterState>();
            hostResolved = true;
        }
        if (hostState != null && hostState.IsDead)
            return false;

        // 碰撞体可能在子物体上（玩家的碰撞体、敌人的部件），向上找角色状态组件。
        CharacterState target = collision.GetComponentInParent<CharacterState>();
        if (target == null)
            return false;

        // 不打自己（发射者）及其子物体：玩家音波穿过玩家本人。
        if (owner != null && (target.transform == owner || target.transform.IsChildOf(owner)))
            return false;

        if (!target.TakeDamage(this))
            return false;

        if (destroyHostOnHit)
            Destroy(gameObject);

        return true;
    }
}
