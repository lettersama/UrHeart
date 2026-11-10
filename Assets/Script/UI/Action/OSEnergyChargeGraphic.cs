using UnityEngine;
using UnityEngine.UI;

// A compact single-pixel battery outline with one continuous fill.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OSEnergyChargeGraphic : MaskableGraphic
{
    float progress;
    bool charging;
    float clock;
    float completeAge = 1f;
    static readonly Color32 Bright = new Color32(201, 190, 173, 255);

    public void SetCharge(float value, bool active)
    {
        value = Mathf.Clamp01(value);
        if (charging && !active && value >= 1f) completeAge = 0f;
        if (!charging && active) clock = 0f;
        if (value != progress || charging != active) SetVerticesDirty();
        progress = value;charging = active;
    }

    void Update()
    {
        clock += Time.unscaledDeltaTime;
        if (charging || completeAge < 1f)
        { completeAge += Time.unscaledDeltaTime;SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Color shell = completeAge < .65f ? Color.Lerp(Bright, color, completeAge / .65f) : color;
        Box(mesh, -34, 13, 64, 1, shell);Box(mesh, -34, -14, 64, 1, shell);
        Box(mesh, -35, -13, 1, 26, shell);Box(mesh, 30, -13, 1, 26, shell);
        Box(mesh, 31, -5, 3, 10, shell);
        int width = Mathf.RoundToInt(progress * 60f);
        if (width <= 0) return;
        Box(mesh, -32, -11, width, 22, shell);
        if (charging)
            Box(mesh, -33 + width, -11, 1, 22,
                Color.Lerp(shell, Bright, .35f + .25f * Mathf.Sin(clock * 3f)));
    }

    static void Box(VertexHelper mesh, int x, int y, int width, int height, Color tint)
    {
        int first = mesh.currentVertCount;
        mesh.AddVert(new Vector3(x, y), tint, Vector2.zero);
        mesh.AddVert(new Vector3(x, y + height), tint, Vector2.zero);
        mesh.AddVert(new Vector3(x + width, y + height), tint, Vector2.zero);
        mesh.AddVert(new Vector3(x + width, y), tint, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2);mesh.AddTriangle(first, first + 2, first + 3);
    }
}
