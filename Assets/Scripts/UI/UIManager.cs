using UnityEngine;

/// <summary>
/// UI 总调度：订阅 HP / SP 两条 ScriptableObject 事件通道，把数值转发给 PlayerStateBar 渲染。
/// 本组件是唯一的 SO 订阅方；PlayerStateBar 只负责画条，不订阅事件、不读游戏状态。
/// 通道资产：Assets/Events/CharacterEventSO.asset（HP）、Assets/Events/PowerEventSO.asset（SP）。
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("事件通道（Assets/Events/ 下的 SO 资产）")]
    [Tooltip("HP 通道：Player 的 CharacterState.OnHealthChange 在 Inspector 里挂它的 RaisedEvent。")]
    public CharacterEventSO healthEvent;

    [Tooltip("SP 通道：Player 的 CharacterState.OnPowerChange 在 Inspector 里挂它的 RaisedEvent。")]
    public CharacterEventSO powerEvent;

    [Header("引用")]
    public PlayerStateBar playerStateBar;

    private void OnEnable()
    {
        if (healthEvent != null)
            healthEvent.OnEventRaised += OnHealthEvent;
        else
            Debug.LogWarning($"{nameof(UIManager)}: healthEvent 未赋值，血条不会同步。", this);

        if (powerEvent != null)
            powerEvent.OnEventRaised += OnPowerEvent;
        else
            Debug.LogWarning($"{nameof(UIManager)}: powerEvent 未赋值，体力条不会同步。", this);
    }

    private void OnDisable()
    {
        // event 语义下 -= 对未订阅的目标是安全的，这里不重复判空（SO 订阅在 Play 期间残留很难排查）。
        if (healthEvent != null)
            healthEvent.OnEventRaised -= OnHealthEvent;

        if (powerEvent != null)
            powerEvent.OnEventRaised -= OnPowerEvent;
    }

    private void OnHealthEvent(CharacterState characterState)
    {
        playerStateBar.SetHealth(characterState.CurrentHP, characterState.maxHP);
    }

    private void OnPowerEvent(CharacterState characterState)
    {
        playerStateBar.SetPower(characterState.CurrentSP, characterState.maxSP);
    }
}
