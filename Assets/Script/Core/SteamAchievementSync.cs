using System;

/// <summary>The Steam boundary is replaceable so progress/retry tests never touch a real account.</summary>
public interface ISteamAchievementBackend
{
    bool Ready { get; }
    ulong AccountId { get; }
    bool TryGetAchievement(string id, out bool achieved);
    bool UnlockAchievement(string id);
    bool StoreStats();
}

/// <summary>Submits the first N story achievements, retaining unacknowledged writes for retry.</summary>
public sealed class SteamAchievementSync
{
    public const double RetrySeconds = 15d;
    public const double StoreTimeoutSeconds = 60d;
    readonly ISteamAchievementBackend backend;
    ulong account;
    int desired, confirmed, pending, inFlight, sessionWritten;
    double nextAttempt, storeDeadline;
    public bool IsWaitingForStore { get; private set; }

    public SteamAchievementSync(ISteamAchievementBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public void Tick(int desiredMilestone, double now)
    {
        int next = Math.Max(0, Math.Min(StoryAchievementProgress.Count, desiredMilestone));
        if (next > desired) { desired = next; nextAttempt = now; }
        if (desired == 0 || !backend.Ready) return;
        ulong currentAccount = backend.AccountId;
        if (currentAccount == 0) return;
        if (account != currentAccount)
        {
            account = currentAccount;
            confirmed = pending = inFlight = sessionWritten = 0;
            IsWaitingForStore = false;
            nextAttempt = now;
        }
        if (IsWaitingForStore)
        {
            if (now < storeDeadline) return;
            // A missing callback is not confirmation. Keep every staged unlock.
            IsWaitingForStore = false;
            inFlight = 0;
        }
        if (now < nextAttempt) return;
        int wanted = (1 << desired) - 1;
        if ((confirmed & wanted) == wanted && pending == 0) return;

        int staged = 0;
        for (int milestone = 1; milestone <= desired; milestone++)
        {
            int bit = 1 << (milestone - 1);
            if ((confirmed & bit) != 0) continue;
            string id = StoryAchievementProgress.ApiName(milestone);
            if (!backend.TryGetAchievement(id, out bool achieved)) continue;
            if (achieved)
            {
                // GetAchievement also sees local SetAchievement changes whose StoreStats failed.
                if ((pending & bit) == 0) confirmed |= bit;
                else staged |= bit;
            }
            else if (backend.UnlockAchievement(id))
            {
                pending |= bit;
                staged |= bit;
                sessionWritten |= bit;
            }
        }

        nextAttempt = now + RetrySeconds;
        if (staged == 0) return;
        // A rejected StoreStats can roll Steam's cache back. Only submit receipts that
        // GetAchievement or this attempt's successful SetAchievement actually staged.
        inFlight = staged;
        IsWaitingForStore = true;
        storeDeadline = now + StoreTimeoutSeconds;
        if (!backend.StoreStats())
        {
            IsWaitingForStore = false;
            inFlight = 0;
        }
    }

    public void OnStatsStored(bool success, double now)
    {
        if (!backend.Ready || account == 0 || backend.AccountId != account) return;
        // Steam callbacks have no request ID. An older success can arrive after a
        // timeout/retry, so a later failure must requeue this session's writes even
        // when that older callback already cleared IsWaitingForStore.
        if (!success)
        {
            confirmed &= ~sessionWritten;
            pending |= sessionWritten;
            IsWaitingForStore = false;
            inFlight = 0;
            nextAttempt = now + RetrySeconds;
            return;
        }
        if (!IsWaitingForStore) return;
        IsWaitingForStore = false;
        confirmed |= inFlight;
        pending &= ~inFlight;
        inFlight = 0;
        nextAttempt = now;
    }
}
