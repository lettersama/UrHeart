using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

public enum OSHeartHealBeatMotionState
{
    Normal = 0,
    Fast = 1,
    Hidden = 2
}

public enum OSHeartHealBeatType
{
    Tap = 0,
    Hold = 1,
    Reinforced = 2
}

[Serializable]
public sealed class OSHeartHealBeatData
{
    [FormerlySerializedAs("hitTime")]
    [SerializeField, Min(0f)] float spawnTime;
    [SerializeField] OSHeartHealBeatMotionState motionState;
    [SerializeField, Min(0f)] float specialStateLeadTime = 0.85f;
    [SerializeField, Min(0f)] float specialStateDuration = 0.35f;
    [SerializeField, Min(1f)] float fastSpeedMultiplier = 1.65f;
    [SerializeField, Range(0f, 1f)] float hiddenAlpha = 0.05f;
    [SerializeField] OSHeartHealBeatType beatType;
    [SerializeField, ShowIf(nameof(IsHold)), Min(0.1f)]
    float holdDuration = 1f;
    [SerializeField, ShowIf(nameof(IsHold)), Min(0.02f)]
    float holdFeedbackInterval = 0.1f;
    [SerializeField, ShowIf(nameof(IsReinforced)), Range(0.1f, 1f)]
    float reinforcedSpeedMultiplier = 0.7f;
    [SerializeField, ShowIf(nameof(IsReinforced)), Min(1f)]
    float reinforcedOuterScale = 1.4f;
    [SerializeField, ShowIf(nameof(IsReinforced)), Min(0.1f)]
    float reinforcedSecondHitWindow = 0.35f;

    bool IsHold => beatType == OSHeartHealBeatType.Hold;
    bool IsReinforced => beatType == OSHeartHealBeatType.Reinforced;
    string InspectorLabel => IsHold
        ? $"Spawn {spawnTime:0.00}-{SpawnEndTime:0.00}s  Hold"
        : IsReinforced
            ? $"Spawn {spawnTime:0.00}s  Reinforced"
            : $"Spawn {spawnTime:0.00}s  {motionState}";

    public float SpawnTime => spawnTime;
    public OSHeartHealBeatMotionState MotionState => motionState;
    public float SpecialStateLeadTime => specialStateLeadTime;
    public float SpecialStateDuration => specialStateDuration;
    public float FastSpeedMultiplier => fastSpeedMultiplier;
    public float HiddenAlpha => hiddenAlpha;
    public OSHeartHealBeatType BeatType => beatType;
    public float HoldDuration => IsHold ? holdDuration : 0f;
    public float HoldFeedbackInterval => holdFeedbackInterval;
    public float ReinforcedSpeedMultiplier => IsReinforced
        ? Mathf.Clamp(
            reinforcedSpeedMultiplier > 0f
                ? reinforcedSpeedMultiplier
                : 0.7f,
            0.1f,
            1f)
        : 1f;
    public float ReinforcedOuterScale => IsReinforced
        ? Mathf.Max(1f, reinforcedOuterScale > 0f
            ? reinforcedOuterScale
            : 1.4f)
        : 1f;
    public float ReinforcedSecondHitWindow => IsReinforced
        ? Mathf.Max(0.1f, reinforcedSecondHitWindow > 0f
            ? reinforcedSecondHitWindow
            : 0.35f)
        : 0f;
    public float SpawnEndTime => spawnTime + HoldDuration;

    public float GetApproachDuration(float baseApproachDuration)
    {
        return Mathf.Max(0.1f, baseApproachDuration)
               / ReinforcedSpeedMultiplier;
    }

    public float GetHitTime(float approachDuration)
    {
        return spawnTime + GetApproachDuration(approachDuration);
    }

    public float GetEndTime(float approachDuration)
    {
        return GetHitTime(approachDuration) + HoldDuration;
    }

    public OSHeartHealBeatData()
    {
    }

    public OSHeartHealBeatData(
        float spawnAt,
        OSHeartHealBeatMotionState state =
            OSHeartHealBeatMotionState.Normal,
        OSHeartHealBeatType type = OSHeartHealBeatType.Tap,
        float duration = 0f,
        float feedbackInterval = 0.1f,
        float reinforcedSpeed = 0.7f,
        float reinforcedScale = 1.4f,
        float reinforcedHitWindow = 0.35f)
    {
        spawnTime = spawnAt;
        motionState = state;
        beatType = type;
        if (type == OSHeartHealBeatType.Hold)
            holdDuration = Mathf.Max(0.1f, duration);
        holdFeedbackInterval = Mathf.Max(0.02f, feedbackInterval);
        reinforcedSpeedMultiplier = Mathf.Clamp(
            reinforcedSpeed,
            0.1f,
            1f);
        reinforcedOuterScale = Mathf.Max(1f, reinforcedScale);
        reinforcedSecondHitWindow = Mathf.Max(
            0.1f,
            reinforcedHitWindow);
    }

    internal void Sanitize(float approachDuration, float hitWindowSeconds)
    {
        spawnTime = Mathf.Max(0f, spawnTime);
        reinforcedSpeedMultiplier = Mathf.Clamp(
            reinforcedSpeedMultiplier > 0f
                ? reinforcedSpeedMultiplier
                : 0.7f,
            0.1f,
            1f);
        reinforcedOuterScale = Mathf.Max(
            1f,
            reinforcedOuterScale > 0f ? reinforcedOuterScale : 1.4f);
        reinforcedSecondHitWindow = Mathf.Max(
            0.1f,
            reinforcedSecondHitWindow > 0f
                ? reinforcedSecondHitWindow
                : 0.35f);
        float effectiveApproachDuration = GetApproachDuration(
            approachDuration);
        specialStateDuration = Mathf.Clamp(
            specialStateDuration,
            0f,
            Mathf.Max(0f, effectiveApproachDuration - hitWindowSeconds));
        specialStateLeadTime = Mathf.Clamp(
            specialStateLeadTime,
            specialStateDuration + hitWindowSeconds,
            effectiveApproachDuration);
        fastSpeedMultiplier = Mathf.Max(1f, fastSpeedMultiplier);
        hiddenAlpha = Mathf.Clamp01(hiddenAlpha);
        holdDuration = Mathf.Max(0.1f, holdDuration);
        holdFeedbackInterval = Mathf.Clamp(
            holdFeedbackInterval,
            0.02f,
            holdDuration);
    }
}

[Serializable]
public sealed class OSHeartHealPuzzleData
{
    [SerializeField, Range(1, 4)] int heartIndex = 1;
    [SerializeField] AudioClip musicClip;
    [SerializeField, Range(0, 100)] int initialState = 20;
    [SerializeField, Range(0, 100), Tooltip("0 uses the global success state.")]
    int successStateOverride;
    [SerializeField, Min(0.01f)] float hitHeal = 7f;
    [SerializeField, Min(0f)] float missDamage = 4f;
    [SerializeField, Min(0.1f)] float approachDuration = 2f;
    [SerializeField, Min(0.05f)] float exitDuration = 0.35f;
    [SerializeField, Min(0f)] float chartDuration;
    [SerializeField, Min(0f)] float hardStopTime;
    [SerializeField, Min(0f)] float musicFadeOutStartTime;
    [SerializeField, Min(0f)] float musicStopTime;
    [SerializeField, Min(0f)] float holdTickHeal = 1f;
    [SerializeField, Min(1f)] float reinforcedHitHealMultiplier = 2f;
    [ListDrawerSettings(ListElementLabelName = "InspectorLabel")]
    [SerializeField]
    List<OSHeartHealBeatData> beatSequence =
        new List<OSHeartHealBeatData>();

    string InspectorLabel => chartDuration > 0f
        ? $"Heart {heartIndex}  |  {beatSequence?.Count ?? 0} Units  |  "
          + $"{chartDuration:0.0}s"
        : $"Heart {heartIndex}  |  {beatSequence?.Count ?? 0} Units";

    public int HeartIndex => heartIndex;
    public AudioClip MusicClip => musicClip;
    public int InitialState => initialState;
    public int SuccessStateOverride => successStateOverride;
    public float HitHeal => hitHeal;
    public float MissDamage => missDamage;
    public float ApproachDuration => approachDuration;
    public float ExitDuration => exitDuration;
    public float ChartDuration => chartDuration;
    public float HardStopTime => hardStopTime;
    public float MusicFadeOutStartTime => musicFadeOutStartTime;
    public float MusicStopTime => musicStopTime;
    public float HoldTickHeal => holdTickHeal;
    public float ReinforcedHitHealMultiplier =>
        reinforcedHitHealMultiplier;
    public IReadOnlyList<OSHeartHealBeatData> BeatSequence =>
        beatSequence;

    public OSHeartHealPuzzleData()
    {
    }

    public OSHeartHealPuzzleData(
        int index,
        int initial,
        float heal,
        float damage,
        List<OSHeartHealBeatData> beats,
        float duration = 0f,
        float hardStop = 0f,
        float musicFadeStart = 0f,
        float musicStop = 0f,
        float healPerHoldTick = 1f,
        float reinforcedHealMultiplier = 2f,
        int successThreshold = 0)
    {
        heartIndex = index;
        initialState = initial;
        hitHeal = heal;
        missDamage = damage;
        beatSequence = beats ?? new List<OSHeartHealBeatData>();
        chartDuration = duration;
        hardStopTime = hardStop;
        musicFadeOutStartTime = musicFadeStart;
        musicStopTime = musicStop;
        holdTickHeal = healPerHoldTick;
        reinforcedHitHealMultiplier = reinforcedHealMultiplier;
        successStateOverride = successThreshold;
    }

    public float GetHitHeal(OSHeartHealBeatType beatType)
    {
        return beatType == OSHeartHealBeatType.Reinforced
            ? hitHeal * reinforcedHitHealMultiplier
            : hitHeal;
    }

    public float GetMaximumHeal(OSHeartHealBeatData beat)
    {
        if (beat == null)
            return 0f;

        if (beat.BeatType != OSHeartHealBeatType.Hold)
            return GetHitHeal(beat.BeatType);

        float interval = Mathf.Max(0.02f, beat.HoldFeedbackInterval);
        int tickCount = Mathf.Max(
            1,
            Mathf.CeilToInt(beat.HoldDuration / interval - 0.0001f));
        return tickCount * holdTickHeal;
    }

    internal void Sanitize(float hitWindowSeconds)
    {
        heartIndex = Mathf.Clamp(heartIndex, 1, 4);
        initialState = Mathf.Clamp(initialState, 0, 100);
        successStateOverride = Mathf.Clamp(successStateOverride, 0, 100);
        hitHeal = Mathf.Max(0.01f, hitHeal);
        missDamage = Mathf.Max(0f, missDamage);
        approachDuration = Mathf.Max(0.1f, approachDuration);
        exitDuration = Mathf.Max(0.05f, exitDuration);
        chartDuration = Mathf.Max(0f, chartDuration);
        hardStopTime = Mathf.Max(0f, hardStopTime);
        musicFadeOutStartTime = Mathf.Max(0f, musicFadeOutStartTime);
        musicStopTime = Mathf.Max(
            musicFadeOutStartTime,
            musicStopTime);
        holdTickHeal = Mathf.Max(0f, holdTickHeal);
        reinforcedHitHealMultiplier = Mathf.Max(
            1f,
            reinforcedHitHealMultiplier);

        if (beatSequence == null)
            beatSequence = new List<OSHeartHealBeatData>();

        beatSequence.RemoveAll(beat => beat == null);
        for (int i = 0; i < beatSequence.Count; i++)
            beatSequence[i].Sanitize(approachDuration, hitWindowSeconds);

        beatSequence.Sort((left, right) =>
            left.SpawnTime.CompareTo(right.SpawnTime));

        float lastBeatEndTime = beatSequence.Count > 0
            ? beatSequence[beatSequence.Count - 1]
                .GetEndTime(approachDuration)
            : 0f;
        if (chartDuration > 0f)
            chartDuration = Mathf.Max(chartDuration, lastBeatEndTime);
        if (hardStopTime > 0f)
        {
            hardStopTime = Mathf.Max(
                hardStopTime,
                chartDuration > 0f ? chartDuration : lastBeatEndTime);
        }
    }
}

[CreateAssetMenu(
    menuName = "MrTGame/OS/Heart Heal Data",
    fileName = "HeartHealData")]
public sealed class HeartHealData_SO : ScriptableObject
{
    public const int MaximumHeartState = 100;

    [Header("Global Rules")]
    [SerializeField, Range(0.01f, 0.2f)]
    float hitWindowSeconds = 0.09f;
    [SerializeField, Range(1, MaximumHeartState)]
    int successState = 80;

    [Header("Visual Resources")]
    [SerializeField] Shader reinforcedBreakShader;

    [Header("Puzzles")]
    [ListDrawerSettings(ListElementLabelName = "InspectorLabel")]
    [SerializeField]
    List<OSHeartHealPuzzleData> puzzles = CreateDefaultPuzzles();

    readonly Dictionary<int, OSHeartHealPuzzleData> lookup =
        new Dictionary<int, OSHeartHealPuzzleData>();

    public float HitWindowSeconds => hitWindowSeconds;
    public int SuccessState => successState;
    public Shader ReinforcedBreakShader => reinforcedBreakShader;
    public IReadOnlyList<OSHeartHealPuzzleData> Puzzles => puzzles;

    public bool TryGetPuzzle(
        int heartIndex,
        out OSHeartHealPuzzleData puzzle)
    {
        EnsureLookup();
        return lookup.TryGetValue(heartIndex, out puzzle);
    }

    public int GetSuccessState(OSHeartHealPuzzleData puzzle)
    {
        return puzzle != null && puzzle.SuccessStateOverride > 0
            ? puzzle.SuccessStateOverride
            : successState;
    }

    public int GetEstimatedRequiredHitCount(OSHeartHealPuzzleData puzzle)
    {
        if (puzzle == null || puzzle.BeatSequence == null)
            return 0;

        int unitCount = puzzle.BeatSequence.Count;
        int requiredState = GetSuccessState(puzzle);
        float estimatedState = puzzle.InitialState
                               - unitCount * puzzle.MissDamage;
        if (estimatedState >= requiredState)
            return 0;

        var recoveredStateByHit = new List<float>(unitCount);
        for (int i = 0; i < unitCount; i++)
        {
            recoveredStateByHit.Add(
                puzzle.MissDamage
                + puzzle.GetMaximumHeal(puzzle.BeatSequence[i]));
        }

        recoveredStateByHit.Sort((left, right) => right.CompareTo(left));
        for (int hitCount = 1; hitCount <= unitCount; hitCount++)
        {
            estimatedState += recoveredStateByHit[hitCount - 1];
            if (estimatedState >= requiredState)
                return hitCount;
        }

        return unitCount + 1;
    }

    void OnEnable()
    {
        RebuildLookup();
    }

    void OnValidate()
    {
        hitWindowSeconds = Mathf.Clamp(hitWindowSeconds, 0.01f, 0.2f);
        successState = Mathf.Clamp(successState, 1, MaximumHeartState);

        if (puzzles == null)
            puzzles = new List<OSHeartHealPuzzleData>();

        for (int i = 0; i < puzzles.Count; i++)
            puzzles[i]?.Sanitize(hitWindowSeconds);

        RebuildLookup();
    }

    void EnsureLookup()
    {
        if (lookup.Count == 0)
            RebuildLookup();
    }

    void RebuildLookup()
    {
        lookup.Clear();
        if (puzzles == null)
            return;

        for (int i = 0; i < puzzles.Count; i++)
        {
            OSHeartHealPuzzleData puzzle = puzzles[i];
            if (puzzle == null)
                continue;

            if (!lookup.TryAdd(puzzle.HeartIndex, puzzle))
            {
                Debug.LogWarning(
                    $"[HeartHealData] Duplicate heartIndex "
                    + $"{puzzle.HeartIndex}; the first entry is used.",
                    this);
            }
        }
    }

    static List<OSHeartHealPuzzleData> CreateDefaultPuzzles()
    {
        return new List<OSHeartHealPuzzleData>
        {
            new OSHeartHealPuzzleData(
                1,
                20,
                1.5f,
                1f,
                CreateFirstTreatmentBeatSequence(),
                0f,
                0f,
                0f,
                0f,
                0.25f,
                2f,
                70),
            new OSHeartHealPuzzleData(
                2,
                20,
                6,
                3,
                CreateBeatSequence(
                    new[]
                    {
                        2f, 2.75f, 3.5f, 4.25f,
                        5.15f, 5.9f, 6.65f, 7.4f,
                        8.3f, 9.05f, 9.8f, 10.55f,
                        11.45f, 12.2f, 12.95f, 13.7f,
                        14.6f, 15.35f
                    },
                    new[] { 5, 10, 15 },
                    new[] { 8, 13 })),
            new OSHeartHealPuzzleData(
                3,
                0,
                7,
                4,
                CreateBeatSequence(
                    new[]
                    {
                        2f, 2.7f, 3.4f, 4.1f, 4.8f,
                        5.65f, 6.35f, 7.05f, 7.75f, 8.45f,
                        9.3f, 10f, 10.7f, 11.4f, 12.1f,
                        12.95f, 13.65f, 14.35f, 15.05f, 15.75f
                    },
                    new[] { 4, 8, 12, 17 },
                    new[] { 6, 10, 15, 19 })),
            new OSHeartHealPuzzleData(
                4,
                20,
                6,
                3,
                CreateBeatSequence(
                    new[]
                    {
                        2f, 2.65f, 3.3f, 3.95f, 4.75f,
                        5.4f, 6.05f, 6.7f, 7.5f, 8.15f,
                        8.8f, 9.45f, 10.25f, 10.9f, 11.55f,
                        12.2f, 13f, 13.65f, 14.3f, 14.95f
                    },
                    new[] { 2, 5, 9, 13, 17 },
                    new[] { 4, 7, 11, 15, 18 }))
        };
    }

    static List<OSHeartHealBeatData> CreateFirstTreatmentBeatSequence()
    {
        List<OSHeartHealBeatData> result = CreateBeatSequence(
            new[]
            {
                3f, 3.25f, 3.5f, 3.75f,
                4.8f, 5.6f, 6.2f,
                8.5f, 8.65f,
                9.3f, 9.45f, 9.6f,
                10.5f, 11.2f, 11.6f,
                 14f,
                 14.5f, 14.625f, 14.75f, 14.875f, 15f,
                 15.5f,
                 16.5f, 16.9f, 17.3f,
                18f, 18.7f, 19.4f,
                 21f,
                 24.2f, 25.4f, 26.6f,
                 28f, 28.3f,
                 30f,
                 36f,
                 37f, 37.3f,
                 42.8f
            },
            new[] { 12, 13, 14 },
            Array.Empty<int>());
        result.Add(new OSHeartHealBeatData(
            6.4f,
            OSHeartHealBeatMotionState.Normal,
            OSHeartHealBeatType.Hold,
            1.6f,
            0.1f));
        result.Add(new OSHeartHealBeatData(
            21.4f,
            OSHeartHealBeatMotionState.Normal,
            OSHeartHealBeatType.Hold,
            1.6f,
            0.1f));
        result.Add(new OSHeartHealBeatData(
            34f,
            OSHeartHealBeatMotionState.Normal,
            OSHeartHealBeatType.Reinforced));
        result.Add(new OSHeartHealBeatData(
            38f,
            OSHeartHealBeatMotionState.Normal,
            OSHeartHealBeatType.Hold,
            1.5f,
            0.1f));
        result.Add(new OSHeartHealBeatData(
            39.8f,
            OSHeartHealBeatMotionState.Normal,
            OSHeartHealBeatType.Reinforced));
        result.Add(new OSHeartHealBeatData(
            41.5f,
            OSHeartHealBeatMotionState.Normal,
            OSHeartHealBeatType.Hold,
            1f,
            0.1f));
        result.Sort((left, right) =>
            left.SpawnTime.CompareTo(right.SpawnTime));
        return result;
    }

    static List<OSHeartHealBeatData> CreateBeatSequence(
        float[] spawnTimes,
        int[] fastIndices,
        int[] hiddenIndices)
    {
        var result = new List<OSHeartHealBeatData>(spawnTimes.Length);
        var fastLookup = new HashSet<int>(fastIndices);
        var hiddenLookup = new HashSet<int>(hiddenIndices);
        for (int i = 0; i < spawnTimes.Length; i++)
        {
            OSHeartHealBeatMotionState state = hiddenLookup.Contains(i)
                ? OSHeartHealBeatMotionState.Hidden
                : fastLookup.Contains(i)
                    ? OSHeartHealBeatMotionState.Fast
                    : OSHeartHealBeatMotionState.Normal;
            result.Add(new OSHeartHealBeatData(spawnTimes[i], state));
        }

        return result;
    }
}
