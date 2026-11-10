using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Visual-only sets. Rules and command selection never use this clock.
[DisallowMultipleComponent]
public sealed class OSPaperPushStagePresentation : MonoBehaviour
{
    [SerializeField] OSWindowUnit window;
    [SerializeField] Image dock;
    [SerializeField] OSPaperPushAtmosphereGraphic rain;
    [SerializeField] OSPaperPushAtmosphereGraphic ribbons;
    [SerializeField] RectTransform audienceRoot;
    [SerializeField] Image[] audience;
    [SerializeField] Sprite[] audiencePoses;
    [SerializeField] TMP_Text audienceCaption;
    [SerializeField] Image curtain;
    [SerializeField] TMP_Text countdown;
    [SerializeField] Image[] poppers;
    [SerializeField] Sprite[] popperPoses;
    [SerializeField] AudioSource ambience;
    [SerializeField] AudioSource foley;
    [SerializeField] AudioClip rainClip, thunderClip, cheerClip, booClip, curtainClip, countClip;
    Material curtainInstance;
    Vector2[] audienceRest;
    int stage, crowdReaction;
    float clock, nextLightning, flash, thunderDelay = -1f, crowdClock = 10f, celebrationClock = 10f;
    bool audioPaused;
    bool lightningFirstFrame;
    bool celebrated, popSoundPlayed;
    OSPaperPushNightTableGraphic nightTable, nightTableForeground;
    GameObject nightRuleCycle;
    bool nightRuleCycleWasActive;
    float lampKnockClock = 1f;
    static readonly Color NightInk = new Color32(0x87, 0x82, 0x7e, 255);
    Graphic[] nightPaletteGraphics;
    Color[] nightOriginalColors;
    Selectable[] nightPaletteSelectables;
    ColorBlock[] nightOriginalButtonColors;
    public int Stage => stage;
    public bool IsIntroPlaying { get; private set; }
    public float CurtainClosed => curtainInstance != null ? curtainInstance.GetFloat("_Closed") : 0f;
    public float Lightning => flash;
    public bool CanPresent => window != null && window.IsOpen && !window.IsMinimized
        && OS_Handle.Instance?.IsOSVisible == true
        && (GameDataManager.Instance?.CurrentChapterIndex == 1 || GameDataManager.Instance?.CurrentChapterIndex == 2)
        && OS_Handle.Instance?.Chapter01Ending?.IsPresenting != true;

    void Awake()
    {
        audienceRest = new Vector2[audience != null ? audience.Length : 0];
        for (int i = 0; i < audienceRest.Length; i++) audienceRest[i] = audience[i].rectTransform.anchoredPosition;
    }

    public void ResetPresentation()
    {
        RestoreNightPalette();
        stage = 0;clock = 0f;flash = 0f;thunderDelay = -1f;lightningFirstFrame = false;
        IsIntroPlaying = false;crowdClock = celebrationClock = 10f;celebrated = popSoundPlayed = false;
        if (dock != null) dock.gameObject.SetActive(false);
        if (rain != null) rain.gameObject.SetActive(false);
        if (ribbons != null) ribbons.gameObject.SetActive(false);
        if (poppers != null) foreach (var popper in poppers) if (popper != null) popper.gameObject.SetActive(false);
        if (audienceRoot != null) audienceRoot.gameObject.SetActive(false);
        if (curtain != null) curtain.gameObject.SetActive(false);
        SetNightTableActive(false);
        if (nightRuleCycle != null) { nightRuleCycle.SetActive(nightRuleCycleWasActive);nightRuleCycle = null; }
        OSLocalizedText.SetRuntime(countdown, OSLocalizedText.Line.Empty);
        OSLocalizedText.SetRuntime(audienceCaption, OSLocalizedText.Line.Empty);
        if (ambience != null) ambience.Stop();
        if (foley != null) foley.Stop();audioPaused = false;
    }

    public void SetStage(int chapter, int number)
    {
        ResetPresentation();
        stage = chapter == 1 ? Mathf.Clamp(number, 1, 3) : chapter == 2 ? 4 : 0;
        if (stage == 4)
        {
            EnsureNightTable();lampKnockClock = 1f;SetNightTableActive(true);
            nightRuleCycle = window != null ? window.transform.Find("Content/Screen/RuleCycle")?.gameObject : null;
            if (nightRuleCycle != null) { nightRuleCycleWasActive = nightRuleCycle.activeSelf;nightRuleCycle.SetActive(false); }
            BeginNightPalette();
        }
        nextLightning = 5.5f;
        if (dock != null) { dock.gameObject.SetActive(stage == 2);dock.color = new Color(1, 1, 1, 0f); }
        if (rain != null) { rain.gameObject.SetActive(stage == 2);rain.SetFrame(0); }
        if (audienceRoot != null) audienceRoot.gameObject.SetActive(stage == 3);
        RenderAudience();
        if (stage == 3) { EnsureCurtain();curtainInstance?.SetFloat("_Closed", 0);curtain?.gameObject.SetActive(true); }
    }

    void Update()
    {
        int chapter = GameDataManager.Instance != null ? GameDataManager.Instance.CurrentChapterIndex : -1;
        if ((chapter != 1 && chapter != 2 || chapter == 1 && stage == 4 || chapter == 2 && stage > 0 && stage < 4)
            && stage != 0) ResetPresentation();
        if (!CanPresent || stage == 0)
        {
            if (!audioPaused)
            { ambience?.Pause();foley?.Pause();audioPaused = true; }
            return;
        }
        if (audioPaused) { ambience?.UnPause();foley?.UnPause();audioPaused = false; }
        float delta = Time.unscaledDeltaTime;clock += delta;crowdClock += delta;celebrationClock += delta;
        if (stage == 4)
        {
            lampKnockClock += delta;
            int offset = lampKnockClock < .08f ? 1 : lampKnockClock < .16f ? -1 : 0;
            nightTable?.SetLampOffset(offset);nightTableForeground?.SetLampOffset(offset);
        }
        if (stage == 2)
        {
            if (ambience != null && rainClip != null && !ambience.isPlaying)
            { ambience.clip = rainClip;ambience.loop = true;ambience.volume = .24f;ambience.Play(); }
            if (clock >= nextLightning)
            { TriggerLightning();nextLightning = clock + 9.5f + Mathf.Sin(clock * .71f) * 2f; }
            if (lightningFirstFrame) lightningFirstFrame = false;
            else flash = Mathf.Max(0f, flash - delta * 3.8f);
            if (dock != null) dock.color = new Color(1f, 1f, 1f, Mathf.Clamp01(flash / .65f));
            rain?.SetFrame(clock, flash);
            if (thunderDelay >= 0f)
            { thunderDelay -= delta;if (thunderDelay < 0f) Play(thunderClip, .45f); }
        }
        if (stage == 3)
        {
            RenderAudience();
            if (ribbons != null && ribbons.gameObject.activeSelf)
            {
                ribbons.SetFrame(celebrationClock);RenderPoppers();
                if (!popSoundPlayed && celebrationClock >= .14f) { popSoundPlayed = true;Play(countClip, .3f); }
                if (celebrationClock > 4.1f)
                { ribbons.gameObject.SetActive(false);if (poppers != null) foreach(var popper in poppers) if(popper != null) popper.gameObject.SetActive(false); }
            }
        }
    }

    void LateUpdate()
    {
        // Music and other content controls can write their normal tint in Update.
        // Only cached window-content graphics are owned by this temporary palette.
        if (stage == 4) ApplyNightPalette();
    }

    void BeginNightPalette()
    {
        var content = window != null ? window.transform.Find("Content") : null;
        if (content == null) return;
        nightPaletteGraphics = content.GetComponentsInChildren<Graphic>(true);
        nightOriginalColors = new Color[nightPaletteGraphics.Length];
        for (int i = 0; i < nightPaletteGraphics.Length; i++)
        {
            var graphic = nightPaletteGraphics[i];
            Color original = graphic.color;
            // Authored sprite pixels already contain their ink. Leave neutral
            // sprite tints and the procedural two-colour table untouched.
            var image = graphic as Image;
            if (graphic is OSPaperPushNightTableGraphic
                || image != null && image.sprite != null
                    && Mathf.Approximately(original.r, 1f)
                    && Mathf.Approximately(original.g, 1f)
                    && Mathf.Approximately(original.b, 1f))
            { nightPaletteGraphics[i] = null;continue; }
            nightOriginalColors[i] = original;
        }
        nightPaletteSelectables = content.GetComponentsInChildren<Selectable>(true);
        nightOriginalButtonColors = new ColorBlock[nightPaletteSelectables.Length];
        for (int i = 0; i < nightPaletteSelectables.Length; i++)
        {
            var control = nightPaletteSelectables[i];
            ColorBlock colors = control.colors;
            nightOriginalButtonColors[i] = colors;
            // Colour-tint multipliers would introduce extra shades on the ink.
            colors.normalColor = NeutralTint(colors.normalColor);
            colors.highlightedColor = NeutralTint(colors.highlightedColor);
            colors.pressedColor = NeutralTint(colors.pressedColor);
            colors.selectedColor = NeutralTint(colors.selectedColor);
            colors.disabledColor = NeutralTint(colors.disabledColor);
            colors.colorMultiplier = 1f;colors.fadeDuration = 0f;
            control.colors = colors;
        }
        ApplyNightPalette();
    }

    static Color NeutralTint(Color original) => new Color(1f, 1f, 1f, original.a);

    void ApplyNightPalette()
    {
        if (nightPaletteGraphics == null) return;
        for (int i = 0; i < nightPaletteGraphics.Length; i++)
        {
            var graphic = nightPaletteGraphics[i];if (graphic == null) continue;
            Color current = graphic.color;
            Color tint = current.grayscale >= .5f ? NightInk : Color.black;
            tint.a = current.a;
            if (current != tint) graphic.color = tint;
        }
    }

    void RestoreNightPalette()
    {
        if (nightPaletteGraphics == null) return;
        for (int i = 0; i < nightPaletteGraphics.Length; i++)
            if (nightPaletteGraphics[i] != null) nightPaletteGraphics[i].color = nightOriginalColors[i];
        for (int i = 0; i < nightPaletteSelectables.Length; i++)
            if (nightPaletteSelectables[i] != null) nightPaletteSelectables[i].colors = nightOriginalButtonColors[i];
        nightPaletteGraphics = null;nightOriginalColors = null;
        nightPaletteSelectables = null;nightOriginalButtonColors = null;
    }

    public void TriggerLightning()
    {
        if (stage != 2) return;
        flash = .65f;thunderDelay = .24f;lightningFirstFrame = true;
        if (dock != null) dock.color = Color.white;
        rain?.SetFrame(clock, flash);
    }

    public void TableKnock()
    {
        if (stage == 4) lampKnockClock = 0f;
    }

    void EnsureNightTable()
    {
        if (window == null) return;
        var screen = window.transform.Find("Content/Screen") as RectTransform;
        if (screen == null) return;
        var pawnStage = screen.Find("Stage");
        if (pawnStage == null) return;
        nightTable = EnsureNightTableLayer(screen, "Chapter02NightTable", OSPaperPushNightTableGraphic.RenderLayer.Background);
        nightTableForeground = EnsureNightTableLayer(screen, "Chapter02NightTableForeground", OSPaperPushNightTableGraphic.RenderLayer.TableForeground);
        nightTable.transform.SetAsFirstSibling();
        // Move out of the way before measuring Stage's index, including reuse
        // after a reopen. Other labels and overlays stay above the table.
        nightTableForeground.transform.SetAsLastSibling();
        nightTableForeground.transform.SetSiblingIndex(pawnStage.GetSiblingIndex() + 1);
    }

    static OSPaperPushNightTableGraphic EnsureNightTableLayer(RectTransform screen, string name,
        OSPaperPushNightTableGraphic.RenderLayer layer)
    {
        var rect = screen.Find(name) as RectTransform;
        if (rect == null)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            root.layer = screen.gameObject.layer;
            rect = (RectTransform)root.transform;rect.SetParent(screen, false);
        }
        rect.anchorMin = Vector2.zero;rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var graphic = rect.GetComponent<OSPaperPushNightTableGraphic>();
        if (graphic == null) graphic = rect.gameObject.AddComponent<OSPaperPushNightTableGraphic>();
        graphic.SetRenderLayer(layer);graphic.raycastTarget = false;
        return graphic;
    }

    void SetNightTableActive(bool active)
    {
        if (nightTable != null) { nightTable.SetLampOffset(0);nightTable.gameObject.SetActive(active); }
        if (nightTableForeground != null)
        { nightTableForeground.SetLampOffset(0);nightTableForeground.gameObject.SetActive(active); }
    }

    public void ReactToRound(int winner)
    {
        if (stage != 3 || !CanPresent) return;
        crowdReaction = winner == 0 ? 2 : 1;crowdClock = 0f;
        Play(winner == 0 ? booClip : cheerClip, winner == 0 ? .34f : .38f);
    }

    public void Celebrate()
    {
        if (stage != 3 || !CanPresent || celebrated
            || GameDataManager.Instance.OSEnergyLoopState.chapter01PaperPushWins < 3) return;
        celebrated = true;
        celebrationClock = 0f;crowdClock = 0f;crowdReaction = 1;
        if (ribbons != null) { ribbons.gameObject.SetActive(true);ribbons.SetFrame(0); }
        if (poppers != null) foreach (var popper in poppers) if (popper != null) popper.gameObject.SetActive(true);
        RenderPoppers();
    }

    void RenderAudience()
    {
        if (audience == null || audienceRest == null) return;
        for (int i = 0; i < audience.Length; i++)
        {
            var member = audience[i];if (member == null) continue;
            float t = crowdClock - i % 3 * .055f;
            bool responding = t >= 0 && t < 1.6f;
            int pose = responding && t >= .08f && t < 1.3f ? crowdReaction : 0;
            int index = i % 3 * 3 + pose;
            if (audiencePoses != null && index < audiencePoses.Length) member.sprite = audiencePoses[index];
            // One raised-arm pose, one hop, then settle. A boo sinks instead of hopping.
            float offset = 0;
            if (responding && crowdReaction == 1)
            {
                if (t < .16f) offset = -Mathf.Sin(t / .16f * Mathf.PI);
                else if (t < .60f) offset = Mathf.Sin((t - .16f) / .44f * Mathf.PI) * 3;
                else if (t < .76f) offset = -Mathf.Sin((t - .60f) / .16f * Mathf.PI);
            }
            else if (responding && crowdReaction == 2)
                offset = -3 * Mathf.SmoothStep(0, 1, t / .24f) * (1 - Mathf.SmoothStep(0, 1, (t - 1.1f) / .5f));
            var rect = member.rectTransform;
            rect.anchoredPosition = audienceRest[i] + new Vector2(0, Mathf.Round(offset));
            rect.localRotation = Quaternion.identity;
        }
        OSLocalizedText.SetRuntime(audienceCaption, crowdClock < 1.8f
            ? new OSLocalizedText.Line("ui.os.paper_push." + (crowdReaction == 2 ? "crowd_boo" : "crowd_cheer"), crowdReaction == 2 ? "呃。" : "噢！")
            : OSLocalizedText.Line.Empty);
    }

    void RenderPoppers()
    {
        if (poppers == null) return;
        for (int i = 0; i < poppers.Length; i++)
        {
            var popper = poppers[i];if (popper == null) continue;
            float t = celebrationClock - (i == 0 ? .14f : .23f);
            float recoil = t < 0 ? 0 : Mathf.Sin(Mathf.Clamp01(t / .28f) * Mathf.PI);
            float side = i == 0 ? 1 : -1;
            popper.rectTransform.anchoredPosition = new Vector2(side * (20 - Mathf.Round(recoil * 3)), 16 - Mathf.Round(recoil * 2));
            popper.rectTransform.localScale = new Vector3(side, 1, 1);
            if (popperPoses != null && popperPoses.Length >= 2) popper.sprite = popperPoses[t < 0 ? 0 : 1];
            popper.color = new Color(1, 1, 1, 1 - Mathf.SmoothStep(0, 1, (celebrationClock - 2.8f) / .6f));
        }
    }

    public IEnumerator PlayIntro()
    {
        if (stage != 3 || curtain == null) yield break;
        IsIntroPlaying = true;EnsureCurtain();
        if (audienceRoot != null) audienceRoot.gameObject.SetActive(false);
        Play(curtainClip, .42f);
        yield return MoveCurtain(0, 1, .75f);
        if (audienceRoot != null) audienceRoot.gameObject.SetActive(true);
        for (int n = 3; n > 0; n--)
        {
            OSLocalizedText.SetRuntime(countdown, string.Empty, n.ToString());
            Play(countClip, .25f);
            yield return VisibleWait(.9f);
        }
        OSLocalizedText.SetRuntime(countdown, OSLocalizedText.Line.Empty);
        Play(curtainClip, .42f);
        yield return MoveCurtain(1, 0, .95f);
        IsIntroPlaying = false;
    }

    IEnumerator MoveCurtain(float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds;)
        {
            yield return null;if (!CanPresent) continue;t += Time.unscaledDeltaTime;
            curtainInstance.SetFloat("_Closed", Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds)));
        }
        curtainInstance.SetFloat("_Closed", to);
    }
    IEnumerator VisibleWait(float seconds)
    { for (float t = 0f; t < seconds;) { yield return null;if (CanPresent) t += Time.unscaledDeltaTime; } }
    void EnsureCurtain()
    {
        if (curtain == null || curtainInstance != null) return;
        curtainInstance = new Material(curtain.material);curtainInstance.name = "Paper curtain (runtime)";
        curtain.material = curtainInstance;
    }
    void Play(AudioClip clip, float volume)
    { if (clip != null && foley != null && CanPresent) foley.PlayOneShot(clip, volume); }
    void OnDisable() => ResetPresentation();
    void OnDestroy() { if (curtainInstance != null) Destroy(curtainInstance); }
}
