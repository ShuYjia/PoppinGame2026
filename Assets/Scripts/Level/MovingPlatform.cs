using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 移动平台（策划案 4.2 场景机关 / 7.1 机关教学）：在起点和「起点 + travel」之间匀速来回，两端各停 waitTime 秒。
///
/// 站在上面的角色会跟着平台走（策划案 4.2 待补项「玩家站在移动平台上是否跟随移动」，这里定为跟随）：
/// 每个物理帧把平台这一帧的位移原样加到站在顶面上的刚体上。不用父子关系，
/// 因为 PlayerController 每帧自己设水平速度，挂到平台下面也会被甩下来。
/// 从平台上起跳 / 走下去时继承平台的水平速度，直到落地：否则原地跳一下，平台就从脚底下滑走了。
///
/// 需要：本物体上的 Rigidbody2D（运行时自动设为 Kinematic）和一个实体 Collider2D（顶面就是站的地方）。
/// 起点 = 场景里摆放的位置；Scene 视图里黄线画出往返路线。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class MovingPlatform : MonoBehaviour
{
    [Tooltip("终点相对起点的偏移（世界单位）。横着走填 (x, 0)，电梯填 (0, y)。")]
    public Vector2 travel = new Vector2(6f, 0f);

    [Tooltip("移动速度（单位 / 秒）。")]
    public float speed = 2f;

    [Tooltip("到达两端后停多久（秒），给玩家上下平台的时间。")]
    public float waitTime = 0.8f;

    [Tooltip("开场先等多久再动（秒）。同一段里有几块平台时可以错开节奏。")]
    public float startDelay;

    [Tooltip("脚底离平台顶面多近算「站在上面」（世界单位）。")]
    public float standTolerance = 0.12f;

    [Tooltip("离开平台后继承它水平速度的最长时间（秒），落地会提前结束。")]
    public float maxInheritTime = 2.5f;

    private Rigidbody2D rb;
    private Collider2D solid;
    private Vector2 origin;
    private float progress;      // 0 = 起点，1 = 终点
    private int direction = 1;   // 1 = 去终点，-1 = 回起点
    private float waitTimer;

    // 碰到平台的刚体 → 它的碰撞体（每帧再用位置判断是不是真的站在顶面上）。
    private readonly Dictionary<Rigidbody2D, Collider2D> touching = new Dictionary<Rigidbody2D, Collider2D>();
    private readonly List<Rigidbody2D> stale = new List<Rigidbody2D>();

    // 上一物理帧被平台带着走的刚体；这一帧不在里面了 = 刚离开平台。
    private readonly HashSet<Rigidbody2D> carriedLast = new HashSet<Rigidbody2D>();
    private readonly HashSet<Rigidbody2D> carriedNow = new HashSet<Rigidbody2D>();

    private class Launch
    {
        public float velocityX;
        public float time;
    }

    // 离开平台、还在空中的刚体 → 继承的水平速度。
    private readonly Dictionary<Rigidbody2D, Launch> launched = new Dictionary<Rigidbody2D, Launch>();
    private readonly List<Rigidbody2D> landed = new List<Rigidbody2D>();

    /// <summary>平台当前这一物理帧的位移（给需要的脚本读）。</summary>
    public Vector2 FrameDelta { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        solid = GetComponent<Collider2D>();
        origin = rb.position;
        waitTimer = startDelay;
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector2 from = rb.position;
        Vector2 to = from;

        float length = travel.magnitude;
        if (length > 0.001f && speed > 0f)
        {
            if (waitTimer > 0f)
            {
                waitTimer -= dt;
            }
            else
            {
                progress += direction * speed * dt / length;
                if (progress >= 1f || progress <= 0f)
                {
                    progress = Mathf.Clamp01(progress);
                    direction = -direction;
                    waitTimer = waitTime;
                }
                to = origin + travel * progress;
            }
        }

        FrameDelta = to - from;
        if (FrameDelta.sqrMagnitude > 0f)
            rb.MovePosition(to);

        carriedNow.Clear();
        CarryRiders(FrameDelta);
        UpdateLaunched(dt, FrameDelta.x / dt);

        carriedLast.Clear();
        carriedLast.UnionWith(carriedNow);
    }

    private void CarryRiders(Vector2 delta)
    {
        if (solid == null)
            return;

        float top = solid.bounds.max.y;
        stale.Clear();
        foreach (KeyValuePair<Rigidbody2D, Collider2D> pair in touching)
        {
            Rigidbody2D rider = pair.Key;
            Collider2D riderCol = pair.Value;
            if (rider == null || riderCol == null || !rider.gameObject.activeInHierarchy)
            {
                stale.Add(rider);
                continue;
            }
            if (rider.bodyType != RigidbodyType2D.Dynamic)
                continue;

            Bounds b = riderCol.bounds;
            bool onTop = Mathf.Abs(b.min.y - top) <= standTolerance
                         && b.max.x > solid.bounds.min.x && b.min.x < solid.bounds.max.x;
            // 正在往上跳的不带（否则起跳那一帧会被平台的下行位移拽回来）。
            if (onTop && rider.velocity.y <= 0.01f)
            {
                rider.position += delta;
                carriedNow.Add(rider);
                launched.Remove(rider);
            }
        }

        foreach (Rigidbody2D r in stale)
            touching.Remove(r);
    }

    /// <summary>刚离开平台的刚体开始继承平台水平速度；已在空中的继续带，落地或超时为止。</summary>
    private void UpdateLaunched(float dt, float platformVelocityX)
    {
        foreach (Rigidbody2D r in carriedLast)
        {
            if (r != null && !carriedNow.Contains(r) && Mathf.Abs(platformVelocityX) > 0.01f)
                launched[r] = new Launch { velocityX = platformVelocityX };
        }

        landed.Clear();
        foreach (KeyValuePair<Rigidbody2D, Launch> pair in launched)
        {
            Rigidbody2D r = pair.Key;
            Launch launch = pair.Value;
            launch.time += dt;

            // 死亡时 PlayerController 会冻结 X，此时也别再推它。
            bool gone = r == null || !r.gameObject.activeInHierarchy
                        || (r.constraints & RigidbodyConstraints2D.FreezePositionX) != 0;
            PlayerGroundSensor sensor = gone ? null : r.GetComponent<PlayerGroundSensor>();
            bool grounded = sensor != null ? r.velocity.y <= 0.01f && sensor.ProbeGround() : launch.time > 0.1f;
            if (gone || grounded || launch.time > maxInheritTime)
            {
                landed.Add(r);
                continue;
            }
            r.position += new Vector2(launch.velocityX * dt, 0f);
        }

        foreach (Rigidbody2D r in landed)
            launched.Remove(r);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.rigidbody != null)
            touching[collision.rigidbody] = collision.collider;
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.rigidbody != null)
            touching[collision.rigidbody] = collision.collider;
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.rigidbody != null)
            touching.Remove(collision.rigidbody);
    }

    private void OnDrawGizmos()
    {
        Vector3 start = Application.isPlaying ? (Vector3)origin : transform.position;
        Vector3 end = start + (Vector3)travel;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(start, 0.15f);
        Gizmos.DrawWireSphere(end, 0.15f);
    }
}
