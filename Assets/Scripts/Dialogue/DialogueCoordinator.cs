using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 对话协调器：集中处理 Interact 输入与「哪个 NPC 是当前目标」，**不持有任何 NPC 的台词或配置**
/// （台词、打字速度、末句停留、气泡引用全部留在各自的 <see cref="NpcDialogue"/> 上）。
///
/// 输入接线：复用玩家 <see cref="PlayerController"/> 已有的那套 <see cref="PlayerInputControl"/>，
/// 订阅 GamePlay.Interact 的 started —— 只监听一次按下事件，按住不会连续补全/跳句。
/// 绝不创建、启用或禁用第二套同类输入资产，避免重复输入。
///
/// 目标选择：范围内所有已登记且启用的 NPC 都是候选，只选**最近**的一个；对话开始后**锁定当前 NPC**，
/// 走出它的范围（或它被禁用/销毁）才中断并重新选择，避免范围重叠时一次按键误推进另一个 NPC。
///
/// 约定：每个场景放一个协调器（<see cref="Current"/> 指向场景里最后启用的那个）。
/// </summary>
public class DialogueCoordinator : MonoBehaviour
{
    [Header("玩家引用")]
    [Tooltip("玩家（挂 PlayerController 的对象）。留空时启动时自动查找场景里的 PlayerController。\n" +
             "只用来读它的输入资产实例与位置，不改它的任何状态、也不影响玩家移动。")]
    public PlayerController player;

    [Tooltip("player 未手动指定时，是否允许自动查找场景里的 PlayerController（只查一次，不在每帧查找）。")]
    public bool autoFindPlayer = true;

    [Header("调试")]
    [Tooltip("调试用：范围变化、目标切换、按键路由时打 Console 日志。")]
    public bool logDebug = false;

    // 所有当前启用中的 NPC（由各自 OnEnable/OnDisable 维护）。
    private static readonly List<NpcDialogue> registered = new List<NpcDialogue>();

    // 玩家此刻站在其交互范围内的 NPC（由触发器进出事件维护）。
    private readonly HashSet<NpcDialogue> inRange = new HashSet<NpcDialogue>();

    /// <summary>场景里当前生效的协调器（对话输入路由的唯一入口）。</summary>
    public static DialogueCoordinator Current { get; private set; }

    private NpcDialogue currentNpc;
    private NpcDialogue nearestNpc;

    private PlayerController resolvedPlayer;
    private bool playerResolved;
    private PlayerInputControl subscribedControl;
    private bool resolveInputTried;

    /// <summary>当前锁定的对话 NPC（没有对话时为 null）。</summary>
    public NpcDialogue CurrentNpc => currentNpc;

    /// <summary>当前最近的有效候选 NPC（未开始对话时就是按键会触发的那一个）。</summary>
    public NpcDialogue NearestNpc => nearestNpc;

    /// <summary>是否已拿到玩家的输入资产并完成订阅。</summary>
    public bool HasInput => subscribedControl != null;

    /// <summary>当前在范围内的候选数量（调试用）。</summary>
    public int CandidateCount => inRange.Count;

    // ─────────────────────────────── 生命周期 ───────────────────────────────

    private void OnEnable()
    {
        Current = this;
        ResolvePlayer();
    }

    private void OnDisable()
    {
        UnsubscribeInteract();
        subscribedControl = null;
        resolveInputTried = false;
        currentNpc = null;

        if (Current == this)
            Current = null;
    }

    private void Update()
    {
        // inputControl 由 PlayerController 在 Awake 创建，Awake 之前取不到；
        // 这里按需再试一次，拿到就订阅（之后按键走事件回调，不轮询）。
        if (subscribedControl == null && !resolveInputTried)
            TrySubscribeInteract();

        nearestNpc = PickNearestNpc();
    }

    // ─────────────────────────────── NPC 登记 ───────────────────────────────

    /// <summary>由 <see cref="NpcDialogue"/> 在 OnEnable 时调用，按实例去重。</summary>
    public static void Register(NpcDialogue npc)
    {
        if (npc == null || registered.Contains(npc))
            return;

        registered.Add(npc);
    }

    /// <summary>由 <see cref="NpcDialogue"/> 在 OnDisable/OnDestroy 时调用。</summary>
    public static void Unregister(NpcDialogue npc)
    {
        if (npc == null)
            return;

        registered.Remove(npc);

        if (Current != null)
            Current.OnNpcLost(npc);
    }

    /// <summary>
    /// 由 <see cref="NpcDialogue"/> 在正常结束或中断时调用：主动交还对话锁定。
    /// 光靠「离开范围」解锁不够——同一 NPC 连续对话结束、或 NPC 自己清掉进度时，
    /// 锁不放会导致后续按键仍被这个已结束的 NPC 吃掉。
    /// </summary>
    public static void ReleaseCurrent(NpcDialogue npc)
    {
        if (Current == null || npc == null)
            return;

        if (Current.currentNpc == npc)
            Current.currentNpc = null;
    }

    /// <summary>NPC 的触发器检测到玩家进入。</summary>
    public static void OnNpcTriggerEnter(NpcDialogue npc)
    {
        if (Current == null || npc == null)
            return;

        Current.HandleTriggerEnter(npc);
    }

    /// <summary>NPC 的触发器检测到玩家离开。</summary>
    public static void OnNpcTriggerExit(NpcDialogue npc)
    {
        if (Current == null || npc == null)
            return;

        Current.HandleTriggerExit(npc);
    }

    private void HandleTriggerEnter(NpcDialogue npc)
    {
        if (!inRange.Add(npc))
            return;

        if (logDebug)
            Debug.Log($"{nameof(DialogueCoordinator)}：进入 {npc.NpcName} 的交互范围（候选 {inRange.Count} 个）。", this);
    }

    private void HandleTriggerExit(NpcDialogue npc)
    {
        if (npc != null)
            inRange.Remove(npc);

        // 走出了当前对话 NPC 的范围 → 解除锁定（台词进度由 NpcDialogue 自己保存）。
        if (currentNpc == npc)
        {
            currentNpc = null;

            if (logDebug)
                Debug.Log($"{nameof(DialogueCoordinator)}：离开 {npc.NpcName} 的交互范围，解除对话锁定。", this);
        }
    }

    /// <summary>NPC 被禁用或销毁：清出候选并解除锁定；提示、气泡与语音由 NpcDialogue 自己回收。</summary>
    private void OnNpcLost(NpcDialogue npc)
    {
        if (npc == null)
            return;

        inRange.Remove(npc);

        if (currentNpc == npc)
            currentNpc = null;

        if (nearestNpc == npc)
            nearestNpc = null;
    }

    // ─────────────────────────────── 输入 ───────────────────────────────

    private void TrySubscribeInteract()
    {
        ResolvePlayer();

        PlayerInputControl control = resolvedPlayer != null ? resolvedPlayer.inputControl : null;
        if (control == null)
            return;

        resolveInputTried = true;
        subscribedControl = control;

        // 先 -= 再 +=：委托按 target+method 比较，能正确摘掉旧订阅，防止重复 Enable 挂两份。
        subscribedControl.GamePlay.Interact.started -= OnInteractPerformed;
        subscribedControl.GamePlay.Interact.started += OnInteractPerformed;

        if (logDebug)
            Debug.Log($"{nameof(DialogueCoordinator)}：已订阅 GamePlay.Interact。", this);
    }

    private void UnsubscribeInteract()
    {
        if (subscribedControl == null)
            return;

        subscribedControl.GamePlay.Interact.started -= OnInteractPerformed;
    }

    private void OnInteractPerformed(InputAction.CallbackContext context)
    {
        // 对话已锁定到某个 NPC：这次按键只推进它，不会因为范围重叠误推进别的 NPC。
        if (currentNpc != null)
        {
            if (currentNpc.State == NpcDialogue.DialogueState.Typing)
                currentNpc.CompleteCurrentLineInstantly();
            else
                currentNpc.AdvanceToNextLine();

            // 这一按可能已经把对话结束了（末句停留够 → EndDialogueCompleted）。
            // 结束/中断后必须解锁，否则锁定会一直指向这个 NPC，之后按键全被它吃掉。
            if (currentNpc != null && !currentNpc.IsDialogueActive)
                currentNpc = null;

            return;
        }

        NpcDialogue target = PickNearestNpc();
        if (target == null)
            return;

        target.BeginOrResumeDialogue();
        currentNpc = target;

        if (logDebug)
            Debug.Log($"{nameof(DialogueCoordinator)}：与 {target.NpcName} 开始/恢复对话。", this);
    }

    // ─────────────────────────────── 目标选择 ───────────────────────────────

    /// <summary>候选里挑最近的有效 NPC：必须启用中、有台词，且此刻在玩家交互范围内。</summary>
    private NpcDialogue PickNearestNpc()
    {
        NpcDialogue best = null;
        float bestDistance = float.MaxValue;

        Vector3 origin = resolvedPlayer != null ? resolvedPlayer.transform.position : transform.position;

        for (int i = 0; i < registered.Count; i++)
        {
            NpcDialogue candidate = registered[i];
            if (!inRange.Contains(candidate))
                continue;

            if (!IsValidCandidate(candidate))
                continue;

            float distance = ((Vector2)candidate.transform.position - (Vector2)origin).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private static bool IsValidCandidate(NpcDialogue candidate)
    {
        if (candidate == null)
            return false;

        if (!candidate.isActiveAndEnabled)
            return false;

        return candidate.LineCount > 0;
    }

    private void ResolvePlayer()
    {
        if (playerResolved && resolvedPlayer != null)
            return;

        if (player != null)
        {
            resolvedPlayer = player;
            playerResolved = true;
            return;
        }

        if (!autoFindPlayer)
            return;

        // Unity 2022 只有 FindObjectOfType（FindFirstObjectByType 是 2023.1 才有的）。
        resolvedPlayer = FindObjectOfType<PlayerController>();
        playerResolved = true;

        if (resolvedPlayer == null)
            Debug.LogWarning($"{nameof(DialogueCoordinator)}：没有手动指定 player，场景里也没找到 PlayerController，交互键不会生效。", this);
    }

    /// <summary>测试/调试用：直接对当前最近 NPC 走一次按键路由（不依赖真实输入设备）。</summary>
    public void SimulateInteractForTest()
    {
        OnInteractPerformed(default(InputAction.CallbackContext));
    }
}
