using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSToolWindowController))]
public sealed class OSToolDecorationController : MonoBehaviour
{
    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int PhaseId = Shader.PropertyToID("_Phase");
    const string IdleMascot = "(\u30fb\u03c9\u30fb)";
    const string WinkMascot = "(<\u309d\u03c9\u30fb)";

    [SerializeField] OSToolWindowController terminal;
    [SerializeField] RectTransform viewport;
    [SerializeField] Image transitionOverlay;
    [SerializeField] TMP_Text mascot;
    [SerializeField, Min(0.1f)] float animationDuration = 2.2f;
    [SerializeField, Min(1f)] float mascotInterval = 8f;
    [SerializeField, Min(0.05f)] float mascotFrameDuration = 0.2f;
    [SerializeField, Min(0f)] float mascotStarTravel = 40f;
    [SerializeField, Min(0f)] float footerHeight = 18f;

    OSWindowUnit window;
    Material transitionMaterial;
    float elapsed;
    float mascotElapsed;
    float mascotWinkWidth;
    float reservedFooterHeight;
    bool revealed;

    public bool IsAnimating { get; private set; }
    public bool HasDecoratedPresentation => revealed || GameDataManager.Instance?.IsToolDecorated == true;
    public float Progress => IsAnimating ? Mathf.Clamp01(elapsed / animationDuration) : 0f;
    public event Action DecorationCompleted;

    void Awake()
    {
        if (terminal == null) terminal = GetComponent<OSToolWindowController>();
        window = GetComponent<OSWindowUnit>();
        if (mascot != null)
        {
            mascot.richText = true;
            mascotWinkWidth = mascot.GetPreferredValues("<noparse>" + WinkMascot + "</noparse>", Mathf.Infinity, Mathf.Infinity).x;
        }
        if (transitionOverlay != null && transitionOverlay.material != null)
        {
            transitionMaterial = new Material(transitionOverlay.material);
            transitionOverlay.material = transitionMaterial;
        }
    }

    void OnDisable()
    {
        if (window == null || !window.IsOpen)
        {
            IsAnimating = false;
            revealed = false;
            elapsed = 0f;
            mascotElapsed = 0f;
        }
        SetFooterVisible(false);
        if (transitionOverlay != null) transitionOverlay.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (transitionMaterial != null) Destroy(transitionMaterial);
    }

    void Update()
    {
        GameDataManager data = GameDataManager.Instance;
        OS_Handle os = OS_Handle.Instance;
        bool visible = terminal != null && terminal.IsReady && window != null
            && window.IsOpen && !window.IsMinimized && (os == null || os.IsOSVisible);
        bool libraryOpen = terminal != null && terminal.ProgramLibrary != null && terminal.ProgramLibrary.IsOpen;
        if (!visible || data == null)
        {
            if (transitionOverlay != null) transitionOverlay.gameObject.SetActive(false);
            SetFooterVisible(false);
            return;
        }

        bool paused = (os != null && os.IsSystemInputLocked)
            || (terminal.ProgramLibrary != null && terminal.ProgramLibrary.IsInstalling);
        if (!IsAnimating && libraryOpen && !paused && data.ToolDecorationState == OSToolDecorationState.Pending)
        {
            IsAnimating = true;
            elapsed = 0f;
            terminal.CommandInput.DeactivateInputField();
        }

        if (IsAnimating)
        {
            if (!paused) elapsed += Time.unscaledDeltaTime;
            if (transitionOverlay != null) transitionOverlay.gameObject.SetActive(true);
            if (transitionMaterial != null)
            {
                transitionMaterial.SetFloat(ProgressId, Progress);
                transitionMaterial.SetFloat(PhaseId, elapsed);
            }
            if (!revealed && Progress >= 0.55f)
            {
                revealed = true;
                terminal.ProgramLibrary.RefreshPrograms();
            }
            if (elapsed >= animationDuration)
            {
                data.CompleteToolDecoration();
                IsAnimating = false;
                revealed = false;
                if (transitionOverlay != null) transitionOverlay.gameObject.SetActive(false);
                terminal.ProgramLibrary.RefreshPrograms();
                DecorationCompleted?.Invoke();
            }
        }

        bool showMascot = data.IsToolDecorated && libraryOpen && !IsAnimating;
        SetFooterVisible(showMascot);
        if (showMascot && mascot != null)
        {
            mascotElapsed += Time.unscaledDeltaTime;
            UpdateMascot();
        }
    }

    void UpdateMascot()
    {
        float winkDuration = mascotFrameDuration * 2f;
        float driftDuration = mascotFrameDuration * 6f;
        float cycle = Mathf.Repeat(mascotElapsed, mascotInterval + winkDuration + driftDuration);
        if (cycle < mascotInterval)
        {
            mascot.text = IdleMascot;
            return;
        }

        float drift = Mathf.Clamp01((cycle - mascotInterval - winkDuration) / driftDuration);
        float availableTravel = Mathf.Max(0f, mascot.rectTransform.rect.width - mascot.margin.x - mascot.margin.z
            - mascotWinkWidth - mascot.fontSize);
        float distance = Mathf.Min(mascotStarTravel, availableTravel) * Mathf.SmoothStep(0f, 1f, drift);
        int alpha = Mathf.RoundToInt(255f * (1f - Mathf.InverseLerp(0.65f, 1f, drift)));
        mascot.text = "<noparse>" + WinkMascot + "</noparse><space="
            + distance.ToString("0.##", CultureInfo.InvariantCulture) + "><alpha=#" + alpha.ToString("X2") + ">\u2606";
    }

    void SetFooterVisible(bool visible)
    {
        if (mascot != null) mascot.gameObject.SetActive(visible);
        float height = visible ? footerHeight : 0f;
        if (viewport == null || Mathf.Approximately(height, reservedFooterHeight)) return;
        Vector2 offset = viewport.offsetMin;
        offset.y += height - reservedFooterHeight;
        viewport.offsetMin = offset;
        reservedFooterHeight = height;
        terminal.RefreshContentLayout();
    }
}
