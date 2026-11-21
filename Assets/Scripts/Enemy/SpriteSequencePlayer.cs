using System;
using UnityEngine;

/// <summary>
/// 轻量序列帧播放器：按名字切换几段 Sprite 序列（Idle / Walk / Run / Hit……），逐帧写进 SpriteRenderer。
///
/// 为什么不用 Animator：敌人的动画只有「按状态换一段循环帧」这一种需求，没有混合、没有过渡，
/// 用 Animator 要额外维护 .anim + .controller 两类资产；这里一个组件 + Inspector 里拖帧就够了。
/// 状态切换由 <see cref="EnemyAnimationStateMachine"/> 决定（行为控制器只通知动画状态），
/// 本组件不含任何规则：给名字就播，播完报进度。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteSequencePlayer : MonoBehaviour
{
    [Serializable]
    public class Clip
    {
        [Tooltip("片段名，控制器按这个名字调用 Play。")]
        public string name = "Idle";
        [Tooltip("按顺序播放的帧。")]
        public Sprite[] frames;
        [Tooltip("帧率（帧/秒）。")]
        public float fps = 10f;
        [Tooltip("是否循环；不循环时播完停在最后一帧。")]
        public bool loop = true;
    }

    [Tooltip("所有片段。")]
    public Clip[] clips;

    [Tooltip("启用时自动播放的片段名（留空 = 第一个片段）。")]
    public string defaultClip;

    private SpriteRenderer spriteRenderer;
    private Clip current;
    private float timer;
    private int frameIndex;

    /// <summary>当前片段名（没有片段时为 null）。</summary>
    public string CurrentClip => current != null ? current.name : null;

    /// <summary>非循环片段是否已播到最后一帧。循环片段恒为 false。</summary>
    public bool IsFinished => current != null && !current.loop && current.frames != null
                              && frameIndex >= current.frames.Length - 1;

    /// <summary>
    /// 当前片段已播放的归一化进度（0 ~ 1，按帧位置换算）。
    /// 供上层动画状态机查询「这一段播到哪了」；不含任何业务规则。
    /// </summary>
    public float NormalizedTime
    {
        get
        {
            if (current == null || current.frames == null || current.frames.Length <= 1)
                return 0f;

            return Mathf.Clamp01((float)frameIndex / (current.frames.Length - 1));
        }
    }

    /// <summary>是否配置了名为 clipName 的片段（上层状态机用来在切换前显式检查）。</summary>
    public bool HasClip(string clipName)
    {
        return Find(clipName) != null;
    }

    /// <summary>片段播一遍的时长（秒）；片段不存在或帧率非法时返回 0。</summary>
    public float ClipDuration(string clipName)
    {
        Clip clip = Find(clipName);
        if (clip == null || clip.frames == null || clip.frames.Length == 0 || clip.fps <= 0f)
            return 0f;

        return clip.frames.Length / clip.fps;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        if (current == null && clips != null && clips.Length > 0)
            Play(string.IsNullOrEmpty(defaultClip) ? clips[0].name : defaultClip, true);
    }

    /// <summary>
    /// 切到名为 clipName 的片段。已经在播同一段时默认不打断（restart = true 才从第 0 帧重来）。
    /// 找不到该片段时保持当前画面不变。
    /// </summary>
    public void Play(string clipName, bool restart = false)
    {
        if (!restart && current != null && current.name == clipName)
            return;

        Clip next = Find(clipName);
        if (next == null)
            return;

        current = next;
        timer = 0f;
        frameIndex = 0;
        ApplyFrame();
    }

    private Clip Find(string clipName)
    {
        if (clips == null)
            return null;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && clips[i].name == clipName)
                return clips[i];
        }
        return null;
    }

    private void Update()
    {
        if (current == null || current.frames == null || current.frames.Length == 0 || current.fps <= 0f)
            return;

        float interval = 1f / current.fps;
        timer += Time.deltaTime;
        while (timer >= interval)
        {
            timer -= interval;
            int next = frameIndex + 1;
            if (next >= current.frames.Length)
            {
                if (!current.loop)
                {
                    timer = 0f;
                    return;
                }
                next = 0;
            }
            frameIndex = next;
            ApplyFrame();
        }
    }

    private void ApplyFrame()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null || current == null || current.frames == null || current.frames.Length == 0)
            return;

        Sprite frame = current.frames[Mathf.Clamp(frameIndex, 0, current.frames.Length - 1)];
        if (frame != null)
            spriteRenderer.sprite = frame;
    }
}
