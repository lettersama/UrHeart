using UnityEngine;

public sealed partial class OSChapter02ActivityMusic
{
    public const string DailyMusicKey = "Chapter02_DailyMusic";
    public const string WarehouseMusicKey = "Chapter02_WarehouseMusic";
    public const string ReturnHomeMusicKey = "Chapter02_ReturnHomeMusic";
    public const string MakeupMusicKey = "Chapter02_MakeupMusic";
    public const string DailyTrackId = "chapter02_daily_activity";
    public const string WarehouseTrackId = "chapter02_warehouse_activity";
    public const string ReturnHomeTrackId = "chapter02_return_home_activity";
    public const string MakeupTrackId = "chapter02_makeup_activity";

    public readonly struct MemoryMusicCue
    {
        public readonly string Key, TrackId;
        public readonly bool Silent;
        public readonly float Fade, Gain, LowPass, Distortion;

        public MemoryMusicCue(string key, string trackId, bool silent = false, float fade = .65f,
            float gain = 1f, float lowPass = 22000f, float distortion = 0f)
        {
            Key = key; TrackId = trackId; Silent = silent; Fade = fade;
            Gain = gain; LowPass = lowPass; Distortion = distortion;
        }
    }

    // Pure node policy; loading a checkpoint selects its destination directly.
    // Silent ranges retain the preceding identity so a fade is triggered once,
    // and its hard deadline can stop the same owned source without restarting it.
    public static MemoryMusicCue ResolveMemoryMusic(int node)
    {
        if (node < 4180)
            return new MemoryMusicCue(null, "chapter02_letter_silence", true, .8f);
        if (node < 4910)
            return new MemoryMusicCue(DailyMusicKey, DailyTrackId);
        if (node < 5450)
            return new MemoryMusicCue(WarehouseMusicKey, WarehouseTrackId);
        if (node < 5960)
            return new MemoryMusicCue(WarehouseMusicKey, WarehouseTrackId, true, 0f);
        if (node < 6130)
            return new MemoryMusicCue(ReturnHomeMusicKey, ReturnHomeTrackId);
        if (node < 6140)
            return new MemoryMusicCue(ReturnHomeMusicKey, ReturnHomeTrackId, true, .65f);
        if (node < 6210)
            return new MemoryMusicCue(ReturnHomeMusicKey, ReturnHomeTrackId, true, 0f);
        if (node < 6520)
            return new MemoryMusicCue(DailyMusicKey, DailyTrackId);
        if (node < 6600)
        {
            // 6590 keeps the last degraded state until the knock at 6600.
            float progress = Mathf.Clamp01((node - 6520f) / 60f);
            return new MemoryMusicCue(DailyMusicKey, DailyTrackId, false, .65f,
                Mathf.Lerp(.82f, .28f, progress), Mathf.Lerp(9000f, 1400f, progress),
                Mathf.Lerp(.04f, .18f, progress));
        }
        if (node < 6790)
            return new MemoryMusicCue(DailyMusicKey, DailyTrackId, true, 0f);
        if (node < 7340)
            return new MemoryMusicCue(MakeupMusicKey, MakeupTrackId);
        return new MemoryMusicCue(MakeupMusicKey, MakeupTrackId, true, node < 7380 ? 1f : 0f);
    }

    void RefreshMemoryMusic(OSMemoryProcessSaveData memory)
    {
        activity = Activity.Memory;
        MemoryMusicCue cue = ResolveMemoryMusic(memory.currentDataIndex);
        bool visible = os.MemoryWindowController?.Chapter02AudioAvailable == true;
        player.SetActivityMusic(this, cue.TrackId, FindMusic(cue.Key), Available && visible,
            cue.Silent, cue.Fade, true, cue.Gain, cue.LowPass, cue.Distortion, manualAvailableNow: Available);
    }
}
