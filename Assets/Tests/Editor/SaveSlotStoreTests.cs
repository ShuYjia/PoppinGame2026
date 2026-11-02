using System;
using System.IO;
using NUnit.Framework;

public sealed class SaveSlotStoreTests
{
    private string directory;
    private SaveSlotStore store;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "PoppinSaveTests_" + Guid.NewGuid().ToString("N"));
        store = new SaveSlotStore(directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Test]
    public void EmptyStoreListsSixEmptySlots()
    {
        SaveSlotInfo[] slots = store.ListSlots();

        Assert.That(slots, Has.Length.EqualTo(SaveSlotStore.SlotCount));
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.That(slots[i].SlotId, Is.EqualTo(i + 1));
            Assert.That(slots[i].IsOccupied, Is.False);
        }
    }

    [Test]
    public void SaveLoadOverwriteAndDeleteRoundTrip()
    {
        SaveOperationResult firstSave = store.SaveSlot(1, CreateSnapshot(12));
        Assert.That(firstSave.Succeeded, Is.True);

        Assert.That(store.LoadSlot(1, out SaveSlotData firstData).Succeeded, Is.True);
        Assert.That(firstData.level.stars, Is.EqualTo(12));

        Assert.That(store.SaveSlot(1, CreateSnapshot(18)).Succeeded, Is.True);
        Assert.That(store.LoadSlot(1, out SaveSlotData overwritten).Succeeded, Is.True);
        Assert.That(overwritten.level.stars, Is.EqualTo(18));

        Assert.That(store.DeleteSlot(1).Succeeded, Is.True);
        Assert.That(store.LoadSlot(1, out _).Succeeded, Is.False);
    }

    [Test]
    public void DeletingOneSlotLeavesOtherSlotUntouched()
    {
        Assert.That(store.SaveSlot(1, CreateSnapshot(12)).Succeeded, Is.True);
        Assert.That(store.SaveSlot(2, CreateSnapshot(7)).Succeeded, Is.True);
        Assert.That(store.DeleteSlot(1).Succeeded, Is.True);

        Assert.That(store.LoadSlot(2, out SaveSlotData independent).Succeeded, Is.True);
        Assert.That(independent.level.stars, Is.EqualTo(7));
    }

    [Test]
    public void InvalidOverwriteDoesNotChangeExistingSave()
    {
        Assert.That(store.SaveSlot(1, CreateSnapshot(12)).Succeeded, Is.True);

        LevelSnapshot invalid = CreateSnapshot(50);
        invalid.hp = 0f;
        Assert.That(store.SaveSlot(1, invalid).Succeeded, Is.False);
        Assert.That(store.LoadSlot(1, out SaveSlotData original).Succeeded, Is.True);
        Assert.That(original.level.stars, Is.EqualTo(12));
    }

    [Test]
    public void CorruptFileIsReportedButCanBeDeleted()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "slot_01.json"), "{ not valid json");

        SaveSlotInfo info = store.ListSlots()[0];
        Assert.That(info.IsOccupied, Is.True);
        Assert.That(info.IsValid, Is.False);
        Assert.That(store.DeleteSlot(1).Succeeded, Is.True);
    }

    [Test]
    public void UnsupportedVersionIsShownAsInvalid()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "slot_01.json"),
            "{\"version\":99,\"slotId\":1,\"savedAtUtc\":\"2026-11-01T00:00:00.0000000Z\",\"level\":{\"scenePath\":\"Assets/Scenes/Level1.unity\",\"hp\":100,\"sp\":50}}");

        SaveSlotInfo info = store.ListSlots()[0];
        Assert.That(info.IsOccupied, Is.True);
        Assert.That(info.IsValid, Is.False);
        Assert.That(info.Error, Does.Contain("unsupported version"));
    }

    private static LevelSnapshot CreateSnapshot(int stars)
    {
        return new LevelSnapshot
        {
            scenePath = "Assets/Scenes/Level1.unity",
            playerPosition = new UnityEngine.Vector2(1f, 2f),
            hp = 100f,
            sp = 75f,
            elapsedTime = 10f,
            stars = stars
        };
    }
}
