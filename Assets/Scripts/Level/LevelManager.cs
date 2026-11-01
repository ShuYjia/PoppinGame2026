using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 一关的流程总管（每个关卡场景放一个）：计时、计分、存档点、死亡复活、通关结算、重开。
///
/// 闭环：开场 → 推进（拾取 / 击杀计分）→ 死亡则从最近的存档点复活 → 碰到终点 → 结算面板 → 确认后重开本关。
///
/// 计分口径（策划案 4.3 定稿，三块）：
/// - 击杀分 = Σ 敌人基础分 + 连击加成（相邻两次击杀间隔 ≤ comboWindow 秒，第 n 杀 +comboStep×(n−1)，单次连击加成上限 comboCap）
/// - 拾取分 = 星屑 × starScore + 告示牌 × signScore
/// - 时间奖励 = max(0, targetTime − 通关秒数) × timeBonusPerSecond，上限 timeBonusCap
///
/// 按键（键盘 + 手柄）：
/// - 随时重开本关：R / 手柄 Select（PS: Share / Create，Xbox: View）
/// - 结算面板确认重开：Enter / Space / J / 手柄南键（✕ / A）/ Start（Options / Menu）
///
/// 其他组件通过 <see cref="Instance"/> 上报事件（拾取、击杀、存档、掉坑、到终点），HUD 每帧读本组件的公开数值。
/// </summary>
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("玩家")]
    [Tooltip("留空自动查找场景里的 PlayerController。")]
    public PlayerController player;
    [Tooltip("开场出生点；留空 = 玩家在场景里的初始位置。")]
    public Transform startPoint;
    [Tooltip("跟随相机（复活时瞬移对齐）；留空自动查找。")]
    public CameraFollow cameraFollow;

    [Header("复活")]
    [Tooltip("死亡后多久复活（秒），留给死亡动画。")]
    public float respawnDelay = 1.5f;
    [Tooltip("复活后的无敌时间（秒），防止一落地就被敌人连续撞死。")]
    public float respawnInvincibleTime = 1.5f;

    [Header("计分（策划案 4.3）")]
    public int starScore = 100;
    public int signScore = 10;
    [Tooltip("连击判定窗口：相邻两次击杀间隔不超过这么多秒算连击。")]
    public float comboWindow = 5f;
    [Tooltip("连击第 n 杀额外加 comboStep × (n−1)。")]
    public int comboStep = 10;
    [Tooltip("单次连击的加成上限。")]
    public int comboCap = 100;
    [Tooltip("目标通关时间（秒），第一关 180。")]
    public float targetTime = 180f;
    public int timeBonusPerSecond = 10;
    public int timeBonusCap = 1000;

    [Header("结算")]
    [Tooltip("结算面板出现后多久才接受确认键（秒），避免通关瞬间按着的跳跃键直接把面板跳过。")]
    public float resultInputDelay = 1f;

    // ───────────────────────────── 运行时数据（HUD 读取） ─────────────────────────────

    public float ElapsedTime { get; private set; }
    public int Stars { get; private set; }
    public int TotalStars { get; private set; }
    public int Kills { get; private set; }
    public int KillPoints { get; private set; }
    public int ComboBonus { get; private set; }
    public int SignsRead { get; private set; }
    public int Deaths { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool IsRespawning { get; private set; }

    /// <summary>游戏进行中显示的分数（不含时间奖励，时间奖励只在结算时给）。</summary>
    public int RunningScore => KillPoints + Stars * starScore + SignsRead * signScore;

    /// <summary>当前生效的复活点。</summary>
    public Vector2 RespawnPoint { get; private set; }

    /// <summary>结算明细，通关后有效。</summary>
    public LevelResult Result { get; private set; }

    /// <summary>通关瞬间触发（HUD 据此弹出结算面板）。</summary>
    public event Action<LevelResult> Completed;

    /// <summary>激活新存档点时触发。</summary>
    public event Action<Vector2> CheckpointReached;

    /// <summary>玩家复活落地后触发（敌人据此回到出生点，避免守在复活点）。</summary>
    public event Action PlayerRespawned;

    private CharacterState playerState;
    private float lastKillTime = -999f;
    private int comboCount;
    private int currentComboBonus;
    private float completedAt;

    [Serializable]
    public struct LevelResult
    {
        public int kills;
        public int killPoints;
        public int comboBonus;
        public int stars;
        public int totalStars;
        public int starPoints;
        public int signs;
        public int signPoints;
        public float timeSeconds;
        public int timeBonus;
        public int deaths;
        public int total;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (playerState != null)
            playerState.Died -= OnPlayerDied;
    }

    private void Start()
    {
        if (player == null)
            player = FindObjectOfType<PlayerController>();
        if (cameraFollow == null)
            cameraFollow = FindObjectOfType<CameraFollow>();

        if (player != null)
        {
            playerState = player.GetComponent<CharacterState>();
            if (playerState != null)
                playerState.Died += OnPlayerDied;

            if (startPoint != null)
                player.Respawn(startPoint.position);
            RespawnPoint = player.transform.position;
        }
        else
        {
            Debug.LogError("LevelManager：场景里找不到 PlayerController。", this);
        }

        foreach (Collectible c in FindObjectsOfType<Collectible>())
        {
            if (c.kind == Collectible.Kind.Star)
                TotalStars++;
        }
    }

    private void Update()
    {
        if (!IsCompleted)
            ElapsedTime += Time.deltaTime;

        if (WasRestartPressed())
        {
            RestartLevel();
            return;
        }

        if (IsCompleted && Time.unscaledTime - completedAt >= resultInputDelay && WasConfirmPressed())
            RestartLevel();
    }

    // ───────────────────────────── 上报入口 ─────────────────────────────

    public void AddStar()
    {
        Stars++;
    }

    public void AddSign()
    {
        SignsRead++;
    }

    /// <summary>敌人死亡时上报：基础分 + 连击加成。</summary>
    public void RegisterKill(int baseScore)
    {
        Kills++;

        if (Time.time - lastKillTime <= comboWindow)
        {
            comboCount++;
        }
        else
        {
            comboCount = 1;
            currentComboBonus = 0;
        }
        lastKillTime = Time.time;

        int bonus = 0;
        if (comboCount > 1)
        {
            int wanted = comboStep * (comboCount - 1);
            bonus = Mathf.Clamp(wanted, 0, Mathf.Max(0, comboCap - currentComboBonus));
            currentComboBonus += bonus;
        }

        ComboBonus += bonus;
        KillPoints += baseScore + bonus;
    }

    /// <summary>存档点被激活。</summary>
    public void SetCheckpoint(Vector2 position)
    {
        RespawnPoint = position;
        CheckpointReached?.Invoke(position);
    }

    /// <summary>
    /// 掉坑 / 落水等「直接判死」：先解除闪避无敌，再一次扣光 HP，走正常的死亡流程。
    /// （直接扣血会被闪避无敌挡掉，玩家就会闪避着掉进坑里永远往下落。）
    /// </summary>
    public void KillPlayer()
    {
        if (playerState == null || playerState.IsDead || IsCompleted)
            return;

        playerState.HoldGlobalInvincible(false);
        playerState.ClearGlobalInvincibility();
        playerState.TakeDamage(playerState.CurrentHP + 1f);
    }

    /// <summary>碰到终点：停表、锁输入、算分、弹结算。</summary>
    public void CompleteLevel()
    {
        if (IsCompleted || player == null || (playerState != null && playerState.IsDead))
            return;

        IsCompleted = true;
        completedAt = Time.unscaledTime;
        player.SetInputLocked(true);
        if (playerState != null)
            playerState.SetGlobalInvincible(9999f);

        int timeBonus = Mathf.Clamp(
            Mathf.FloorToInt(Mathf.Max(0f, targetTime - ElapsedTime)) * timeBonusPerSecond, 0, timeBonusCap);

        LevelResult result = new LevelResult
        {
            kills = Kills,
            killPoints = KillPoints,
            comboBonus = ComboBonus,
            stars = Stars,
            totalStars = TotalStars,
            starPoints = Stars * starScore,
            signs = SignsRead,
            signPoints = SignsRead * signScore,
            timeSeconds = ElapsedTime,
            timeBonus = timeBonus,
            deaths = Deaths,
        };
        result.total = result.killPoints + result.starPoints + result.signPoints + result.timeBonus;
        Result = result;

        Completed?.Invoke(result);
    }

    /// <summary>重新加载当前场景（场景不在 Build Settings 里时，编辑器下按路径加载兜底）。</summary>
    public void RestartLevel()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0)
        {
            SceneManager.LoadScene(scene.buildIndex);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(scene.name);
#endif
    }

    // ───────────────────────────── 死亡与复活 ─────────────────────────────

    private void OnPlayerDied()
    {
        if (IsCompleted || IsRespawning)
            return;
        Deaths++;
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        IsRespawning = true;
        yield return new WaitForSeconds(respawnDelay);

        if (player != null)
        {
            player.Respawn(RespawnPoint);
            if (playerState != null && respawnInvincibleTime > 0f)
                playerState.SetGlobalInvincible(respawnInvincibleTime);
            if (cameraFollow != null)
                cameraFollow.SnapToTarget();
            PlayerRespawned?.Invoke();
        }

        IsRespawning = false;
    }

    // ───────────────────────────── 输入 ─────────────────────────────

    private static bool WasRestartPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && gamepad.selectButton.wasPressedThisFrame;
    }

    private static bool WasConfirmPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                                 || keyboard.spaceKey.wasPressedThisFrame || keyboard.jKey.wasPressedThisFrame))
            return true;

        Gamepad gamepad = Gamepad.current;
        return gamepad != null && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame);
    }
}
