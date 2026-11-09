using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class SaveData
{
    public int version;
    public int slot;
    public bool initialized;
    public string sceneName;
    public string levelName;
    public string checkpointId;
    public float checkpointX;
    public float checkpointY;
    public float playerX;
    public float playerY;
    public float hp;
    public float sp;
    public float elapsedTime;
    public int stars;
    public int kills;
    public int killPoints;
    public int comboBonus;
    public int signsRead;
    public int deaths;
    public int totalScore;
    public string[] collectedIds;
    public string updatedAtUtc;
}

public sealed class SaveSlotSummary
{
    public int slot;
    public bool occupied;
    public string levelName;
    public string updatedAtUtc;
    public int totalScore;

    public string DisplayText
    {
        get
        {
            if (!occupied)
                return "空槽";

            string time = updatedAtUtc;
            DateTime parsed;
            if (DateTime.TryParse(updatedAtUtc, out parsed))
                time = parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            return string.Format("{0}  |  {1}  |  分数 {2}", levelName, time, totalScore);
        }
    }
}

public static class SaveManager
{
    public const int SlotCount = 6;
    public const int CurrentVersion = 1;

    private static int activeSlot = -1;

    public static int ActiveSlot
    {
        get { return activeSlot; }
    }

    private static string SaveDirectory
    {
        get { return Path.Combine(Application.persistentDataPath, "saves"); }
    }

    public static bool HasAnySave()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            SaveData data;
            if (TryLoad(i, out data) && data.initialized)
                return true;
        }

        return false;
    }

    public static SaveSlotSummary[] GetSlotSummaries()
    {
        SaveSlotSummary[] summaries = new SaveSlotSummary[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            SaveData data;
            bool occupied = TryLoad(i, out data) && data.initialized;
            summaries[i] = new SaveSlotSummary
            {
                slot = i,
                occupied = occupied,
                levelName = occupied && !string.IsNullOrEmpty(data.levelName) ? data.levelName : "Level 1",
                updatedAtUtc = occupied ? data.updatedAtUtc : string.Empty,
                totalScore = occupied ? data.totalScore : 0
            };
        }

        return summaries;
    }

    public static SaveData CreateNew(int slot)
    {
        return CreateInitial(slot, true);
    }

    public static SaveData Overwrite(int slot)
    {
        return CreateInitial(slot, true);
    }

    public static bool Delete(int slot)
    {
        if (!IsValidSlot(slot))
            return false;

        string path = GetSlotPath(slot);
        if (File.Exists(path))
            File.Delete(path);

        if (activeSlot == slot)
            activeSlot = -1;

        return true;
    }

    public static void SetActiveSlot(int slot)
    {
        activeSlot = IsValidSlot(slot) ? slot : -1;
    }

    public static SaveData LoadActive()
    {
        if (!IsValidSlot(activeSlot))
            return null;

        SaveData data;
        return TryLoad(activeSlot, out data) ? data : null;
    }

    public static bool TryLoad(int slot, out SaveData data)
    {
        data = null;
        if (!IsValidSlot(slot))
            return false;

        string path = GetSlotPath(slot);
        if (!File.Exists(path))
            return false;

        try
        {
            data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if (data == null || data.version != CurrentVersion || data.slot != slot)
            {
                Debug.LogWarning("SaveManager: invalid or incompatible save in slot " + (slot + 1));
                data = null;
                return false;
            }

            if (data.collectedIds == null)
                data.collectedIds = new string[0];
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("SaveManager: failed to read slot " + (slot + 1) + ": " + exception.Message);
            data = null;
            return false;
        }
    }

    public static void Save(SaveData data)
    {
        if (data == null || !IsValidSlot(data.slot))
            return;

        data.version = CurrentVersion;
        data.updatedAtUtc = DateTime.UtcNow.ToString("o");
        if (data.collectedIds == null)
            data.collectedIds = new string[0];

        try
        {
            Directory.CreateDirectory(SaveDirectory);
            File.WriteAllText(GetSlotPath(data.slot), JsonUtility.ToJson(data, true));
        }
        catch (Exception exception)
        {
            Debug.LogError("SaveManager: failed to write slot " + (data.slot + 1) + ": " + exception.Message);
        }
    }

    public static string GetSlotPath(int slot)
    {
        return Path.Combine(SaveDirectory, "slot_" + (slot + 1) + ".json");
    }

    private static SaveData CreateInitial(int slot, bool activate)
    {
        if (!IsValidSlot(slot))
            return null;

        Delete(slot);
        if (activate)
            activeSlot = slot;

        SaveData data = new SaveData
        {
            version = CurrentVersion,
            slot = slot,
            initialized = false,
            sceneName = "Level1",
            levelName = "Level 1",
            checkpointId = string.Empty,
            collectedIds = new string[0]
        };
        Save(data);
        return data;
    }

    private static bool IsValidSlot(int slot)
    {
        return slot >= 0 && slot < SlotCount;
    }
}
