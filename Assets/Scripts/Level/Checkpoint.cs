using UnityEngine;

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

    public bool IsActive { get; private set; }

    private SpriteRenderer spriteRenderer;
    private Vector3 baseScale;
    private float popTimer = -1f;

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
        if (IsActive)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        IsActive = true;
        popTimer = 0f;
        if (spriteRenderer != null)
            spriteRenderer.color = activeColor;
        if (LevelManager.Instance != null)
            LevelManager.Instance.SetCheckpoint((Vector2)transform.position + respawnOffset);
    }
}
