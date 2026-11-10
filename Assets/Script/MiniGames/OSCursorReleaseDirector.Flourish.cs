using UnityEngine;
using UnityEngine.UI;

// Companion flourishes for Character's red pointer. When the player's pointer has
// rested for a while and she is parked at her home, she occasionally does one small
// thing: visits the player's pointer and circles half way round it, knocks twice on
// Live's frame, doodles a tiny heart, or hops. These only offset the *rendered*
// pointer; the idle avoidance/return state machine keeps integrating underneath, and
// any player movement retracts the flourish at once.
public sealed partial class OSCursorReleaseDirector
{
    enum Flourish { None, Visit, Knock, Heart, Hop }
    static readonly Flourish[] FlourishOrder = { Flourish.Visit, Flourish.Knock, Flourish.Heart, Flourish.Hop, Flourish.Visit, Flourish.Heart, Flourish.Knock, Flourish.Hop };
    const float PlayerRestBeforeFlourish = 4f, FlourishRetractSeconds = .25f;
    Flourish flourish;
    float flourishTime, flourishCooldown = 2f, flourishWeight, playerRest;
    int flourishSerial;
    Vector2 lastPlayerPoint, flourishPlayer, flourishOffset;
    bool lastPlayerKnown;
    Rect flourishWindow;
    Image[] heartTrail;
    int heartTrailCount;
    float heartTrailAge = -1f;
    readonly System.Random flourishRandom = new System.Random(5101);

    bool FlourishAllowed => OS_Handle.Instance?.CursorHeartTalk?.IsEngaged != true
        && (!IsEngaged || Step == ReleaseStep.ConversationHold) && !idleAvoiding && !idleReturning;

    float FlourishDuration(Flourish f) => f == Flourish.Visit ? 2.4f : f == Flourish.Knock ? 1.25f : f == Flourish.Heart ? 1.5f : .8f;

    /// <summary>Returns the rendered point for this idle frame.</summary>
    Vector2 ApplyFlourish(Vector2 idlePoint, float dt)
    {
        bool hasPlayer = TryIdlePlayerPoint(out Vector2 player);
        bool playerMoved = hasPlayer && lastPlayerKnown && (player - lastPlayerPoint).sqrMagnitude > .25f;
        playerRest = hasPlayer && !playerMoved ? playerRest + dt : 0f;
        lastPlayerPoint = player; lastPlayerKnown = hasPlayer;
        TickHeartTrail(dt);

        if (flourish != Flourish.None)
        {
            // Movement or a story beat takes priority: fold the gesture back in.
            bool abort = playerMoved || !FlourishAllowed;
            float previousTime = flourishTime;
            flourishTime += dt;
            if (!abort && flourish == Flourish.Knock && GameDataManager.Instance?.CurrentChapterIndex == 2
                && ((previousTime <= .4f && flourishTime > .4f) || (previousTime <= .64f && flourishTime > .64f)))
                Chapter02Audio.Play("Chapter02_TableTap", .55f, flourishTime < .64f ? 1f : 1.06f, .12f);
            float duration = FlourishDuration(flourish);
            if (abort || flourishTime >= duration)
            {
                flourishWeight = Mathf.MoveTowards(flourishWeight, 0f, dt / FlourishRetractSeconds);
                if (flourishWeight <= 0f) EndFlourish();
            }
            else flourishWeight = 1f;
            if (flourish != Flourish.None)
            {
                Vector2 target = FlourishPoint(idlePoint, Mathf.Min(flourishTime, duration));
                flourishOffset = target - idlePoint;
            }
            return idlePoint + flourishOffset * flourishWeight;
        }

        flourishCooldown -= dt;
        if (FlourishAllowed && playerRest >= PlayerRestBeforeFlourish && flourishCooldown <= 0f)
            StartFlourish(idlePoint, hasPlayer, player);
        return idlePoint;
    }

    void StartFlourish(Vector2 idlePoint, bool hasPlayer, Vector2 player)
    {
        Flourish next = FlourishOrder[flourishSerial++ % FlourishOrder.Length];
        bool hasWindow = TryIdleWindowBounds(out flourishWindow);
        // Visit only when the player rests nearby on her side: the trip never crosses Live.
        if (next == Flourish.Visit && (!hasPlayer || Vector2.Distance(idlePoint, player) > 150f || Vector2.Distance(idlePoint, player) < 30f
            || (hasWindow && IdleSegmentCrosses(idlePoint, player, IdleWindowKeepout(flourishWindow)))))
            next = Flourish.Hop;
        if (next == Flourish.Knock && !hasWindow) next = Flourish.Heart;
        flourish = next; flourishTime = 0f; flourishWeight = 1f; flourishPlayer = player;
        flourishOffset = Vector2.zero;
        if (flourish == Flourish.Heart) BeginHeartTrail();
        flourishCooldown = 7f + (float)flourishRandom.NextDouble() * 5f;
    }

    void EndFlourish()
    {
        if (flourish == Flourish.Knock) SetPressed(false);
        if (flourish == Flourish.Heart && heartTrailAge < 0f) heartTrailAge = 0f;
        flourish = Flourish.None; flourishWeight = 0f; flourishOffset = Vector2.zero; flourishTime = 0f;
    }

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    Vector2 FlourishPoint(Vector2 home, float t)
    {
        switch (flourish)
        {
            case Flourish.Visit:
            {
                // Approach beside the player's pointer, circle half way round, go home.
                const float radius = 18f;
                Vector2 toHome = home - flourishPlayer;
                float startAngle = Mathf.Atan2(toHome.y, toHome.x);
                Vector2 Around(float a) => flourishPlayer + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                Vector2 entry = Around(startAngle), exit = Around(startAngle + Mathf.PI);
                if (t < .7f) return Vector2.Lerp(home, entry, Smooth(t / .7f));
                if (t < 1.7f) return Around(startAngle + Mathf.PI * Smooth((t - .7f) / 1f));
                // Return round the near side, not through the player's pointer.
                Vector2 mid = Around(startAngle + Mathf.PI * 1.5f);
                float u = Smooth((t - 1.7f) / .7f);
                return Vector2.Lerp(Vector2.Lerp(exit, mid, u), Vector2.Lerp(mid, home, u), u);
            }
            case Flourish.Knock:
            {
                // Reach Live's nearest frame edge, two quick taps, back.
                Vector2 edge = new Vector2(Mathf.Clamp(home.x, flourishWindow.xMin, flourishWindow.xMax),
                    Mathf.Clamp(home.y, flourishWindow.yMin, flourishWindow.yMax));
                Vector2 inward = (edge - home).sqrMagnitude > .01f ? (edge - home).normalized : Vector2.left;
                Vector2 rest = edge - inward * 5f;
                bool tap = (t > .4f && t < .52f) || (t > .64f && t < .76f);
                SetPressed(tap);
                if (t < .4f) return Vector2.Lerp(home, rest, Smooth(t / .4f));
                if (t < .9f) return rest + inward * (tap ? 4f : 0f);
                return Vector2.Lerp(rest, home, Smooth((t - .9f) / .35f));
            }
            case Flourish.Heart:
            {
                // Classic heart curve, about 12x11 units, starting at its bottom tip.
                float a = Mathf.PI * 2f * Smooth(t / 1.35f);
                float x = 16f * Mathf.Pow(Mathf.Sin(a), 3f);
                float y = 13f * Mathf.Cos(a) - 5f * Mathf.Cos(2f * a) - 2f * Mathf.Cos(3f * a) - Mathf.Cos(4f * a);
                Vector2 point = home + new Vector2(x, y + 17f) * .36f + new Vector2(-14f, 0f);
                RecordHeartTrail(point);
                return point;
            }
            default:
            {
                float hop = Mathf.Abs(Mathf.Sin(t / .8f * Mathf.PI * 2f)) * 4f;
                return home + new Vector2(0f, Mathf.Round(hop));
            }
        }
    }

    void BeginHeartTrail()
    {
        if (visualRoot == null) return;
        if (heartTrail == null)
        {
            heartTrail = new Image[26];
            for (int i = 0; i < heartTrail.Length; i++)
            {
                heartTrail[i] = NewImage("HeartDoodle_" + i, visualRoot);
                heartTrail[i].rectTransform.sizeDelta = Vector2.one;
                heartTrail[i].color = Red;
            }
        }
        foreach (var dot in heartTrail) dot.gameObject.SetActive(false);
        heartTrailCount = 0; heartTrailAge = -1f;
    }

    void RecordHeartTrail(Vector2 point)
    {
        if (heartTrail == null || heartTrailCount >= heartTrail.Length) return;
        Vector2 snapped = new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));
        if (heartTrailCount > 0 && (heartTrail[heartTrailCount - 1].rectTransform.anchoredPosition - snapped).sqrMagnitude < 2.5f) return;
        var dot = heartTrail[heartTrailCount++];
        dot.rectTransform.anchoredPosition = snapped;
        dot.gameObject.SetActive(true);
        cursorImage.transform.SetAsLastSibling();
    }

    void TickHeartTrail(float dt)
    {
        if (heartTrail == null || heartTrailAge < 0f) return;
        heartTrailAge += dt;
        // Hold the finished doodle briefly, then blink it away.
        bool visible = heartTrailAge < .6f || (heartTrailAge < 1f && Mathf.FloorToInt((heartTrailAge - .6f) / .1f) % 2 == 1);
        for (int i = 0; i < heartTrailCount; i++) heartTrail[i].gameObject.SetActive(visible);
        if (heartTrailAge >= 1f) { heartTrailAge = -1f; heartTrailCount = 0; }
    }

    void DestroyFlourish()
    {
        if (heartTrail != null) foreach (var dot in heartTrail) if (dot != null) Destroy(dot.gameObject);
        heartTrail = null; heartTrailCount = 0; heartTrailAge = -1f;
        flourish = Flourish.None; flourishWeight = 0f; flourishOffset = Vector2.zero;
        playerRest = 0f; lastPlayerKnown = false; flourishCooldown = 2f;
    }
}
