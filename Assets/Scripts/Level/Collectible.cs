using UnityEngine;

/// <summary>
/// 可拾取物：玩家碰到就收走。
/// - Star（星屑）：计分（策划案 4.3.2：每个 100 分），HUD 显示「已收集 / 总数」。
///   将来远程攻击要消耗星屑时（1 个 = 1 发），从 LevelManager.Stars 扣即可。
/// - Heart（回血）：回复 healAmount 点 HP；满血时不拾取，留给需要的时候。
///
/// 需要：本物体上一个 isTrigger 的 Collider2D。待机时上下浮动，拾取时放大淡出后销毁。
/// </summary>
public class Collectible : MonoBehaviour
{
    public enum Kind
    {
        Star,
        Heart
    }

    [Tooltip("拾取物种类。")]
    public Kind kind = Kind.Star;
    [Tooltip("Heart 回复的 HP。")]
    public float healAmount = 30f;

    [Header("表现")]
    [Tooltip("上下浮动幅度（单位）。")]
    public float bobHeight = 0.15f;
    [Tooltip("浮动速度（周/秒）。")]
    public float bobSpeed = 0.8f;
    [Tooltip("拾取动画时长（秒）。")]
    public float pickupTime = 0.25f;

    private Vector3 basePosition;
    private Vector3 baseScale;
    private float phase;
    private bool collected;
    private float pickupTimer;
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        basePosition = transform.localPosition;
        baseScale = transform.localScale;
        // 每个拾取物的浮动相位错开一点，一排星星不会整齐划一地上下动。
        phase = (transform.position.x * 0.37f) % 1f;
    }

    private void Update()
    {
        if (!collected)
        {
            float y = Mathf.Sin((Time.time * bobSpeed + phase) * Mathf.PI * 2f) * bobHeight;
            transform.localPosition = basePosition + new Vector3(0f, y, 0f);
            return;
        }

        pickupTimer += Time.deltaTime;
        float t = Mathf.Clamp01(pickupTimer / Mathf.Max(0.01f, pickupTime));
        transform.localScale = baseScale * (1f + 0.6f * t);
        transform.localPosition += Vector3.up * (2f * Time.deltaTime);
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.a = 1f - t;
            spriteRenderer.color = c;
        }
        if (t >= 1f)
            Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryCollect(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // 满血时碰到爱心不收；之后掉了血还站在上面，也要能收到。
        if (kind == Kind.Heart)
            TryCollect(other);
    }

    private void TryCollect(Collider2D other)
    {
        if (collected)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead)
            return;

        switch (kind)
        {
            case Kind.Star:
                if (LevelManager.Instance != null)
                    LevelManager.Instance.AddStar();
                break;
            case Kind.Heart:
                CharacterState state = player.State;
                if (state == null || state.CurrentHP >= state.maxHP)
                    return;
                state.Heal(healAmount);
                break;
        }

        collected = true;
        foreach (Collider2D c in GetComponents<Collider2D>())
            c.enabled = false;
    }
}
