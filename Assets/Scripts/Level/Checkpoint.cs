using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 存档点（策划案里 Otea 的位置，先用占位美术）：玩家经过时激活，之后死亡从这里复活。
/// 只会激活一次；激活后由灰变亮并弹一下，提示「存好了」。
///
/// 需要：本物体上一个 isTrigger 的 Collider2D。复活位置 = 本物体位置 + respawnOffset。
/// </summary>
public class Checkpoint : MonoBehaviour
{
    [Tooltip("复活位置相对本物体的偏移（单位）。")]
    public Vector2 respawnOffset = new Vector2(0f, 0.1f);
    [Tooltip("未激活时的颜色。")]
    public Color inactiveColor = new Color(0.55f, 0.55f, 0.6f, 1f);
    [Tooltip("激活后的颜色。")]
    public Color activeColor = Color.white;

    [SerializeField, Tooltip("可选的跨场景稳定存档 ID；留空时使用场景层级路径。")]
    private string saveId;

    public bool IsActive { get; private set; }
    public bool HasPlayerNearby => playerColliders.Count > 0;
    public string SaveId => SaveIdentity.Resolve(this, saveId);
    public Vector2 RespawnPosition => (Vector2)transform.position + respawnOffset;

    private SpriteRenderer spriteRenderer;
    private Vector3 baseScale;
    private float popTimer = -1f;
    private readonly HashSet<Collider2D> playerColliders = new HashSet<Collider2D>();

    private void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        baseScale = transform.localScale;
        if (spriteRenderer != null)
            spriteRenderer.color = inactiveColor;
    }

    private void Update()
    {
        if (popTimer < 0f)
            return;

        popTimer += Time.deltaTime;
        float t = Mathf.Clamp01(popTimer / 0.3f);
        float s = 1f + Mathf.Sin(t * Mathf.PI) * 0.25f;
        transform.localScale = baseScale * s;
        if (t >= 1f)
        {
            transform.localScale = baseScale;
            popTimer = -1f;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        if (!playerColliders.Add(other) || playerColliders.Count > 1)
            return;

        if (LevelManager.Instance != null)
            LevelManager.Instance.EnterCheckpoint(this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || !playerColliders.Remove(other) || playerColliders.Count > 0)
            return;

        if (LevelManager.Instance != null)
            LevelManager.Instance.ExitCheckpoint(this);
    }

    public bool Activate()
    {
        if (IsActive)
            return false;

        IsActive = true;
        popTimer = 0f;
        if (spriteRenderer != null)
            spriteRenderer.color = activeColor;
        return true;
    }

    public void RestoreActivatedFromSave(bool activated)
    {
        IsActive = activated;
        popTimer = -1f;
        transform.localScale = baseScale;
        if (spriteRenderer != null)
            spriteRenderer.color = activated ? activeColor : inactiveColor;
    }
}
