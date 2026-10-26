using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 角色事件的 SO 通道：CharacterState 在 Inspector 里把 OnHealthChange / OnPowerChange
/// 挂到 RaisedEvent，UIManager 订阅 OnEventRaised，实现「状态 ↔ UI」跨场景解耦。
/// 每条通道一个资产（CharacterEventSO=HP、PowerEventSO=SP），资产放在 Assets/Events/ 下。
/// </summary>
[CreateAssetMenu(fileName = "Event/CharacterEventSO")]
public class CharacterEventSO : ScriptableObject
{
    /// <summary>
    /// 订阅出口。声明为 event：外部只能 += / -=，防止误用 = 把已有订阅全部覆盖掉
    /// （Play 期间 SO 不随场景卸载，订阅残留很难排查）。
    /// </summary>
    public event UnityAction<CharacterState> OnEventRaised;

    /// <summary>触发事件（Inspector 持久化监听调用的入口，必须保持 public）。</summary>
    public void RaisedEvent(CharacterState characterState)
    {
        OnEventRaised?.Invoke(characterState);
    }
}
