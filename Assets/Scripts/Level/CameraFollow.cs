using UnityEngine;

/// <summary>
/// 横版跟随相机：平滑跟随玩家，朝前方多看一点（lookAhead），并限制在关卡边界内（不露出关卡外的空白）。
///
/// 挂在 Main Camera 上。边界用世界坐标填（minX / maxX / minY / maxY 是「相机视野边缘」能到的范围，
/// 不是相机中心），留空（min ≥ max）表示该轴不限制。复活时 LevelManager 会调 <see cref="SnapToTarget"/> 瞬移对齐。
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Tooltip("跟随目标；留空自动查找 PlayerController。")]
    public Transform target;

    [Tooltip("相机中心相对目标脚底的偏移（单位）。y 为正 = 镜头略高于角色，多看前上方。")]
    public Vector2 offset = new Vector2(0f, 2f);

    [Tooltip("朝面向方向多看的距离（单位）。")]
    public float lookAhead = 3f;
    [Tooltip("lookAhead 切换方向的平滑时间（秒）。")]
    public float lookAheadSmoothTime = 0.5f;

    [Tooltip("水平跟随平滑时间（秒），越小越跟手。")]
    public float smoothTimeX = 0.12f;
    [Tooltip("竖直跟随平滑时间（秒）。竖直慢一点，跳跃时画面不会跟着上下晃。")]
    public float smoothTimeY = 0.3f;

    [Header("关卡边界（视野边缘，世界坐标）")]
    public float minX = 0f;
    public float maxX = 0f;
    public float minY = 0f;
    public float maxY = 0f;

    private Camera cam;
    private PlayerController targetController;
    private float currentLookAhead;
    private float lookAheadVelocity;
    private Vector2 velocity;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        if (target == null)
        {
            PlayerController player = FindObjectOfType<PlayerController>();
            if (player != null)
                target = player.transform;
        }
        if (target != null)
            targetController = target.GetComponent<PlayerController>();
        SnapToTarget();
    }

    /// <summary>立刻对齐到目标（不做平滑），开场 / 复活时调用。</summary>
    public void SnapToTarget()
    {
        if (target == null)
            return;

        currentLookAhead = FacingDirection() * lookAhead;
        lookAheadVelocity = 0f;
        velocity = Vector2.zero;
        Vector2 desired = Clamp(DesiredPosition());
        transform.position = new Vector3(desired.x, desired.y, transform.position.z);
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        currentLookAhead = Mathf.SmoothDamp(currentLookAhead, FacingDirection() * lookAhead,
            ref lookAheadVelocity, lookAheadSmoothTime);

        Vector2 desired = Clamp(DesiredPosition());
        Vector3 position = transform.position;
        position.x = Mathf.SmoothDamp(position.x, desired.x, ref velocity.x, smoothTimeX);
        position.y = Mathf.SmoothDamp(position.y, desired.y, ref velocity.y, smoothTimeY);
        transform.position = position;
    }

    private Vector2 DesiredPosition()
    {
        Vector2 p = target.position;
        return new Vector2(p.x + offset.x + currentLookAhead, p.y + offset.y);
    }

    private float FacingDirection()
    {
        return targetController != null ? targetController.FacingDirection : 1f;
    }

    private Vector2 Clamp(Vector2 center)
    {
        if (cam == null || !cam.orthographic)
            return center;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        if (maxX > minX)
            center.x = ClampAxis(center.x, minX + halfWidth, maxX - halfWidth);
        if (maxY > minY)
            center.y = ClampAxis(center.y, minY + halfHeight, maxY - halfHeight);
        return center;
    }

    /// <summary>边界比视野还窄时居中，不让 Clamp 的上下限颠倒。</summary>
    private static float ClampAxis(float value, float min, float max)
    {
        if (min > max)
            return (min + max) * 0.5f;
        return Mathf.Clamp(value, min, max);
    }

    private void OnDrawGizmosSelected()
    {
        if (maxX <= minX && maxY <= minY)
            return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        float x0 = maxX > minX ? minX : transform.position.x - 1f;
        float x1 = maxX > minX ? maxX : transform.position.x + 1f;
        float y0 = maxY > minY ? minY : transform.position.y - 1f;
        float y1 = maxY > minY ? maxY : transform.position.y + 1f;
        Gizmos.DrawWireCube(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f), new Vector3(x1 - x0, y1 - y0, 0f));
    }
}
