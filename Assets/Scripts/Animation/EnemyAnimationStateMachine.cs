using Poppin.Animation;
using UnityEngine;

/// <summary>
/// 敌人动画有限状态机：把「行为状态」（巡逻 / 蓄力 / 冲撞 / 受击 / 死亡…）翻译成
/// 「动画状态」（Idle / Walk / Run / Attack / Hurt / Dead），再交给同物体上的
/// <see cref="SpriteSequencePlayer"/> 逐帧播放。
///
/// 边界：
/// - 本组件**只**决定播哪段序列帧，不碰 Rigidbody2D、CharacterState、伤害或任何行为参数；
/// - 行为状态仍由各自的控制器（<see cref="GroundEnemyController"/> / <see cref="BoarController"/>）负责，
///   它们只在状态入口调用 <see cref="ChangeState"/>；
/// - 同一个状态重复请求不会重播（避免每帧调用把循环动画卡在第 0 帧），需要重播时传 restart。
///
/// 默认绑定按策划案的地面敌人素材来：Idle / Walk / Run / Attack / Hurt / Dead，
/// 其中 Attack / Hurt / Dead 都复用 Hit 片段（当前敌人只有四段美术）。具体敌人在 Inspector 里覆盖。
/// </summary>
[RequireComponent(typeof(SpriteSequencePlayer))]
[DisallowMultipleComponent]
public class EnemyAnimationStateMachine : MonoBehaviour
{
    [Tooltip("逐帧播放器（同物体）。留空时自动获取。")]
    [SerializeField]
    private SpriteSequencePlayer sequence;

    [Tooltip("动画状态 id → 序列帧片段名。stateId 必须与 EnemyAnimationState 的枚举名一致，playbackName 是 SpriteSequencePlayer.clips 里的 name。")]
    [SerializeField]
    private AnimationStateBinding[] bindings = CreateDefaultBindings();

    private AnimationStateMachine machine;

    /// <summary>当前动画状态。</summary>
    public EnemyAnimationState CurrentState { get; private set; } = EnemyAnimationState.Idle;

    /// <summary>当前状态 id（字符串形式，供日志 / 调试）。</summary>
    public string CurrentStateId => machine != null ? machine.CurrentStateId : null;

    /// <summary>当前状态是否已经播完（循环状态恒为 false）。</summary>
    public bool IsFinished => machine != null && machine.IsFinished;

    /// <summary>
    /// 默认绑定表：预制体、Level1Builder 动态生成的敌人都用这一份，保证两边动画规则一致。
    /// 顺序与 <see cref="EnemyAnimationState"/> 一致，便于人工核对。
    /// </summary>
    public static AnimationStateBinding[] CreateDefaultBindings()
    {
        return new[]
        {
            Bind(EnemyAnimationState.Idle, "Idle"),
            Bind(EnemyAnimationState.Walk, "Walk"),
            Bind(EnemyAnimationState.Run, "Run"),
            Bind(EnemyAnimationState.Attack, "Hit", false),
            Bind(EnemyAnimationState.Hurt, "Hit", false),
            Bind(EnemyAnimationState.Dead, "Hit", false),
        };
    }

    private static AnimationStateBinding Bind(EnemyAnimationState state, string playbackName, bool loop = true)
    {
        return new AnimationStateBinding
        {
            stateId = state.ToString(),
            playbackName = playbackName,
            loop = loop,
            exitNormalizedTime = 1f
        };
    }

    private void Awake()
    {
        if (sequence == null)
            sequence = GetComponent<SpriteSequencePlayer>();

        machine = new AnimationStateMachine(new SpriteSequenceBackend(sequence), bindings,
            message => Debug.LogWarning(message, this));
    }

    private void OnEnable()
    {
        // 启用时统一从 Idle 起手：对象池 / 场景重载后不残留上一次的片段。
        if (machine == null)
            return;

        machine.Reset();
        CurrentState = EnemyAnimationState.Idle;
        ChangeState(EnemyAnimationState.Idle, true);
    }

    /// <summary>请求切到某个动画状态。返回是否真的发生了切换。</summary>
    public bool ChangeState(EnemyAnimationState state, bool restart = false)
    {
        if (machine == null)
            return false;

        bool changed = machine.ChangeState(state.ToString(), restart);
        if (changed)
            CurrentState = state;
        return changed;
    }

    /// <summary>
    /// 播放一次非循环表现（例如远程敌人开火）：从该状态起播，播完由调用方切回其他状态。
    /// 目前等价于 <c>ChangeState(state, true)</c>，单独留出入口是为了让「一次性」语义在调用处可读。
    /// </summary>
    public bool PlayOneShot(EnemyAnimationState state)
    {
        return ChangeState(state, true);
    }

    public bool IsInState(EnemyAnimationState state)
    {
        return machine != null && machine.IsInState(state.ToString());
    }

    /// <summary>回到初始 Idle 并强制重播（玩家复活、敌人回出生点时用）。</summary>
    public void ResetToIdle()
    {
        if (machine == null)
            return;

        machine.Reset();
        CurrentState = EnemyAnimationState.Idle;
        ChangeState(EnemyAnimationState.Idle, true);
    }

    /// <summary>
    /// 序列帧播放后端：把 <see cref="SpriteSequencePlayer"/> 包装成
    /// <see cref="IAnimationPlaybackBackend"/>。
    ///
    /// 播完判定**按时间**算（距 Play 是否已过一个片段长度），而不是依赖片段自身的 loop 标记：
    /// 敌人素材的 Hit 片段本来就是循环的（播完停在最后一帧会让「消失」帧提前定住），
    /// 用时间判定「这一遍播够了」既不改美术资产，也不会让 Hit 因为 loop 永远算不完。
    /// </summary>
    private sealed class SpriteSequenceBackend : IAnimationPlaybackBackend
    {
        private readonly SpriteSequencePlayer player;
        private float playStartTime;
        private float playDuration;

        public SpriteSequenceBackend(SpriteSequencePlayer player)
        {
            this.player = player;
        }

        public bool Has(string playbackName)
        {
            return player != null && player.HasClip(playbackName);
        }

        public void Play(string playbackName, bool loop, bool restart)
        {
            if (player == null)
                return;

            player.Play(playbackName, restart);
            playStartTime = Time.time;
            playDuration = player.ClipDuration(playbackName);
        }

        public float GetNormalizedTime()
        {
            if (playDuration <= 0f)
                return 0f;

            return Mathf.Clamp01((Time.time - playStartTime) / playDuration);
        }

        public bool HasFinished()
        {
            return playDuration > 0f && Time.time - playStartTime >= playDuration;
        }
    }
}
