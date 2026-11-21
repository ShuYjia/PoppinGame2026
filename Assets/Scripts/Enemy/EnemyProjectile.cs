using UnityEngine;

/// <summary>
/// 敌方投射物：沿给定方向飞行，撞到地形消失，命中角色后由 Attack 自毁。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Attack))]
public class EnemyProjectile : MonoBehaviour
{
    [Tooltip("默认飞行速度（单位/秒）。")]
    public float speed = 7f;
    [Tooltip("默认存活时间（秒）。")]
    public float lifetime = 3f;
    [Tooltip("撞到哪些层会消失。")]
    public LayerMask hitLayers = ~0;
    [Tooltip("撞墙检测半径。")]
    public float wallCheckRadius = 0.18f;

    private Rigidbody2D rb;
    private Collider2D projectileCollider;
    private Attack attack;
    private Transform owner;
    private Vector2 direction = Vector2.right;
    private float lifeTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        projectileCollider = GetComponent<Collider2D>();
        attack = GetComponent<Attack>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        projectileCollider.isTrigger = true;
    }

    /// <summary>设置发射方向、来源和本次投射物参数。</summary>
    public void Launch(Vector2 launchDirection, Transform ownerTransform,
        float launchSpeed, float launchLifetime, float damage, LayerMask launchHitLayers)
    {
        direction = launchDirection.sqrMagnitude > 0.0001f
            ? launchDirection.normalized
            : Vector2.right;
        owner = ownerTransform;
        speed = Mathf.Max(0f, launchSpeed);
        lifetime = Mathf.Max(0.05f, launchLifetime);
        hitLayers = launchHitLayers;
        lifeTimer = 0f;

        if (attack != null)
        {
            attack.owner = ownerTransform;
            attack.damage = damage;
            attack.destroyHostOnHit = true;
        }

        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
            sprite.flipX = direction.x < 0f;
    }

    private void FixedUpdate()
    {
        lifeTimer += Time.fixedDeltaTime;
        if (lifeTimer >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        Vector2 nextPosition = rb.position + direction * speed * Time.fixedDeltaTime;
        if (HitObstacle(nextPosition))
        {
            Destroy(gameObject);
            return;
        }

        rb.MovePosition(nextPosition);
    }

    private bool HitObstacle(Vector2 position)
    {
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true
        };
        filter.SetLayerMask(hitLayers);

        Collider2D[] hits = new Collider2D[4];
        int count = Physics2D.OverlapCircle(position, wallCheckRadius, filter, hits);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null || hit == projectileCollider)
                continue;
            if (owner != null && (hit.transform == owner || hit.transform.IsChildOf(owner)))
                continue;
            if (hit.GetComponentInParent<CharacterState>() != null)
                continue;
            return true;
        }

        return false;
    }
}
