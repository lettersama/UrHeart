#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

/// <summary>Reads local story receipts; only the full Steam player submits achievements.</summary>
[DisallowMultipleComponent]
public sealed class SteamAchievementService : MonoBehaviour
{
    GameDataManager data;
    SteamAchievementSync sync;
    int highestObserved;
    float nextPoll;
#if !DISABLESTEAMWORKS
    NativeBackend backend;
#endif

    public void Initialize(GameDataManager manager) => data = manager;

    public static bool CanSubmit(bool editor, bool demo, bool itch, bool testMode, bool testSession)
        => !editor && !demo && !itch && !testMode && !testSession;

    void Update()
    {
        var saves = GameAutosaveService.Instance;
        if (data == null || !data.IsInitialized || saves == null
            || !CanSubmit(Application.isEditor, GameSetup.IsDemo, GameSetup.IsItch,
                GameSetup.IsTestMode, saves.IsTestSession)) return;
        if (GameSessionFlow.Instance?.IsBusy == true || Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + .25f;

        // The menu has only preferences in SaveData. Read the already-loaded autosave receipt
        // as well, without starting Continue, loading scenes, or rewriting the player's save.
        highestObserved = Mathf.Max(highestObserved, saves.StoredStoryAchievementMilestone);
        if (saves.SessionActive)
            highestObserved = Mathf.Max(highestObserved, StoryAchievementProgress.GetReached(data.SaveData));
        // This receipt lives beside the save: menu SaveData cannot retain a
        // post-game dismissal, and a failed Steam submission must survive restart.
        highestObserved = Mathf.Max(highestObserved,
            PostGameFarewellService.Current?.CommittedAchievementMilestone ?? 0);
#if !DISABLESTEAMWORKS
        if (highestObserved == 0) return;
        if (backend == null)
        {
            backend = new NativeBackend();
            sync = new SteamAchievementSync(backend);
            backend.Stored += success => sync?.OnStatsStored(success, Time.unscaledTimeAsDouble);
        }
        sync.Tick(highestObserved, Time.unscaledTimeAsDouble);
#endif
    }

    void OnDisable()
    {
#if !DISABLESTEAMWORKS
        backend?.Dispose();
        backend = null;
#endif
        sync = null;
    }

#if !DISABLESTEAMWORKS
    sealed class NativeBackend : ISteamAchievementBackend, System.IDisposable
    {
        Callback<UserStatsStored_t> stored;
        uint appId;
        public event System.Action<bool> Stored;

        public bool Ready
        {
            get
            {
                if (!SteamManager.Initialized) return false;
                if (stored == null)
                {
                    appId = SteamUtils.GetAppID().m_AppId;
                    stored = Callback<UserStatsStored_t>.Create(OnStored);
                }
                return true;
            }
        }
        public ulong AccountId => SteamUser.GetSteamID().m_SteamID;
        public bool TryGetAchievement(string id, out bool achieved)
            => SteamUserStats.GetAchievement(id, out achieved);
        public bool UnlockAchievement(string id) => SteamUserStats.SetAchievement(id);
        public bool StoreStats() => SteamUserStats.StoreStats();

        void OnStored(UserStatsStored_t result)
        {
            if (result.m_nGameID != appId) return;
            bool success = result.m_eResult == EResult.k_EResultOK;
            if (!success) Debug.LogWarning("[Steam achievements] StoreStats failed: " + result.m_eResult + "; will retry.");
            Stored?.Invoke(success);
        }

        public void Dispose()
        {
            stored?.Dispose();
            stored = null;
        }
    }
#endif
}
