using UnityEngine;

/// <summary>
/// 普通攻击的音波：朝给定方向水平飞行，超时或碰到障碍后消失。
/// 朝向用 SpriteRenderer.flipX 镜像（贴图朝左绘制），不走负缩放——URP 2D 会背面剔除整物体。
/// 默认驱动 Assets/Art/FX/NorAttack/NormalAttackFx.png 的切片序列帧（左侧声波弧 + 右侧音符星星），
/// 播放一遍后停在最后一帧（非循环），与寿命自然衔接。
///
/// 伤害由预制体上的 <see cref="Attack"/> 组件结算（进入触发即造成伤害并销毁本物体）；
/// 撞到带 <see cref="CharacterState"/> 的目标时不当成墙销毁（否则伤害还没生效就没了），
/// 由 Attack 命中后自行销毁。发射者（owner）及其子物体既不算障碍，也不会被误伤。
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

    [Header("序列帧特效")]
    [Tooltip("按顺序播放的序列帧（预制体上已配好 NormalAttackFx 切片）。留空则静止显示 SpriteRenderer 当前贴图。")]
    public Sprite[] frames;
    [Tooltip("序列帧帧率（帧/秒）。29 帧 @ 24fps ≈ 1.21s，与默认寿命对齐。")]
    public float frameRate = 24f;
    [Tooltip("播放完毕后是否循环；false 时停在最后一帧直到寿命结束。")]
    public bool loop = false;

    private const float WallCheckSize = 0.3f; // 撞墙检测盒边长（世界单位）
    private const int MaxHitResults = 4;      // 单帧最多关注的命中数

    private readonly Collider2D[] hitBuffer = new Collider2D[MaxHitResults];

    private float lifeTimer;
    private float frameTimer;
    private int frameIndex;
    private int direction = 1;
    private SpriteRenderer spriteRenderer;
    private Transform owner;
    private Attack attack;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        attack = GetComponent<Attack>();
    }

    /// <summary>按方向发射：direction &gt;= 0 朝右，否则朝左。序列帧从第 0 帧开始。</summary>
    /// <param name="launchDirection">发射方向（1 右 / -1 左）。</param>
    /// <param name="ownerTransform">生成者（玩家） Transform：撞墙检测时排除，避免出生即判定撞墙；
    /// 同时交给身上的 Attack 作为 owner，音波不会打中玩家自己。</param>
    public void Launch(int launchDirection, Transform ownerTransform = null)
    {
        direction = launchDirection >= 0 ? 1 : -1;
        owner = ownerTransform;

        // 预制体可能在 Awake 之后才挂上 Attack（或本实例是旧对象），这里做一次兜底获取。
        if (attack == null)
            attack = GetComponent<Attack>();
        if (attack != null)
            attack.owner = ownerTransform;

        frameIndex = 0;
        frameTimer = 0f;
        ApplyFrame();

        if (spriteRenderer != null)
            spriteRenderer.flipX = direction > 0;
    }

    private void Update()
    {
        lifeTimer += Time.deltaTime;
        if (lifeTimer >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        AdvanceFrame(Time.deltaTime);

        Vector3 position = transform.position;
        position.x += direction * speed * Time.deltaTime;
        transform.position = position;

        if (HitObstacle())
            Destroy(gameObject);
    }

    /// <summary>按 frameRate 推进序列帧；非循环模式下播完停在最后一帧。</summary>
    private void AdvanceFrame(float deltaTime)
    {
        if (frames == null || frames.Length == 0 || frameRate <= 0f)
            return;

        float frameInterval = 1f / frameRate;
        frameTimer += deltaTime;
        while (frameTimer >= frameInterval)
        {
            frameTimer -= frameInterval;
            int next = frameIndex + 1;
            if (next >= frames.Length)
            {
                if (!loop)
                {
                    // 停在最后一帧：钳住计时器避免无限累积，后续帧不再推进。
                    frameTimer = Mathf.Min(frameTimer, frameInterval);
                    return;
                }
                next = 0;
            }
            frameIndex = next;
            ApplyFrame();
        }
    }

    /// <summary>把当前帧写进 SpriteRenderer。</summary>
    private void ApplyFrame()
    {
        if (spriteRenderer == null || frames == null || frames.Length == 0)
            return;
        if (frameIndex < 0 || frameIndex >= frames.Length)
            return;

        Sprite frame = frames[frameIndex];
        if (frame != null)
            spriteRenderer.sprite = frame;
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
            // 带 CharacterState 的物体是可攻击目标而不是墙：音波穿过去，由 Attack 触发器
            // 结算伤害并销毁本体。若在这里当墙销毁，触发伤害还没生效音波就消失了。
            if (hit.GetComponentInParent<CharacterState>() != null)
                continue;
            return true;
        }

        return false;
    }
}
