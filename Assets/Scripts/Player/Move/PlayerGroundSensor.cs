using UnityEngine;

/// <summary>
/// 角色的物理检测组件：只回答「脚下是不是踩在别人的碰撞体上」「某块区域有没有被挡住」，
/// 不保存任何游戏规则状态（起跳锁定、上升速度过滤等仍由 PlayerController 判定）。
///
/// 检测方式沿用地形贴合脚底的薄盒：探测高度只有 groundCheckDistance，
/// 因此不会出现「离地一段距离仍算落地」导致的连跳；命中过滤排除自身碰撞体、
/// 同一刚体上的子碰撞体以及所有触发器。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerGroundSensor : MonoBehaviour
{
    [Header("地面检测")]
    [Tooltip("哪些层算作地面。")]
    public LayerMask groundLayer = ~0;
    [Tooltip("脚下探测盒向下延伸的距离（世界单位）。当前值 0.02：只有脚底近乎贴地（≤2.5cm）才判定接地，保证落地动画（JumpGround）严格在脚接触地面时才触发。越大越容易判定为“站在地上”。")]
    public float groundCheckDistance = 0.02f;
    [Range(0.1f, 1f)]
    [Tooltip("脚下探测盒宽度占碰撞体宽度的比例。")]
    public float groundCheckWidthScale = 0.9f;

    [Header("调试")]
    [SerializeField]
    [Tooltip("在 Scene 视图里画出脚下的地面探测盒。")]
    private bool drawGizmos = true;

    private const float ProbeSlack = 0.01f;      // 探测盒越过脚底线的余量
    private const int MaxProbeResults = 8;

    private Rigidbody2D rb;
    private Collider2D col;
    private Collider2D[] probeBuffer;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        CacheComponents();
    }

    private void CacheComponents()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        if (probeBuffer == null)
            probeBuffer = new Collider2D[MaxProbeResults];
    }

    /// <summary>
    /// 用贴住脚底的一条薄盒判定脚下是否有别人的碰撞体。
    /// </summary>
    public bool ProbeGround()
    {
        if (col == null || rb == null)
            return false;

        GetGroundProbe(out Vector2 center, out Vector2 size);
        return HasForeignCollider(center, size);
    }

    /// <summary>计算脚下探测盒（世界坐标中心 + 尺寸），探测与 Gizmos 共用同一套公式。</summary>
    public void GetGroundProbe(out Vector2 center, out Vector2 size)
    {
        if (col == null)
        {
            center = rb != null ? rb.position : (Vector2)transform.position;
            size = Vector2.zero;
            return;
        }

        Bounds bounds = col.bounds;
        float reach = Mathf.Max(0.005f, groundCheckDistance);
        size = new Vector2(Mathf.Max(0.01f, bounds.size.x * groundCheckWidthScale), reach + ProbeSlack);
        center = new Vector2(bounds.center.x, bounds.min.y - reach * 0.5f + ProbeSlack * 0.5f);
    }

    /// <summary>区域内是否有“不属于自己”的阻挡碰撞体（起身头顶检测用）。</summary>
    public bool IsAreaBlocked(Vector2 center, Vector2 size)
    {
        return HasForeignCollider(center, size);
    }

    /// <summary>区域内是否命中“不属于自己”的碰撞体。</summary>
    private bool HasForeignCollider(Vector2 center, Vector2 size)
    {
        if (probeBuffer == null)
            probeBuffer = new Collider2D[MaxProbeResults];

        int count = Physics2D.OverlapBox(center, size, 0f, BuildGroundFilter(), probeBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = probeBuffer[i];
            if (hit == null || hit.isTrigger || hit == col)
                continue;
            // 自己以及挂在同一个刚体上的子碰撞体都不算障碍。
            if (rb != null && hit.attachedRigidbody == rb)
                continue;
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;
            return true;
        }

        return false;
    }

    private ContactFilter2D BuildGroundFilter()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.useLayerMask = true;
        filter.layerMask = groundLayer;
        return filter;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        if (col == null)
            col = GetComponent<Collider2D>();
        if (col == null)
            return;

        GetGroundProbe(out Vector2 center, out Vector2 size);

        bool grounded = false;
        if (Application.isPlaying)
        {
            PlayerController controller = GetComponent<PlayerController>();
            grounded = controller != null && controller.IsGrounded;
        }

        Gizmos.color = grounded ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(center, size);
    }
}
