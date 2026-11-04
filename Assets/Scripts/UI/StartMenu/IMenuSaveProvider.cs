using System;

/// <summary>
/// StartMenu 与存档系统之间的占位接口。
/// 槽位代表“完整周目档案”，不是检查点或关卡位置。
/// TODO: 接入肉鸽大存档系统时，由真实 provider 实现此接口；
/// StartMenu UI 不应依赖旧的 SaveManager、PlayerPrefs 或 JSON 存档实现。
/// </summary>
public interface IMenuSaveProvider
{
    /// <summary>周目档案槽位数量。</summary>
    int SlotCount { get; }

    /// <summary>是否至少存在一个有效存档。TODO: 接入真实周目档案后由存档层提供。</summary>
    bool HasAnySave { get; }

    /// <summary>获取全部槽位摘要。TODO: 接入真实周目档案后返回周目进度、通关次数等摘要。</summary>
    MenuSaveSlotSummary[] GetSlotSummaries();

    /// <summary>在指定槽位创建新周目档案。TODO: 接入真实大存档系统。</summary>
    void CreateNew(int slot);

    /// <summary>读取指定槽位的周目档案。TODO: 接入真实大存档系统。</summary>
    void Load(int slot);

    /// <summary>覆盖指定槽位的周目档案。TODO: 接入真实大存档系统。</summary>
    void Overwrite(int slot);

    /// <summary>删除指定槽位的周目档案。TODO: 接入真实大存档系统。</summary>
    void Delete(int slot);
}

/// <summary>
/// 一个槽位的展示摘要，由 provider 填充，StartMenu 只负责显示。
/// </summary>
[Serializable]
public struct MenuSaveSlotSummary
{
    public bool occupied;
    public string title;
    public string detail;
    public int runCount;
    public int clearCount;
}
