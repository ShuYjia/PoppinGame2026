using UnityEngine;

/// <summary>
/// 死亡区（坑底 / 关卡下边界）：玩家进入直接判死，从存档点复活；敌人掉进来直接死亡。
/// 需要：本物体上一个 isTrigger 的 Collider2D，通常是铺在关卡最下方的一条长盒子。
/// </summary>
public class KillZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            if (LevelManager.Instance != null)
                LevelManager.Instance.KillPlayer();
            return;
        }

        CharacterState state = other.GetComponentInParent<CharacterState>();
        if (state != null && !state.IsDead)
            state.TakeDamage(state.CurrentHP + 1f);
    }
}
