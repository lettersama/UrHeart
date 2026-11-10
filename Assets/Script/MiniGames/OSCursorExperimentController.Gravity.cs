using UnityEngine;

public sealed partial class OSCursorExperimentController
{
    // Falling streaks live in the open corridors: each spawns at a safe point,
    // accelerates downward, lengthens with speed and vanishes on the next wall.
    // Everything is derived from the experiment clock, so pauses freeze it.
    const int GravityStreakCount = 22;
    const float StreakGravity = 46f, StreakStartSpeed = 8f;
    RectTransform gravityField;
    readonly UnityEngine.UI.Image[] gravityStreams = new UnityEngine.UI.Image[GravityStreakCount];
    readonly UnityEngine.UI.Image[] pointerPullMarks = new UnityEngine.UI.Image[2];
    readonly UnityEngine.UI.Image[] gravityLamps = new UnityEngine.UI.Image[6];

    void InitializeGravityPresentation()
    {
        var stage = stages[1];
        var device = stage.Find("GravityPuller");
        if (device == null) return;
        gravityField = MakeRect("RuntimeGravityField", stage, new Vector2(256, 208));
        // Above the walls, beneath the pointer and the puller.
        var walls = stage.Find("Walls");
        if (walls != null) gravityField.SetSiblingIndex(walls.GetSiblingIndex() + 1);
        for (int i = 0; i < gravityStreams.Length; i++)
            gravityStreams[i] = MakeImage("Downstream" + i, gravityField, null);
        for (int i = 0; i < pointerPullMarks.Length; i++)
        {
            pointerPullMarks[i] = MakeImage("PointerPull" + i, gravityField, null);
            pointerPullMarks[i].rectTransform.sizeDelta = new Vector2(1, 2);
        }
        // Black gaps travelling through the grille toward its centre.
        for (int i = 0; i < gravityLamps.Length; i++)
        {
            gravityLamps[i] = MakeImage("IntakePulse" + i, device, null);
            gravityLamps[i].rectTransform.sizeDelta = new Vector2(2, 1);
            gravityLamps[i].color = Color.black;
        }
        gravityField.gameObject.SetActive(false);
    }

    static float GravityHash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9e3779b9u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xffffff) / 16777216f;
        }
    }

    void RenderGravityPresentation()
    {
        if (gravityField == null) return;
        bool showing = round == 2 && (phase == ExperimentPhase.RoundIntro
            || phase == ExperimentPhase.Playing || phase == ExperimentPhase.Collision);
        gravityField.gameObject.SetActive(showing);
        bool pulling = showing && phase == ExperimentPhase.Playing;
        for (int i = 0; i < gravityLamps.Length; i++)
        {
            gravityLamps[i].gameObject.SetActive(pulling);
            if (!pulling) continue;
            // Three gaps per side, flowing from each end of the grille into the middle.
            float travel = Mathf.Repeat(phaseTime * 14f + (i % 3) * 8.5f, 25f);
            float x = i < 3 ? 4f + travel : 54f - travel;
            Place(gravityLamps[i].rectTransform, new Vector2(x, 3));
        }
        if (!showing) return;
        for (int i = 0; i < gravityStreams.Length; i++)
        {
            var streak = gravityStreams[i];
            Vector2 head = Vector2.zero; int length = 0;
            bool visible = pulling && TryStreak(i, out head, out length);
            streak.gameObject.SetActive(visible);
            if (!visible) continue;
            streak.rectTransform.sizeDelta = new Vector2(1, length);
            Place(streak.rectTransform, MazeOffset + head - new Vector2(0, length - 1));
            streak.color = Ink;
        }
        // Two short marks slide down past the pointer, tying it to the field.
        for (int i = 0; i < pointerPullMarks.Length; i++)
        {
            var mark = pointerPullMarks[i];
            Vector2 native = pointer - NormalPointerHalfSize + new Vector2(i == 0 ? -3 : 11,
                -5f + Mathf.Repeat(phaseTime * 24f + i * 9f, 18f));
            native = new Vector2(Mathf.Round(native.x), Mathf.Round(native.y));
            bool clear = pulling && OSCursorMazeGeometry.IsPointSafe(layout, native, .6f)
                && OSCursorMazeGeometry.IsPointSafe(layout, native + Vector2.up, .6f);
            mark.gameObject.SetActive(clear);
            if (!clear) continue;
            Place(mark.rectTransform, MazeOffset + native);
            mark.color = Ink;
        }
    }

    bool TryStreak(int index, out Vector2 head, out int length)
    {
        head = Vector2.zero; length = 0;
        float period = 1.6f + GravityHash(index, 7) * 1.6f;
        float clock = phaseTime + GravityHash(index, 11) * period;
        int cycle = Mathf.FloorToInt(clock / period);
        float age = clock - cycle * period;
        // A short blank wait between drops keeps the field sparse.
        float wait = .25f + GravityHash(index, cycle * 3 + 1) * .6f;
        if (age < wait) return false;
        age -= wait;
        Vector2 spawn = Vector2.zero; bool found = false;
        for (int attempt = 0; attempt < 4 && !found; attempt++)
        {
            spawn = new Vector2(Mathf.Floor(6f + GravityHash(index * 5 + attempt, cycle) * 204f),
                Mathf.Floor(6f + GravityHash(index * 5 + attempt, cycle + 997) * 110f));
            found = OSCursorMazeGeometry.IsPointSafe(layout, spawn, 1.2f);
        }
        if (!found) return false;
        float fall = StreakStartSpeed * age + .5f * StreakGravity * age * age;
        float speed = StreakStartSpeed + StreakGravity * age;
        head = new Vector2(spawn.x, Mathf.Round(spawn.y + fall));
        length = Mathf.Clamp(1 + Mathf.RoundToInt(speed / 22f), 1, Mathf.Min(5, Mathf.RoundToInt(head.y - spawn.y) + 1));
        // Gone once the head reaches a wall or the bottom of the field.
        if (head.y > 140f) return false;
        for (float y = spawn.y; y <= head.y; y += 2f)
            if (!OSCursorMazeGeometry.IsPointSafe(layout, new Vector2(spawn.x, y), .6f)) return false;
        return OSCursorMazeGeometry.IsPointSafe(layout, head, .6f);
    }
}
