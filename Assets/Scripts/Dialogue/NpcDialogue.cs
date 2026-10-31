using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 单个 NPC 的对话数据与状态机（挂在 NPC 上，一个 NPC 一份）。
///
/// 职责边界：
/// - **本组件持有**该 NPC 的全部对话配置：显示名、触发器、头顶锚点、气泡引用、台词列表、
///   打字速度、末句最短停留、语音与事件。
/// - <see cref="DialogueCoordinator"/> 只负责输入路由与范围候选管理，不持有任何台词或配置。
///
/// 流程规则（与策划案一致）：
/// 1. 玩家进入触发器 → 头顶显示交互提示（提示文本在气泡/提示预制体上配置）。
/// 2. 按 Interact（键盘 E / 手柄东侧键）→ 开始对话，或从中途中断处恢复。
/// 3. 句子打字中按键：**只补全本句**；本句完整后再按一次才进入下一句（按住不会连跳）。
/// 4. 玩家走出该 NPC 交互范围 → 立刻中断：隐藏气泡、停止语音，但**保存当前句索引**
///    与“离开时本句是否已完整”：
///      · 离开时正在打字 → 恢复时从本句开头重新打字；
///      · 离开时已完整 → 恢复时直接完整显示本句，等待下一次按键推进。
///    两种情况恢复时都会**重播本句语音**。
/// 5. 最后一句完整显示后自动结束：至少停留 lastLineMinHoldSeconds（默认 2.5s），
///    且若本句配了语音则等语音播完；停留计时从“本句刚完整显示”那一刻开始，打字过程不占这 2.5s。
/// 6. 正常结束后清零进度，下次交互从第一句重播；中断进度只存在于当前场景实例运行期间，
///    不写磁盘、不跨场景重载。
/// 7. 玩家在对话期间**照常移动**——本组件不会禁用玩家控制，离开范围即中断就是唯一的退出条件。
/// </summary>
public class NpcDialogue : MonoBehaviour
{
    /// <summary>单句对话的运行状态。</summary>
    public enum DialogueState
    {
        /// <summary>空闲：没在对话，也没保存中断进度。</summary>
        Idle,

        /// <summary>正在逐字显示当前句。</summary>
        Typing,

        /// <summary>当前句已完整显示，等待下一次按键推进（末句则是等待停留结束）。</summary>
        LineComplete,

        /// <summary>中断中：保存了句索引与“离开时是否已完整”，等待玩家回到范围内恢复。</summary>
        Interrupted
    }

    // ─────────────────────────────── 身份与引用 ───────────────────────────────

    [Header("身份")]
    [Tooltip("NPC 显示名。台词里 speakerName 留空时用它作为说话者。")]
    public string displayName = "NPC";

    [Header("交互范围")]
    [Tooltip("交互触发器：带 isTrigger 的 Collider2D。建议做成 NPC 的子物体单独摆放范围；\n" +
             "留空时自动查找——优先用自身、其次第一个子物体上带 isTrigger 的 Collider2D。\n" +
             "只认挂载了 PlayerController 的玩家，敌人踩进来不会触发。")]
    public Collider2D interactionTrigger;

    [Tooltip("气泡锚点：World Space 气泡挂在这个位置上方。留空时用 NPC 自身。")]
    public Transform bubbleAnchor;

    [Tooltip("提示锚点：交互提示挂在这个位置上方。留空时用气泡锚点。")]
    public Transform promptAnchor;

    [Header("视图")]
    [Tooltip("场景里已经摆好的气泡视图（推荐：气泡预制体作为 NPC 子物体，这里直接引用）。\n" +
             "与下面的预制体二选一：都为空时本 NPC 只是不显示气泡，对话逻辑照常运行。")]
    public DialogueBubbleView bubbleView;

    [Tooltip("气泡预制体：bubbleView 未指定时运行时实例化到气泡锚点下（NPC 禁用/销毁时自动回收）。")]
    public DialogueBubbleView bubblePrefab;

    [Tooltip("交互提示预制体：进入范围时实例化到提示锚点下，离开范围/NPC 禁用销毁时回收。\n" +
             "留空则不显示提示（对话逻辑不受影响）。")]
    public DialoguePromptView promptPrefab;

    [Tooltip("场景里已经摆好的交互提示（与 promptPrefab 二选一，优先用这个）。\n" +
             "推荐把提示预制体拖成 NPC 子物体后在这里引用，避免运行时再实例化一份。")]
    public DialoguePromptView promptView;

    // ─────────────────────────────── 台词 ───────────────────────────────

    [Header("台词")]
    [Tooltip("按顺序播放的台词。列表为空时交互安全退出（不抛异常，只给一条 Console 警告）。")]
    public DialogueLine[] lines = new DialogueLine[0];

    [Header("节奏")]
    [Tooltip("逐字显示速度（可见字符 / 秒）。打字中按键会立即补全本句。")]
    public float charactersPerSecond = 24f;

    [Tooltip("最后一句完整显示后的最短停留时间（秒）。\n" +
             "从「本句刚完整显示」开始计时，因此打字过程不会占用这段时间；本句配了语音时还要等语音播完。")]
    public float lastLineMinHoldSeconds = 2.5f;

    [Header("语音")]
    [Tooltip("播放台词语音的 AudioSource。留空时自动查找自身/子物体上的 AudioSource；\n" +
             "只有真正要播某一句时才会在自身上新建一个，不会平白给 NPC 挂空组件。")]
    public AudioSource voiceSource;

    [Tooltip("调试用：每次播放/停止语音时打一条 Console 日志。")]
    public bool logVoicePlayback = false;

    [Header("事件")]
    [Tooltip("对话开始（首次开始或中断后恢复都会触发）。供剧情、音效等后续功能接入。")]
    public UnityEvent onDialogueStarted;

    [Tooltip("每一句开始显示时触发（参数：句索引）。")]
    public UnityEvent<int> onLineStarted;

    [Tooltip("对话正常结束（最后一句停留结束）时触发。中断不触发本事件。")]
    public UnityEvent onDialogueEnded;

    // ─────────────────────────────── 运行时状态 ───────────────────────────────

    private DialogueState state = DialogueState.Idle;
    private int currentIndex;
    private bool isCurrentLineComplete;
    private Coroutine typingRoutine;

    private bool hasInterruptedProgress;
    private bool hasEnteredDialogue;
    private int interruptedIndex;
    private bool interruptedLineWasComplete;

    private float lastLineCompleteTime = -1f;

    private AudioSource cachedVoiceSource;
    private DialogueBubbleView runtimeBubble;
    private DialoguePromptView runtimePrompt;

    // 玩家范围主动检测用（不依赖触发器回调）。
    private const float rangeCheckInterval = 0.1f;
    private readonly List<Collider2D> overlapResults = new List<Collider2D>(8);
    private readonly ContactFilter2D triggerFilter = new ContactFilter2D { useTriggers = true };
    private Collider2D playerColliderCache;
    private bool wasPlayerInsideRange;
    private float rangeCheckTimer;
    [SerializeField]
    private DialogueState debugState = DialogueState.Idle;
    [SerializeField]
    private int debugLineIndex;
    [SerializeField]
    private bool debugHasInterruptedProgress;

    /// <summary>当前运行状态（只读调试信息）。</summary>
    public DialogueState State => state;

    /// <summary>当前句索引（只读调试信息）。中断期间保留的是“下次恢复要播的那句”。</summary>
    public int CurrentIndex => currentIndex;

    /// <summary>当前句是否已经完整显示（打字结束或恢复出来的完整句）。</summary>
    public bool IsCurrentLineComplete => isCurrentLineComplete;

    /// <summary>是否处于对话中（打字或等待推进都算）。</summary>
    public bool IsDialogueActive => state == DialogueState.Typing || state == DialogueState.LineComplete;

    /// <summary>是否握着一段中断进度（回到范围内按键会从这里恢复）。</summary>
    public bool HasInterruptedProgress => hasInterruptedProgress;

    /// <summary>有效台词条数（列表未赋值时为 0）。</summary>
    public int LineCount => lines == null ? 0 : lines.Length;

    /// <summary>NPC 名称：显示名为空时退回 GameObject 名字。</summary>
    public string NpcName => string.IsNullOrEmpty(displayName) ? name : displayName;

    // ─────────────────────────────── 生命周期 ───────────────────────────────

    private void Awake()
    {
        if (voiceSource != null)
            cachedVoiceSource = voiceSource;

        if (interactionTrigger == null)
            interactionTrigger = FindFallbackTrigger();
    }

    private void OnEnable()
    {
        // 每次启用都重新注册：脚本重编译 / Play 中反序列化都可能让协调器的登记表失效，
        // Register 内部按实例去重，重复调用不会挂两份。
        DialogueCoordinator.Register(this);

        // 禁用期间协调器已把本 NPC 清出候选，这里必须把「玩家在范围内」的记忆一起清掉，
        // 否则重新启用后若玩家仍站在原地，RefreshPlayerPresence 会判定「状态没变」而不重新登记，
        // 结果提示不出现、按键也不响应。清掉后第一次检测就会重新登记并显示提示。
        wasPlayerInsideRange = false;
        rangeCheckTimer = rangeCheckInterval;
    }

    private void OnDisable()
    {
        // 禁用即视为场景重载：清掉中断进度，避免下次启用时从旧索引恢复。
        DialogueCoordinator.Unregister(this);
        Interrupt(resetProgress: true);
        DestroyRuntimeView(ref runtimeBubble);
        DestroyRuntimeView(ref runtimePrompt);
    }

    private void OnDestroy()
    {
        DialogueCoordinator.Unregister(this);
        Interrupt(resetProgress: true);
        DestroyRuntimeView(ref runtimeBubble);
        DestroyRuntimeView(ref runtimePrompt);
    }

    private void OnValidate()
    {
        if (lastLineMinHoldSeconds < 0f)
            lastLineMinHoldSeconds = 0f;
        if (charactersPerSecond < 0f)
            charactersPerSecond = 0f;
    }

    // ─────────────────────────────── 玩家范围检测 ───────────────────────────────

    /// <summary>
    /// 主动检测玩家是否在本 NPC 的交互范围内（每 rangeCheckInterval 秒一次）。
    ///
    /// 为什么不用 OnTriggerEnter2D / OnTriggerExit2D：实测（Unity 2022.3.34f1c1，本项目物理设置）
    /// 触发器挂在**没有 Rigidbody2D 的子物体**上时，接触事件不派发；即便补了 Static Rigidbody2D，
    /// 用代码直接改玩家 transform.position 走进范围也不会触发 Enter（物理世界用 IsTouching /
    /// OverlapCollider 查是相交的，但回调不来）。为了「玩家进入范围就该出提示」这条验收在任何
    /// 情况下都成立，这里改为主动查询：用玩家的碰撞体做 OverlapCollider，判断是否碰到交互触发器。
    /// 结果与事件无关，瞬移、走动、NPC 后生成都能正确进入/离开。
    /// </summary>
    private void RefreshPlayerPresence()
    {
        Collider2D playerCollider = FindPlayerCollider();
        bool inside = false;

        if (playerCollider != null && interactionTrigger != null)
        {
            overlapResults.Clear();
            int hits = playerCollider.OverlapCollider(triggerFilter, overlapResults);
            for (int i = 0; i < hits && i < overlapResults.Count; i++)
            {
                if (overlapResults[i] == interactionTrigger)
                {
                    inside = true;
                    break;
                }
            }
        }

        if (inside == wasPlayerInsideRange)
            return;

        wasPlayerInsideRange = inside;

        if (inside)
        {
            DialogueCoordinator.OnNpcTriggerEnter(this);
            ShowPrompt();
        }
        else
        {
            DialogueCoordinator.OnNpcTriggerExit(this);
            HidePrompt();

            // 走出了本 NPC 的范围：中断它自己的对话（不关别的 NPC 的事），但保留句子进度。
            Interrupt(resetProgress: false);
        }
    }

    private void Update()
    {
        // 玩家是否在范围内：低频主动查询（不依赖触发器回调，原因见 RefreshPlayerPresence）。
        rangeCheckTimer += Time.deltaTime;
        if (rangeCheckTimer >= rangeCheckInterval)
        {
            rangeCheckTimer = 0f;
            RefreshPlayerPresence();
        }

        // 末句自动结束：完整显示后至少停留 lastLineMinHoldSeconds（有语音则等播完）再收气泡。
        // 停留计时从「本句刚完整显示」开始，所以打字过程不占用这段时间。
        if (state == DialogueState.LineComplete && currentIndex >= LineCount - 1 && LastLineHoldSatisfied())
            EndDialogueCompleted();
    }

    /// <summary>找到玩家的碰撞体：优先缓存，其次按 PlayerController 查一次。</summary>
    private Collider2D FindPlayerCollider()
    {
        if (playerColliderCache != null)
            return playerColliderCache;

        PlayerController controller = DialogueCoordinator.Current != null && DialogueCoordinator.Current.player != null
            ? DialogueCoordinator.Current.player
            : FindObjectOfType<PlayerController>();

        if (controller == null)
            return null;

        playerColliderCache = controller.GetComponent<Collider2D>();
        return playerColliderCache;
    }

    // ─────────────────────────────── 对外流程 ───────────────────────────────

    /// <summary>
    /// 交互键入口：没有中断进度时从第一句开始，有进度时从中断处恢复。
    /// </summary>
    public void BeginOrResumeDialogue()
    {
        if (LineCount == 0)
        {
            Debug.LogWarning($"{nameof(NpcDialogue)}：{name} 的台词列表为空，交互已安全退出。请在 Inspector 的 Lines 里至少配一句。", this);
            return;
        }

        if (IsDialogueActive)
            return;

        bool isResuming = hasInterruptedProgress;
        currentIndex = isResuming ? Mathf.Clamp(interruptedIndex, 0, LineCount - 1) : 0;

        hasInterruptedProgress = false;
        hasEnteredDialogue = true;
        lastLineCompleteTime = -1f;

        HidePrompt();

        if (onDialogueStarted != null)
            onDialogueStarted.Invoke();

        if (isResuming)
        {
            // 恢复：离开时是完整句 → 直接完整显示并等推进；离开时在打字 → 从本句开头重打。
            // 两种都要重播本句语音。
            if (interruptedLineWasComplete)
            {
                ShowLine(currentIndex);
                FinishCurrentLine();
                PlayVoice(lines[currentIndex]);
                Debug.Log($"{nameof(NpcDialogue)}：{NpcName} 从第 {currentIndex + 1} 句完整状态恢复。", this);
            }
            else
            {
                // 走 StartLineAt 而不是只 ShowLine：必须重新进入 Typing 状态并从第一个字符开始打字，
                // 否则会整句瞬间显示、且状态停在 Idle 导致按键推进失效。
                StartLineAt(currentIndex);
            }
        }
        else
        {
            StartLineAt(currentIndex);
        }
    }

    /// <summary>
    /// 立即补全当前句（只在本句正在打字时有效）。完整后再调用是空操作，不会顺带推进到下一句。
    /// </summary>
    public void CompleteCurrentLineInstantly()
    {
        if (state != DialogueState.Typing)
            return;

        FinishCurrentLine();
    }

    /// <summary>
    /// 推进到下一句；最后一句停留结束后则正常结束对话。
    /// 只在本句已完整显示且末句停留条件满足时有效，程序化调用与按键走的是同一套校验。
    /// </summary>
    public void AdvanceToNextLine()
    {
        if (state != DialogueState.LineComplete)
            return;

        if (currentIndex >= LineCount - 1)
        {
            if (!LastLineHoldSatisfied())
                return;

            EndDialogueCompleted();
            return;
        }

        StartLineAt(currentIndex + 1);
    }

    /// <summary>
    /// 中断对话：隐藏气泡、停语音、停打字协程，并把当前句索引与完成状态存下来。
    /// </summary>
    /// <param name="resetProgress">为 true 时连中断进度一起清掉（NPC 禁用/销毁、场景重载）。</param>
    public void Interrupt(bool resetProgress)
    {
        StopVoice();
        StopTyping();

        bool wasInDialogue = hasEnteredDialogue;

        // 离开时本句是否已经完整 —— 这个值决定恢复时是「重打本句」还是「完整显示等推进」。
        interruptedLineWasComplete = isCurrentLineComplete;
        interruptedIndex = currentIndex;

        state = DialogueState.Idle;
        isCurrentLineComplete = false;
        lastLineCompleteTime = -1f;

        if (bubbleView != null)
            bubbleView.SetVisible(false);

        // 交还对话锁定：之后按键要重新挑最近 NPC，不能被这个已中断/已结束的 NPC 吃掉。
        DialogueCoordinator.ReleaseCurrent(this);

        if (resetProgress)
        {
            hasInterruptedProgress = false;
            hasEnteredDialogue = false;
            interruptedIndex = 0;
            interruptedLineWasComplete = false;
            currentIndex = 0;
        }
        else
        {
            // 只有真的进过对话才记进度：玩家只是路过触发器、压根没按过键时不该留下“下次恢复”的状态。
            hasInterruptedProgress = wasInDialogue;
        }

        SyncDebugFields();
    }

    /// <summary>清掉对话进度，下次交互从第一句开始（Inspector 按钮与剧情脚本可调用）。</summary>
    public void ResetProgress()
    {
        Interrupt(resetProgress: true);
    }

    // ─────────────────────────────── 内部流程 ───────────────────────────────

    /// <summary>开始显示第 index 句：停上一句语音 → 触发单句事件 → 显示 → 起打字协程 → 播本句语音。</summary>
    private void StartLineAt(int index)
    {
        StopVoice();

        currentIndex = Mathf.Clamp(index, 0, LineCount - 1);
        isCurrentLineComplete = false;

        if (onLineStarted != null)
            onLineStarted.Invoke(currentIndex);

        ShowLine(currentIndex);
        StartTyping();
        PlayVoice(lines[currentIndex]);
        SyncDebugFields();
    }

    /// <summary>把第 index 句的文本与说话者写进气泡并显示气泡。</summary>
    private void ShowLine(int index)
    {
        DialogueBubbleView view = ResolveBubbleView();
        if (view == null)
            return;

        DialogueLine line = lines[index];
        string speaker = line != null && !string.IsNullOrEmpty(line.speakerName) ? line.speakerName : NpcName;
        view.SetSpeaker(speaker);
        view.SetText(line != null ? line.text : string.Empty);
        view.SetContinuePromptVisible(false);
        view.SetVisible(true);
    }

    private void StartTyping()
    {
        StopTyping();
        state = DialogueState.Typing;
        isCurrentLineComplete = false;
        typingRoutine = StartCoroutine(TypewriterRoutine());
    }

    private void StopTyping()
    {
        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }
    }

    /// <summary>本句显示完整：写满可见字符、显示继续提示、切状态；末句则记下“刚完整”的时刻。</summary>
    private void FinishCurrentLine()
    {
        StopTyping();

        DialogueBubbleView view = ResolveBubbleView();
        if (view != null)
        {
            view.RevealAllCharacters();
            view.SetContinuePromptVisible(true);
        }

        state = DialogueState.LineComplete;
        isCurrentLineComplete = true;

        if (currentIndex >= LineCount - 1)
            lastLineCompleteTime = Time.time;

        SyncDebugFields();
    }

    /// <summary>末句停留是否已达要求：至少 lastLineMinHoldSeconds，且配了语音时要等语音播完。</summary>
    private bool LastLineHoldSatisfied()
    {
        if (lastLineCompleteTime < 0f)
            return false;

        if (Time.time - lastLineCompleteTime < lastLineMinHoldSeconds)
            return false;

        AudioSource source = ResolveVoiceSource(createIfMissing: false);
        if (source != null && source.isPlaying)
            return false;

        return true;
    }

    /// <summary>正常结束：清进度、隐藏气泡、触发结束事件。</summary>
    private void EndDialogueCompleted()
    {
        StopVoice();
        StopTyping();

        state = DialogueState.Idle;
        isCurrentLineComplete = false;
        lastLineCompleteTime = -1f;

        DialogueBubbleView view = ResolveBubbleView();
        if (view != null)
            view.SetVisible(false);

        // 结束后玩家通常还站在范围内：把交互提示放回来，否则要走出再进来才看得到提示。
        if (wasPlayerInsideRange)
            ShowPrompt();

        // 正常完成后清零：下次交互从第一句重播。
        currentIndex = 0;
        hasInterruptedProgress = false;
        hasEnteredDialogue = false;
        interruptedIndex = 0;
        interruptedLineWasComplete = false;

        // 交还对话锁定：这一次对话已经走完，之后按键应重新挑最近 NPC。
        DialogueCoordinator.ReleaseCurrent(this);

        SyncDebugFields();

        if (onDialogueEnded != null)
            onDialogueEnded.Invoke();
    }

    private IEnumerator TypewriterRoutine()
    {
        DialogueBubbleView view = ResolveBubbleView();
        if (view == null)
        {
            // 没有气泡也要走完状态机：直接判定本句完成，避免停在 Typing 状态卡住流程。
            FinishCurrentLine();
            yield break;
        }

        view.SetVisibleCharacters(0);
        view.ForceMeshUpdate();

        int total = view.VisibleCharacterCount;
        float interval = charactersPerSecond > 0f ? 1f / charactersPerSecond : 0f;

        for (int shown = 0; shown <= total; shown++)
        {
            view.SetVisibleCharacters(shown);
            if (shown >= total)
                break;

            if (interval > 0f)
            {
                // 按帧推进：不用 WaitForSeconds，避免与打字速度不同步导致「一顿一顿」。
                float timer = 0f;
                while (timer < interval)
                {
                    timer += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                yield return null;
            }
        }

        view.RevealAllCharacters();
        typingRoutine = null;
        FinishCurrentLine();
    }

    // ─────────────────────────────── 语音 ───────────────────────────────

    private void PlayVoice(DialogueLine line)
    {
        if (line == null || line.voiceClip == null)
            return;

        AudioSource source = ResolveVoiceSource(createIfMissing: true);
        if (source == null)
            return;

        source.Stop();
        source.clip = line.voiceClip;
        source.Play();

        if (logVoicePlayback)
            Debug.Log($"{nameof(NpcDialogue)}：{NpcName} 播放第 {currentIndex + 1} 句语音「{line.voiceClip.name}」。", this);
    }

    private void StopVoice()
    {
        AudioSource source = ResolveVoiceSource(createIfMissing: false);
        if (source == null || !source.isPlaying)
            return;

        source.Stop();

        if (logVoicePlayback)
            Debug.Log($"{nameof(NpcDialogue)}：{NpcName} 停止语音。", this);
    }

    private AudioSource ResolveVoiceSource(bool createIfMissing)
    {
        if (voiceSource != null)
        {
            cachedVoiceSource = voiceSource;
            return cachedVoiceSource;
        }

        // 注意：不能把「这次没找到」缓存成「已解析」。
        // StartLineAt 会先 StopVoice 再 PlayVoice，若 StopVoice 提前把结果钉成 null，
        // 之后即使配了 voiceClip 也永远不会创建 AudioSource，等于语音功能失效。
        if (cachedVoiceSource != null)
            return cachedVoiceSource;

        cachedVoiceSource = GetComponent<AudioSource>();
        if (cachedVoiceSource == null)
            cachedVoiceSource = GetComponentInChildren<AudioSource>(true);

        if (cachedVoiceSource == null && createIfMissing)
            cachedVoiceSource = gameObject.AddComponent<AudioSource>();

        return cachedVoiceSource;
    }

    // ─────────────────────────────── 视图与触发器 ───────────────────────────────

    private DialogueBubbleView ResolveBubbleView()
    {
        if (bubbleView != null)
            return bubbleView;

        if (bubblePrefab == null)
            return null;

        if (runtimeBubble == null)
        {
            Transform anchor = bubbleAnchor != null ? bubbleAnchor : transform;
            runtimeBubble = Instantiate(bubblePrefab, anchor);
            runtimeBubble.transform.localPosition = Vector3.zero;
        }

        return runtimeBubble;
    }

    private void ShowPrompt()
    {
        DialoguePromptView prompt = ResolvePromptView();
        if (prompt == null)
            return;

        prompt.Show();
    }

    private void HidePrompt()
    {
        DialoguePromptView prompt = ResolvePromptView();
        if (prompt == null)
            return;

        prompt.Hide();
    }

    /// <summary>提示视图：优先用场景里接好的实例，没接才用预制体在提示锚点下实例化一份。</summary>
    private DialoguePromptView ResolvePromptView()
    {
        if (promptView != null)
            return promptView;

        if (promptPrefab == null)
            return null;

        if (runtimePrompt == null)
        {
            Transform anchor = promptAnchor != null ? promptAnchor : (bubbleAnchor != null ? bubbleAnchor : transform);
            runtimePrompt = Instantiate(promptPrefab, anchor);
            runtimePrompt.transform.localPosition = Vector3.zero;
        }

        return runtimePrompt;
    }

    private void DestroyRuntimeView<T>(ref T instance) where T : Component
    {
        if (instance == null)
            return;

        // 只回收运行时自己实例化的对象；Inspector 里直接引用的场景对象只隐藏、绝不销毁。
        if (Application.isPlaying)
            Destroy(instance.gameObject);
        else
            DestroyImmediate(instance.gameObject);

        instance = null;
    }

    private Collider2D FindFallbackTrigger()
    {
        Collider2D[] self = GetComponents<Collider2D>();
        for (int i = 0; i < self.Length; i++)
        {
            if (self[i].isTrigger)
                return self[i];
        }

        Collider2D[] children = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].isTrigger)
                return children[i];
        }

        return null;
    }

    private void SyncDebugFields()
    {
        debugState = state;
        debugLineIndex = currentIndex;
        debugHasInterruptedProgress = hasInterruptedProgress;
    }
}
