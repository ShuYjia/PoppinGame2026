using UnityEngine;

/// <summary>
/// 终点（第一关：仓库的门，之后换成「遇到有咲」）：玩家碰到即通关，交给 LevelManager 结算。
/// 需要：本物体上一个 isTrigger 的 Collider2D。
/// </summary>
public class LevelGoal : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        if (LevelManager.Instance != null)
            LevelManager.Instance.CompleteLevel();
    }
}
