public partial class GameDataManager
{
    // An existing collectible can gain content without being unlocked again.
    public event System.Action<int> RecordUpdated;

    public bool NotifyRecordDataUpdated(int recordIndex)
    {
        if (!IsRecordDataUnlocked(recordIndex) || !TryGetRecordData(recordIndex, out _)) return false;
        SetDesktopProcessIsNew(OSDesktopProcessKeys.Record, true);
        RecordUpdated?.Invoke(recordIndex);
        return true;
    }
}
