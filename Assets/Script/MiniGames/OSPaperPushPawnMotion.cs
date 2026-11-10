using UnityEngine;

// Presentation only: the support's gameplay position and the round clock stay in the controller.
// Every transition starts from the currently displayed pose, so changing the illustration
// does not also snap the whole paper stand back to an upright transform.
public sealed class OSPaperPushPawnMotion
{
    public Vector2 Offset { get; private set; }
    public float Angle { get; private set; }
    public Vector2 Scale { get; private set; } = Vector2.one;
    OSPaperPushActionState state;
    OSPaperPushPose pose;
    Vector2 fromOffset, fromScale = Vector2.one;
    float fromAngle, age;
    bool initialized;

    public void Reset()
    { initialized = false;Offset = Vector2.zero;Angle = age = 0f;Scale = Vector2.one; }

    public void Tick(OSPaperPushActionState next, OSPaperPushPose nextPose, float delta, float clock, float hop, float tilt, bool player)
    {
        if (next == OSPaperPushActionState.Choosing || delta <= 0f) return;
        if (!initialized || state != next || pose != nextPose)
        {
            state = next;pose = nextPose;age = 0f;initialized = true;
            fromOffset = Offset;fromAngle = Angle - tilt;fromScale = Scale;
        }
        age += delta;
        float direction = player ? 1f : -1f;
        float angle = 0f;
        Vector2 offset = Vector2.zero, scale = Vector2.one;
        float settling = Mathf.Exp(-age * 11f);
        switch (state)
        {
            case OSPaperPushActionState.Ready:
                angle = Mathf.Sin(clock * 2f + (player ? 0f : .7f)) * .8f;
                break;
            case OSPaperPushActionState.Pushing:
                if (pose == OSPaperPushPose.Idle || hop > .1f)
                {
                    float beat = Mathf.Repeat(clock, OSPaperPushRules.HopSeconds) / OSPaperPushRules.HopSeconds;
                    angle = direction * (-3f * Mathf.Sin(beat * Mathf.PI * 2f));
                    float press = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(beat * Mathf.PI * 2f)), 6f);
                    scale = new Vector2(1f + press * .025f, 1f - press * .03f);
                }
                else
                {
                    angle = -direction * (4f + Mathf.Sin(clock * 9f) * 1.2f);
                    scale.y = 1f - .012f * (.5f + .5f * Mathf.Sin(clock * 9f));
                }
                break;
            case OSPaperPushActionState.Guarding:
                // Keep the bottom anchor planted; only the cardboard body braces.
                angle = -direction * (5f + 2f * settling);
                scale = new Vector2(1.025f, .97f);
                break;
            case OSPaperPushActionState.GuardSucceeded:
                angle = direction * (-5f + 7f * Mathf.Sin(age * 27f) * settling);
                scale = new Vector2(1.02f, .975f);
                break;
            case OSPaperPushActionState.GuardBroken:
                angle = direction * Mathf.Sin(age * 25f) * 5f * settling;
                offset.y = Mathf.Sin(Mathf.Clamp01(age / .22f) * Mathf.PI) * 2f;
                break;
            case OSPaperPushActionState.WindingUp:
                float windup = Mathf.SmoothStep(0f, 1f, age / .8f);
                angle = direction * (3f + 10f * windup);
                offset.x = -direction * 1.5f * windup;
                scale = new Vector2(1f + .025f * windup, 1f - .035f * windup);
                if (age > .7f) angle += direction * Mathf.Sin((age - .7f) * 36f) * .7f;
                break;
            case OSPaperPushActionState.Attacking:
                bool heavy = pose == OSPaperPushPose.HeavyPush;
                float thrust = Mathf.Sin(Mathf.Clamp01(age / (heavy ? .32f : .23f)) * Mathf.PI);
                angle = -direction * (heavy ? 17f : 10f) * thrust;
                offset.x = direction * (heavy ? 3f : 2f) * thrust;
                scale = new Vector2(1f - .015f * thrust, 1f + .025f * thrust);
                break;
            case OSPaperPushActionState.Stunned:
                angle = direction * Mathf.Sin(age * 29f) * 5f * settling;
                break;
            case OSPaperPushActionState.KnockedDown:
                // The controller supplies the fall angle. A single little bounce sells the landing.
                offset.y = age < .18f ? 0f : Mathf.Abs(Mathf.Sin((age - .18f) * 24f)) * 1.8f * Mathf.Exp(-(age - .18f) * 12f);
                break;
            case OSPaperPushActionState.GettingUp:
                offset.y = Mathf.Sin(Mathf.Clamp01(age / .15f) * Mathf.PI) * 2f;
                break;
            case OSPaperPushActionState.Panting:
                float breath = .5f + .5f * Mathf.Sin(age * 10f);
                angle = -direction * (4f + 2.5f * breath);
                scale.y = 1f - .025f * breath;
                break;
            case OSPaperPushActionState.Out:
                break;
        }
        float blend = Mathf.SmoothStep(0f, 1f, age / .075f);
        Offset = Vector2.LerpUnclamped(fromOffset, offset, blend);
        Angle = tilt + Mathf.LerpUnclamped(fromAngle, angle, blend);
        Scale = Vector2.LerpUnclamped(fromScale, scale, blend);
        if (state == OSPaperPushActionState.Guarding || state == OSPaperPushActionState.GuardSucceeded)
            Offset = Vector2.zero;
    }

    public static float HopTravel(float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.1f, .87f, t));
    public static float HopArc(float t) => Mathf.Sin(Mathf.InverseLerp(.1f, .87f, t) * Mathf.PI);
    public static float GetUpEase(float t)
    {
        // Small overshoot as the support springs upright, then a clean zero at the end.
        float u = Mathf.Clamp01(t) - 1f;
        return 1f + 2.2f * u * u * u + 1.2f * u * u;
    }
}
