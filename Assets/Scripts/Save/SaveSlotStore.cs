using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Stores six independent local save slots as versioned JSON files.</summary>
public sealed class SaveSlotStore
{
    public const int SlotCount = 6;

    private readonly string directoryPath;

    public SaveSlotStore()
        : this(Path.Combine(Application.persistentDataPath, "Saves"))
    {
    }

    public SaveSlotStore(string directoryPath)
    {
        this.directoryPath = directoryPath;
    }

    public SaveSlotInfo[] ListSlots()
    {
        SaveSlotInfo[] slots = new SaveSlotInfo[SlotCount];
        for (int slotId = 1; slotId <= SlotCount; slotId++)
        {
            SaveSlotInfo info = new SaveSlotInfo { SlotId = slotId };
            string path = GetSlotPath(slotId);
            if (!File.Exists(path))
            {
                slots[slotId - 1] = info;
                continue;
            }

            info.IsOccupied = true;
            SaveOperationResult result = ReadSlot(slotId, out SaveSlotData data);
            info.IsValid = result.Succeeded;
            info.Error = result.Error;
            if (result.Succeeded)
            {
                info.SavedAtUtc = data.savedAtUtc;
                info.ScenePath = data.level.scenePath;
                info.Snapshot = data.level;
            }
            slots[slotId - 1] = info;
        }

        return slots;
    }

    public SaveOperationResult SaveSlot(int slotId, LevelSnapshot snapshot)
    {
        if (!IsValidSlotId(slotId))
            return SaveOperationResult.Failure("Invalid save slot.");
        if (snapshot == null)
            return SaveOperationResult.Failure("No game state was provided.");
        if (string.IsNullOrWhiteSpace(snapshot.scenePath))
            return SaveOperationResult.Failure("The current scene has no saved path.");
        if (!IsWellFormed(snapshot))
            return SaveOperationResult.Failure("Game state contains invalid values.");

        SaveSlotData data = new SaveSlotData
        {
            version = SaveSlotData.CurrentVersion,
            slotId = slotId,
            savedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            level = snapshot
        };

        string targetPath = GetSlotPath(slotId);
        string temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));

            if (File.Exists(targetPath))
            {
                string backupPath = temporaryPath + ".bak";
                try
                {
                    File.Replace(temporaryPath, targetPath, backupPath);
                    TryDelete(backupPath);
                }
                catch (PlatformNotSupportedException)
                {
                    ReplaceWithRollback(temporaryPath, targetPath, backupPath);
                }
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }

            return SaveOperationResult.Success();
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            return SaveOperationResult.Failure("Save failed: " + exception.Message);
        }
    }

    public SaveOperationResult LoadSlot(int slotId, out SaveSlotData data)
    {
        return ReadSlot(slotId, out data);
    }

    public SaveOperationResult DeleteSlot(int slotId)
    {
        if (!IsValidSlotId(slotId))
            return SaveOperationResult.Failure("Invalid save slot.");

        try
        {
            string path = GetSlotPath(slotId);
            if (File.Exists(path))
                File.Delete(path);
            return SaveOperationResult.Success();
        }
        catch (Exception exception)
        {
            return SaveOperationResult.Failure("Delete failed: " + exception.Message);
        }
    }

    private SaveOperationResult ReadSlot(int slotId, out SaveSlotData data)
    {
        data = null;
        if (!IsValidSlotId(slotId))
            return SaveOperationResult.Failure("Invalid save slot.");

        string path = GetSlotPath(slotId);
        if (!File.Exists(path))
            return SaveOperationResult.Failure("This slot is empty.");

        try
        {
            data = JsonUtility.FromJson<SaveSlotData>(File.ReadAllText(path));
            if (data == null || data.level == null)
                return SaveOperationResult.Failure("Save data is incomplete or corrupted.");
            if (data.version != SaveSlotData.CurrentVersion)
                return SaveOperationResult.Failure("This save uses an unsupported version.");
            if (data.slotId != slotId)
                return SaveOperationResult.Failure("Save slot metadata does not match its file.");
            if (string.IsNullOrWhiteSpace(data.level.scenePath))
                return SaveOperationResult.Failure("Save data has no scene path.");
            if (!IsWellFormed(data.level))
                return SaveOperationResult.Failure("Save data contains invalid values.");

            return SaveOperationResult.Success();
        }
        catch (Exception exception)
        {
            data = null;
            return SaveOperationResult.Failure("Save data is corrupted: " + exception.Message);
        }
    }

    private string GetSlotPath(int slotId)
    {
        return Path.Combine(directoryPath, "slot_" + slotId.ToString("00", CultureInfo.InvariantCulture) + ".json");
    }

    private static bool IsValidSlotId(int slotId)
    {
        return slotId >= 1 && slotId <= SlotCount;
    }

    private static bool IsWellFormed(LevelSnapshot snapshot)
    {
        return IsFinite(snapshot.playerPosition.x)
               && IsFinite(snapshot.playerPosition.y)
               && IsFinite(snapshot.hp)
               && snapshot.hp > 0f
               && IsFinite(snapshot.sp)
               && snapshot.sp >= 0f
               && IsFinite(snapshot.elapsedTime)
               && snapshot.elapsedTime >= 0f
               && snapshot.stars >= 0
               && snapshot.kills >= 0
               && snapshot.killPoints >= 0
               && snapshot.comboBonus >= 0
               && snapshot.signsRead >= 0
               && snapshot.deaths >= 0
               && snapshot.comboCount >= 0
               && snapshot.currentComboBonus >= 0
               && IsFinite(snapshot.secondsSinceLastKill)
               && snapshot.secondsSinceLastKill >= -1f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static void ReplaceWithRollback(string temporaryPath, string targetPath, string backupPath)
    {
        if (File.Exists(backupPath))
            File.Delete(backupPath);

        File.Move(targetPath, backupPath);
        try
        {
            File.Move(temporaryPath, targetPath);
            TryDelete(backupPath);
        }
        catch
        {
            if (!File.Exists(targetPath) && File.Exists(backupPath))
                File.Move(backupPath, targetPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Preserve the original operation error.
        }
    }
}
