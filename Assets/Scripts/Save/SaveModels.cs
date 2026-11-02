using System;
using UnityEngine;

[Serializable]
public sealed class SaveSlotData
{
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public int slotId;
    public string savedAtUtc;
    public LevelSnapshot level;
}

[Serializable]
public sealed class LevelSnapshot
{
    public string scenePath;
    public Vector2 playerPosition;
    public string respawnCheckpointId;
    public float hp;
    public float sp;
    public float elapsedTime;
    public int stars;
    public int kills;
    public int killPoints;
    public int comboBonus;
    public int signsRead;
    public int deaths;
    public int comboCount;
    public int currentComboBonus;
    public float secondsSinceLastKill;
    public string[] collectedIds = Array.Empty<string>();
    public string[] defeatedEnemyIds = Array.Empty<string>();
    public string[] readSignIds = Array.Empty<string>();
    public string[] activatedCheckpointIds = Array.Empty<string>();
}

public sealed class SaveSlotInfo
{
    public int SlotId;
    public bool IsOccupied;
    public bool IsValid;
    public string SavedAtUtc;
    public string ScenePath;
    public string Error;
    public LevelSnapshot Snapshot;
}

public struct SaveOperationResult
{
    public bool Succeeded;
    public string Error;

    public static SaveOperationResult Success()
    {
        return new SaveOperationResult { Succeeded = true, Error = string.Empty };
    }

    public static SaveOperationResult Failure(string error)
    {
        return new SaveOperationResult { Succeeded = false, Error = error };
    }
}
