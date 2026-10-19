using UnityEngine;

/// <summary>
/// 普通攻击的音波：朝给定方向水平飞行，超时或碰到障碍后消失。
/// 朝向用 SpriteRenderer.flipX 镜像（贴图朝右绘制），不走负缩放——URP 2D 会背面剔除整物体。
/// 纯表现道具：伤害、命中判定与消耗留待战斗系统实现时在本预制体上扩展
/// （PlayerController.RequestAttack 即调用入口）。
/// </summary>
public class PlayerAttackWave : MonoBehaviour
{
    [Header("音波")]
    [Tooltip("飞行速度（单位/秒）。")]
    public float speed = 8f;
    [Tooltip("存活时间（秒），超时消失。")]
    public float lifetime = 1.2f;
    [Tooltip("碰到哪些层会消失（默认全部；自动排除生成者自身及其子物体）。")]
    public LayerMask hitLayers = ~0;

    private const float WallCheckSize = 0.3f; // 撞墙检测盒边长（世界单位）
    private const int MaxHitResults = 4;      // 单帧最多关注的命中数

    private readonly Collider2D[] hitBuffer = new Collider2D[MaxHitResults];

    private float lifeTimer;
    private int direction = 1;
    private SpriteRenderer spriteRenderer;
    private Transform owner;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>按方向发射：direction &gt;= 0 朝右，否则朝左。</summary>
    /// <param name="launchDirection">发射方向（1 右 / -1 左）。</param>
    /// <param name="ownerTransform">生成者（玩家） Transform：撞墙检测时排除，避免出生即判定撞墙。</param>
    public void Launch(int launchDirection, Transform ownerTransform = null)
    {
        direction = launchDirection >= 0 ? 1 : -1;
        owner = ownerTransform;

        if (spriteRenderer != null)
            spriteRenderer.flipX = direction < 0;
    }

    private void Update()
    {
        lifeTimer += Time.deltaTime;
        if (lifeTimer >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 position = transform.position;
        position.x += direction * speed * Time.deltaTime;
        transform.position = position;

        if (HitObstacle())
            Destroy(gameObject);
    }

    /// <summary>音波位置是否撞到障碍（查询 hitLayers，排除生成者自身及其子物体）。</summary>
    private bool HitObstacle()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.useLayerMask = true;
        filter.layerMask = hitLayers;

        int count = Physics2D.OverlapBox(transform.position, Vector2.one * WallCheckSize, 0f, filter, hitBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = hitBuffer[i];
            if (hit == null)
                continue;
            // 自己（挂在玩家身上的子碰撞体）不算障碍。
            if (owner != null && (hit.transform == owner || hit.transform.IsChildOf(owner)))
                continue;
            return true;
        }

        return false;
    }
}
