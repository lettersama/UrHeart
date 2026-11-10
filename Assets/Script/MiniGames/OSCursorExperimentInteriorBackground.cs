using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Presentation only. The owner controls visibility, pause and draw order.
[DisallowMultipleComponent]
public sealed class OSCursorExperimentInteriorBackground : MonoBehaviour
{
    const string MaterialPath = "UI/CursorExperimentInterior";
    const string ShaderName = "UI/OS/CursorExperimentInterior";
    static readonly int ClockId = Shader.PropertyToID("_Clock");
    static readonly int ModeId = Shader.PropertyToID("_FragmentMode");
    static readonly int WipeId = Shader.PropertyToID("_Wipe");
    static readonly int WipeBoundsId = Shader.PropertyToID("_WipeBounds");
    static readonly int WipeAxisId = Shader.PropertyToID("_WipeAxis");
    static readonly Color BackgroundColor = new Color32(4, 4, 4, 255);
    // 1-bit: every surviving stroke uses the same ink; depth comes from occlusion.
    const float InkBrightness = .9f;
    // Sentinel shade marking an opaque window backing (drawn in background colour).
    const float BackingShade = -1f;

    sealed class FragmentSpec
    {
        public string label;
        public Rect bounds;
        public Rect[] strokes;
        public float[] shades;
        public bool[] restored;
        public float brightness;
    }

    // Strokes use top-left 640 x 360 coordinates, with one logical pixel edges.
    sealed class FragmentBuilder
    {
        readonly FragmentSpec spec;
        readonly List<Rect> strokes = new List<Rect>();
        readonly List<float> shades = new List<float>();
        readonly List<bool> restored = new List<bool>();
        readonly List<Rect> backings = new List<Rect>();
        bool completing;

        public FragmentBuilder(string label, float brightness)
        {
            spec = new FragmentSpec { label = label, brightness = brightness };
        }
        FragmentBuilder AddRect(float x, float y, float width, float height, float shade)
        {
            strokes.Add(new Rect(x, y, width, height));
            shades.Add(shade);
            restored.Add(completing);
            return this;
        }
        public FragmentBuilder H(float x, float y, float length, float shade = 1f) => AddRect(x, y, length, 1, shade);
        public FragmentBuilder V(float x, float y, float length, float shade = 1f) => AddRect(x, y, 1, length, shade);
        public FragmentBuilder Bit(float x, float y, float shade = .7f, float size = 3) => AddRect(x, y, size, size, shade);
        public FragmentBuilder Dots(float x, float y) => Bit(x, y, .76f, 2).Bit(x + 5, y, .64f, 2).Bit(x + 10, y, .53f, 2);
        public FragmentBuilder Outline(float x, float y, int width, int height)
        {
            return H(x, y, width).H(x, y + height - 1, width)
                .V(x, y + 1, height - 2).V(x + width - 1, y + 1, height - 2);
        }
        public FragmentBuilder Dashed(float x, float y, int width, int height)
        {
            for (int offset = 0; offset < width; offset += 4)
            {
                int length = Mathf.Min(2, width - offset);
                H(x + offset, y, length).H(x + offset, y + height - 1, length);
            }
            for (int offset = 4; offset < height - 1; offset += 4)
            {
                int length = Mathf.Min(2, height - 1 - offset);
                V(x, y + offset, length).V(x + width - 1, y + offset, length);
            }
            return this;
        }
        // The reference is a MASKED state of a whole window. Fill in its missing
        // edges, but retain which pixels were erased in the approved first frame.
        public FragmentBuilder CompleteWindow(int x, int y, int width, int height, int titleHeight = 10)
        {
            completing = true;
            CompleteLine(x, y, width, true);
            CompleteLine(x, y + height - 1, width, true);
            CompleteLine(x, y + 1, height - 2, false);
            CompleteLine(x + width - 1, y + 1, height - 2, false);
            if (titleHeight > 0) CompleteLine(x + 1, y + titleHeight, width - 2, true);
            completing = false;
            // An opaque backing lets nearer windows hide the edges of farther ones.
            backings.Add(new Rect(x + 1, y + 1, width - 2, height - 2));
            return this;
        }
        void CompleteLine(int x, int y, int length, bool horizontal)
        {
            int run = -1;
            for (int n = 0; n <= length; n++)
            {
                bool covered = n == length;
                if (!covered)
                {
                    var point = new Vector2(x + (horizontal ? n : 0) + .5f, y + (horizontal ? 0 : n) + .5f);
                    foreach (Rect stroke in strokes) if (stroke.Contains(point)) { covered = true; break; }
                }
                if (!covered && run < 0) run = n;
                if (covered && run >= 0)
                {
                    AddRect(x + (horizontal ? run : 0), y + (horizontal ? 0 : run), horizontal ? n - run : 1, horizontal ? 1 : n - run, .86f);
                    run = -1;
                }
            }
        }
        public FragmentSpec Build()
        {
            float xMin = float.PositiveInfinity, yMin = float.PositiveInfinity;
            float xMax = float.NegativeInfinity, yMax = float.NegativeInfinity;
            foreach (Rect stroke in strokes)
            {
                xMin = Mathf.Min(xMin, stroke.xMin); yMin = Mathf.Min(yMin, stroke.yMin);
                xMax = Mathf.Max(xMax, stroke.xMax); yMax = Mathf.Max(yMax, stroke.yMax);
            }
            spec.bounds = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            // Backings first so this group's own strokes draw over them.
            var allStrokes = new List<Rect>(backings); allStrokes.AddRange(strokes);
            var allShades = new List<float>(); var allRestored = new List<bool>();
            foreach (Rect _ in backings) { allShades.Add(BackingShade); allRestored.Add(false); }
            allShades.AddRange(shades); allRestored.AddRange(restored);
            spec.strokes = allStrokes.ToArray(); spec.shades = allShades.ToArray();
            spec.restored = allRestored.ToArray();
            return spec;
        }
    }
    static FragmentBuilder Group(string label, float brightness) => new FragmentBuilder(label, brightness);

    // First-frame strokes trace the approved half-erased reference. CompleteWindow
    // restores the underlying geometry, hidden initially by the spatial wipe.
    // _InkColor in the material is the absolute color ceiling.
    static readonly FragmentSpec[] Specs =
    {
        Group("UpperLeftNodes",.82f)
            .Outline(12,16,9,9).V(16,25,6).Outline(12,31,9,9)
            .V(16,40,6).Outline(12,46,9,9).V(16,55,17).H(16,72,8).Bit(22,71).Build(),
        Group("UpperLeftHeader",.96f)
            .H(58,8,100).V(58,9,11).H(59,18,99,.86f).V(157,9,20)
            .Dots(62,12).Bit(41,9).Bit(48,9,.60f).Bit(41,16,.62f).Bit(48,16,.48f)
            .Bit(167,17,.48f).Bit(174,10,.63f).CompleteWindow(58,8,100,43,10).Build(),
        Group("LeftFloatingRail",.74f)
            .H(58,49,38).Bit(59,56,.72f).Bit(67,56,.64f).CompleteWindow(58,49,38,22,6).Build(),
        Group("LeftUpperCorner",.84f)
            .H(7,85,28).V(7,86,29).V(7,117,3,.67f).Bit(10,119,.23f,2).CompleteWindow(7,85,28,36,0).Build(),
        Group("LeftDashedControl",.78f)
            .Dashed(42,98,26,24).Bit(74,102,.65f).Bit(74,109,.51f).Bit(79,116,.19f).Build(),
        Group("LeftCroppedHeader",1f)
            .H(-24,129,65).H(-24,138,65,.86f).V(40,130,44)
            .V(39,174,2,.68f).Bit(38,177,.41f,2).Bit(34,179,.20f,2).CompleteWindow(-24,129,65,62,9).Build(),
        Group("LeftControlBits",.80f)
            .Bit(8,189).Bit(8,196,.82f).Bit(15,196,.57f).Bit(8,203,.68f).Bit(15,203,.49f).Build(),
        Group("LeftLowerCorner",.88f)
            .Bit(10,231,.68f,2).H(15,231,35).V(49,232,42)
            .V(48,276,3,.50f).Bit(45,279,.29f,2)
            .Bit(58,250,.66f).Bit(58,257,.75f).Bit(58,264,.60f).CompleteWindow(-12,231,62,50,0).Build(),
        Group("LowerLeftUnderlay",.71f)
            .H(-20,267,53).V(32,268,26).CompleteWindow(-20,267,53,40,0).Build(),
        Group("BottomLeftHeader",1f)
            .H(7,293,102).H(8,303,101,.88f).V(7,294,51).V(108,294,32)
            .Dots(11,297).Bit(7,346,.45f,1).CompleteWindow(7,293,102,57,10).Build(),
        Group("BottomLeftDashed",.83f)
            .Dashed(116,307,25,24).Bit(147,315,.77f).Bit(154,316,.52f)
            .Bit(147,322,.70f).Bit(153,329,.19f).Build(),
        Group("BottomLeftOpenHeader",.93f)
            .H(72,335,69).H(73,344,102,.84f).V(72,336,18).Dots(76,339)
            .Bit(61,337,.64f).Bit(61,344,.74f).Bit(61,351,.54f).CompleteWindow(72,335,103,34,9).Build(),
        Group("BottomCentreHalfHeader",.90f)
            .H(354,321,86).V(354,322,25).Dots(358,326)
            .Bit(340,326,.66f).Bit(346,329,.59f).Bit(340,333,.62f).CompleteWindow(354,321,86,43,10).Build(),
        Group("BottomRightOpenHeader",.90f)
            .H(495,324,61).V(495,325,29).H(496,333,61,.84f).H(590,333,22,.71f)
            .Dots(499,328).Bit(616,337,.41f).Bit(623,343,.45f).CompleteWindow(495,324,118,40,9).Build(),
        Group("BottomRightDashed",.80f)
            .Dashed(560,307,27,27).Bit(594,304,.72f).Bit(594,311,.63f)
            .Bit(601,311,.22f).Bit(600,318,.23f).Bit(595,319,.16f,2).Build(),
        Group("RightTallHalfHeader",.89f)
            .H(603,161,74).V(603,162,84).H(604,170,73,.85f).Dots(607,165)
            .Bit(603,248,.38f,2).Bit(606,252,.52f).Bit(612,258,.32f).CompleteWindow(603,161,74,87,9).Build(),
        Group("RightDashedControl",.77f)
            .Dashed(569,207,26,24).Bit(561,216,.64f).Bit(561,223,.67f).Build(),
        Group("RightUpperCorner",.88f)
            .Bit(586,83,.65f,2).H(591,84,36).V(626,85,53)
            .Bit(624,140,.43f,2).Bit(620,142,.17f,2)
            .Bit(632,106,.60f).Bit(632,113,.68f).Bit(632,121,.63f).CompleteWindow(585,84,42,58,0).Build(),
        Group("UpperRightBackHeader",.95f)
            .H(452,5,92).V(452,6,13).V(543,6,10).V(543,18,3,.62f).Dots(456,9)
            .Bit(554,20,.61f).Bit(566,19,.63f).Bit(554,27,.69f)
            .Bit(559,24,.27f).Bit(566,29,.14f).CompleteWindow(452,5,92,43,10).Build(),
        Group("UpperRightForeHeader",.98f)
            .H(432,22,69).V(432,23,20).H(433,32,17,.81f).Dots(436,27)
            .V(432,45,2,.65f).CompleteWindow(432,22,69,29,10).Build()
    };

    // An owned, deterministic scheduler: random waits really change each round,
    // but never consume gameplay Random or advance while focus/settings pause us.
    sealed class WipeCycle
    {
        public enum Stage { Initial, Erasing, Dark, Revealing, Complete }
        public Stage phase;
        public float progress, elapsed, duration;
        public Vector2 axis;
        public bool initialMask;
        // 0..1 trust from the heart-to-heart: slower erasing, shorter dark gaps,
        // longer complete holds. holdComplete freezes a finished window for good.
        public float calm;
        public bool holdComplete;
        bool revealFirst;
        System.Random random;
        float start;
        public void Reset(int index)
        {
            random = new System.Random(7301 + index * 7919);
            progress = start = .56f;
            initialMask = true; revealFirst = index % 3 == 1;
            phase = Stage.Initial; elapsed = 0f;
            duration = index % 3 == 0 ? 0f : Range(.2f, 2.8f);
            ChooseAxis();
        }
        float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        void ChooseAxis()
        {
            float x = Range(.4f, 1f), y = Range(.3f, 1f);
            axis = new Vector2(random.Next(2) == 0 ? x : -x, random.Next(2) == 0 ? y : -y);
        }
        void Next()
        {
            elapsed = 0f;
            switch (phase)
            {
                case Stage.Initial:
                    if (revealFirst)
                    {
                        phase = Stage.Revealing; start = progress;
                        duration = Range(5f, 8.5f) * (1f - start);
                        break;
                    }
                    goto case Stage.Complete;
                case Stage.Complete:
                    if (holdComplete) { duration = float.MaxValue; break; }
                    phase = Stage.Erasing; start = progress;
                    duration = Range(5.5f, 9f) * start * (1f + 1.5f * calm);
                    if (start > .99f) ChooseAxis();
                    break;
                case Stage.Erasing:
                    phase = Stage.Dark; progress = 0f; initialMask = false; duration = Range(1.4f, 5.2f) * (1f - .75f * calm);
                    break;
                case Stage.Dark:
                    phase = Stage.Revealing; start = 0f; duration = Range(5f, 8.5f) * (1f - .3f * calm); ChooseAxis();
                    break;
                default:
                    phase = Stage.Complete; progress = 1f;
                    duration = holdComplete ? float.MaxValue : Range(.3f, 1.25f) * (1f + 6f * calm);
                    break;
            }
        }
        public void RevealNow(float seconds)
        {
            if (phase == Stage.Complete) { duration = float.MaxValue; elapsed = 0f; return; }
            phase = Stage.Revealing; start = progress; elapsed = 0f;
            duration = seconds * Mathf.Max(.15f, 1f - progress);
        }
        public void Advance(float seconds)
        {
            while (seconds > 0f)
            {
                float step = Mathf.Min(seconds, Mathf.Max(0f, duration - elapsed));
                elapsed += step; seconds -= step;
                float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
                if (phase == Stage.Erasing) progress = Mathf.Lerp(start, 0f, t);
                if (phase == Stage.Revealing) progress = Mathf.Lerp(start, 1f, t);
                if (elapsed >= duration) Next();
            }
        }
    }

    RectTransform root;
    CanvasGroup group;
    Material baseMaterial;
    readonly RectTransform[] fragments = new RectTransform[Specs.Length];
    readonly Material[] fragmentMaterials = new Material[Specs.Length];
    readonly OSCursorExperimentInteriorFragmentGraphic[] graphics = new OSCursorExperimentInteriorFragmentGraphic[Specs.Length];
    readonly WipeCycle[] cycles = new WipeCycle[Specs.Length];
    Vector2 laidOutSize = new Vector2(-1, -1);
    float visibility, clock, pixelSize = 1f;
    bool hasFocus, skipAdvance, paused;

    public RectTransform Root => root;
    public float Visibility { get => visibility; set => SetVisibility(value); }

    /// <summary>Heart-to-heart trust (0..1); applies from each fragment's next stage.</summary>
    public void SetCalm(float amount)
    {
        amount = Mathf.Clamp01(amount);
        foreach (var cycle in cycles) if (cycle != null) cycle.calm = amount;
    }

    /// <summary>Every fragment completes within <paramref name="seconds"/> and then holds.</summary>
    public void RevealAll(float seconds)
    {
        foreach (var cycle in cycles)
        {
            if (cycle == null) continue;
            cycle.holdComplete = true;
            cycle.RevealNow(Mathf.Max(.05f, seconds));
        }
    }
    public bool Paused
    {
        get => paused;
        set { if (paused == value) return; paused = value; skipAdvance = true; }
    }

    /// <param name="pixelSize">One art pixel in parent Canvas reference units.
    /// Does not change CanvasScaler or the screen resolution.</param>
    public static OSCursorExperimentInteriorBackground Create(RectTransform parent, float pixelSize = 1f)
    {
        if (parent == null) throw new System.ArgumentNullException(nameof(parent));
        var node = new GameObject("CursorExperimentInterior", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(RectMask2D));
        node.layer = parent.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();
        var background = node.AddComponent<OSCursorExperimentInteriorBackground>();
        background.Initialize(Mathf.Max(.01f, pixelSize));
        return background;
    }

    void Initialize(float nativePixelSize)
    {
        pixelSize = nativePixelSize;
        root = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false; group.alpha = visibility;
        Image baseImage = GetComponent<Image>();
        baseImage.raycastTarget = false; baseImage.color = BackgroundColor;
        // This serialized Resources dependency retains the shader in builds.
        Material template = Resources.Load<Material>(MaterialPath);
        Shader shader = template != null ? template.shader : Shader.Find(ShaderName);
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("Cursor experiment interior shader unavailable; using an opaque dark background.", this);
            return;
        }
        baseMaterial = MakeMaterial(template, shader, "Runtime_CursorExperimentInterior");
        baseMaterial.SetFloat(ModeId, 0f);
        baseImage.material = baseMaterial; baseImage.color = Color.white;
        for (int i = 0; i < Specs.Length; i++)
        {
            FragmentSpec spec = Specs[i];
            var node = new GameObject(spec.label, typeof(RectTransform), typeof(CanvasRenderer), typeof(OSCursorExperimentInteriorFragmentGraphic));
            node.layer = gameObject.layer;
            var rect = (RectTransform)node.transform;
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            fragments[i] = rect;
            var graphic = node.GetComponent<OSCursorExperimentInteriorFragmentGraphic>();
            graphics[i] = graphic;
            graphic.raycastTarget = false; graphic.color = Color.white;
            graphic.Configure(spec.bounds, spec.strokes, spec.shades, spec.restored, pixelSize);
            Material material = MakeMaterial(template, shader, "Runtime_" + spec.label);
            material.SetFloat(ModeId, 1f);
            material.SetVector(WipeBoundsId, new Vector4(spec.bounds.x, spec.bounds.y, spec.bounds.width, spec.bounds.height));
            fragmentMaterials[i] = material; graphic.material = material;
            cycles[i] = new WipeCycle(); cycles[i].Reset(i);
        }
        SetClock(0f);
        LayoutFragments();
    }

    static Material MakeMaterial(Material template, Shader shader, string label)
    {
        var material = template != null ? new Material(template) : new Material(shader);
        material.name = label; material.hideFlags = HideFlags.DontSave;
        return material;
    }
    public void SetVisibility(float value)
    {
        visibility = Mathf.Clamp01(value);
        if (group != null) group.alpha = visibility;
    }
    void OnEnable() { hasFocus = Application.isFocused; skipAdvance = true; }
    void OnApplicationFocus(bool focused) { hasFocus = focused; skipAdvance = true; }
    void LateUpdate()
    {
        if (root == null) return;
        if ((root.rect.size - laidOutSize).sqrMagnitude > .0001f) LayoutFragments();
        if (!hasFocus || paused || visibility <= 0f) return;
        if (skipAdvance) { skipAdvance = false; return; }
        // Freeze for settings, focus loss and hidden phases; never catch up.
        SetClock(clock + Mathf.Min(Time.unscaledDeltaTime, .05f));
    }
    void SetClock(float value)
    {
        float delta = value - clock;
        if (delta < 0f)
        {
            for (int i = 0; i < cycles.Length; i++) if (cycles[i] != null) cycles[i].Reset(i);
            delta = value;
        }
        clock = value;
        for (int i = 0; i < fragmentMaterials.Length; i++)
        {
            if (fragmentMaterials[i] == null) continue;
            WipeCycle cycle = cycles[i];
            cycle.Advance(delta);
            var wipe = new Vector4(cycle.progress, cycle.initialMask ? 1f : 0f, InkBrightness, i * 1.713f + .5f);
            var axis = new Vector4(cycle.axis.x, cycle.axis.y, 0f, 0f);
            ApplyWipe(fragmentMaterials[i], wipe, axis);
            // A parent Mask can return a stencil clone. Update what is actually
            // rendered as well, rather than leaving its cutoff frozen at creation.
            Material rendered = graphics[i].materialForRendering;
            if (rendered != null && rendered != fragmentMaterials[i]) ApplyWipe(rendered, wipe, axis);
        }
    }
    void ApplyWipe(Material material, Vector4 wipe, Vector4 axis)
    {
        material.SetFloat(ClockId, clock);
        material.SetVector(WipeId, wipe);
        material.SetVector(WipeAxisId, axis);
    }
    void LayoutFragments()
    {
        laidOutSize = root.rect.size;
        float width = Mathf.Max(1f, laidOutSize.x / pixelSize);
        float height = Mathf.Max(1f, laidOutSize.y / pixelSize);
        for (int i = 0; i < fragments.Length; i++)
        {
            if (fragments[i] == null) continue;
            Rect b = Specs[i].bounds;
            fragments[i].anchoredPosition = new Vector2(Mathf.Round(width * b.x / 640f), -Mathf.Round(height * b.y / 360f)) * pixelSize;
            fragments[i].sizeDelta = new Vector2(Mathf.Max(1, Mathf.Round(width * b.width / 640f)), Mathf.Max(1, Mathf.Round(height * b.height / 360f))) * pixelSize;
        }
    }
    void OnDestroy()
    {
        if (baseMaterial != null) Destroy(baseMaterial);
        foreach (Material material in fragmentMaterials) if (material != null) Destroy(material);
    }

#if UNITY_EDITOR
    public int DebugFragmentCount => Specs.Length;
    public float DebugClock => clock;
    public void DebugSetClock(float seconds) => SetClock(Mathf.Max(0f, seconds));
    public string DebugFragmentName(int index) => Specs[index].label;
    public float DebugFragmentOpacity(int index)
    {
        // Surviving pixels keep their ink. Visibility is spatial, never a fade.
        return cycles[index].progress > 0f ? visibility : 0f;
    }
    public float DebugWipeProgress(int index) => cycles[index].progress;
    public string DebugWipeStage(int index) => cycles[index].phase.ToString();
    public float DebugWipeDuration(int index) => cycles[index].duration;
#endif
}

// One mesh per independent silhouette avoids hundreds of UI GameObjects.
// Runtime only: never attached to serialized scenes or prefabs.
[AddComponentMenu("")]
public sealed class OSCursorExperimentInteriorFragmentGraphic : MaskableGraphic
{
    Rect authoredBounds;
    Rect[] strokes;
    float[] shades;
    bool[] restored;
    float pixelSize = 1f;
    public void Configure(Rect bounds, Rect[] authoredStrokes, float[] authoredShades, bool[] restoredStrokes, float nativePixelSize)
    {
        authoredBounds = bounds; strokes = authoredStrokes; shades = authoredShades;
        restored = restoredStrokes;
        pixelSize = nativePixelSize; SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (strokes == null || authoredBounds.width <= 0 || authoredBounds.height <= 0) return;
        Rect rect = rectTransform.rect;
        float sx = rect.width / authoredBounds.width, sy = rect.height / authoredBounds.height;
        for (int i = 0; i < strokes.Length; i++)
        {
            Rect stroke = strokes[i];
            float x0 = Mathf.Round((stroke.xMin - authoredBounds.xMin) * sx / pixelSize) * pixelSize;
            float y0 = Mathf.Round((stroke.yMin - authoredBounds.yMin) * sy / pixelSize) * pixelSize;
            float x1 = stroke.width == 1 ? x0 + pixelSize : Mathf.Round((stroke.xMax - authoredBounds.xMin) * sx / pixelSize) * pixelSize;
            float y1 = stroke.height == 1 ? y0 + pixelSize : Mathf.Round((stroke.yMax - authoredBounds.yMin) * sy / pixelSize) * pixelSize;
            // 1-bit: authored per-stroke shades are ignored; only backings differ.
            Color tint = color;
            float backing = shades[i] < 0f ? 1f : 0f;
            int first = mesh.currentVertCount;
            // Authored XY, not per-stroke 0..1 UVs: one shared spatial wipe cuts
            // every intersecting edge. Z preserves the approved initial erasure.
            float band = restored[i] ? 1f : 0f;
            mesh.AddVert(new Vector3(rect.xMin + x0, rect.yMax - y0), tint, new Vector4(stroke.xMin, stroke.yMin, band, backing));
            mesh.AddVert(new Vector3(rect.xMin + x1, rect.yMax - y0), tint, new Vector4(stroke.xMax, stroke.yMin, band, backing));
            mesh.AddVert(new Vector3(rect.xMin + x1, rect.yMax - y1), tint, new Vector4(stroke.xMax, stroke.yMax, band, backing));
            mesh.AddVert(new Vector3(rect.xMin + x0, rect.yMax - y1), tint, new Vector4(stroke.xMin, stroke.yMax, band, backing));
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
