using UnityEngine;

/// <summary>
/// 菜单阶段的占位 provider：只保存当前菜单选择，不读写任何真实存档数据。
/// 不调用旧 SaveManager，不写 PlayerPrefs，不读写 JSON。
/// TODO: 真实肉鸽大存档系统接入时替换本组件，StartMenuShell 只依赖 IMenuSaveProvider。
/// </summary>
public sealed class PlaceholderSaveProvider : MonoBehaviour, IMenuSaveProvider
{
    private const int DefaultSlotCount = 6;

    [SerializeField] private int slotCount = DefaultSlotCount;

    private int trialSlot = -1;

    public int SlotCount
    {
        get { return slotCount < 1 ? DefaultSlotCount : slotCount; }
    }

    public bool HasAnySave
    {
        // 占位 provider 永远没有真实存档，主菜单的 Continue 因此默认隐藏。
        get { return false; }
    }

    /// <summary>当前菜单选中的试玩槽位，仅用于占位状态，不产生真实进度。</summary>
    public int TrialSlot
    {
        get { return trialSlot; }
    }

    public MenuSaveSlotSummary[] GetSlotSummaries()
    {
        MenuSaveSlotSummary[] summaries = new MenuSaveSlotSummary[SlotCount];
        for (int i = 0; i < summaries.Length; i++)
        {
            summaries[i].occupied = false;
            summaries[i].title = "存档 " + (i + 1);
            summaries[i].detail = "空槽";
            summaries[i].runCount = 0;
            summaries[i].clearCount = 0;
        }

        return summaries;
    }

    public void CreateNew(int slot)
    {
        trialSlot = slot;
        // TODO: 接入真实大存档系统时，在此创建完整周目档案。
        Debug.Log("[PlaceholderSaveProvider] CreateNew(slot=" + slot + ") 仅记录试玩选择，未创建真实存档。");
    }

    public void Load(int slot)
    {
        trialSlot = slot;
        // TODO: 接入真实大存档系统时，在此读取指定槽位的周目档案。
        Debug.Log("[PlaceholderSaveProvider] Load(slot=" + slot + ") 未读取真实存档。");
    }

    public void Overwrite(int slot)
    {
        trialSlot = slot;
        // TODO: 接入真实大存档系统时，在此覆盖指定槽位的周目档案。
        Debug.Log("[PlaceholderSaveProvider] Overwrite(slot=" + slot + ") 未修改真实存档。");
    }

    public void Delete(int slot)
    {
        if (trialSlot == slot)
            trialSlot = -1;

        // TODO: 接入真实大存档系统时，在此删除指定槽位的周目档案。
        Debug.Log("[PlaceholderSaveProvider] Delete(slot=" + slot + ") 未删除真实存档。");
    }

    /// <summary>记录当前试玩槽位；不产生真实存档。</summary>
    public void BeginTrial(int slot)
    {
        trialSlot = slot;
        Debug.Log("[PlaceholderSaveProvider] BeginTrial(slot=" + slot + ") 试玩模式，未写入存档。");
    }
}
