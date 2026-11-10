using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSOptionLauncherButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Header("References")]
    [SerializeField] OSWindowUnit optionWindow;
    [SerializeField] Image activeImage;

    [Header("Hover")]
    [SerializeField, Min(0f)] float fadeDuration = 0.1f;

    int lastToggleFrame = -1;

    void Awake()
    {
        ResolveReferences();
        SetActiveAlpha(0f);
    }

    void OnEnable()
    {
        ResolveReferences();
        SetActiveAlpha(0f);
    }

    void Update()
    {
        if (GameTestConsole.BlocksGameplayInput || AdditionHandle.Instance?.BlocksPaperInput == true || Keyboard.current == null
            || !Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        OS_Handle osHandle = OS_Handle.Instance;
        if (osHandle != null && !osHandle.IsOSVisible)
            return;

        // Chapter-three papers sit above the normal window stack. Give their
        // close action priority, while an already visible settings window keeps Escape.
        bool settingsVisible = optionWindow != null && optionWindow.IsOpen
            && !optionWindow.IsMinimized && optionWindow.gameObject.activeInHierarchy;
        if (!settingsVisible && osHandle?.Chapter03Story?.Presentation?.TryDismissPaperFromEscape() == true)
            return;

        // The story experiment cannot be dismissed. Escape opens its settings
        // pause directly instead of closing unrelated windows behind it.
        if (osHandle?.CursorExperiment?.IsActive == true || osHandle?.Chapter03Story?.IsMirrorSequenceActive == true
            || osHandle?.Chapter04Story?.FinalHeal?.IsRunning == true)
        {
            ToggleOptionWindow();
            return;
        }

        // Keep a keyboard exit available while only the story cursor is trapped.
        if (osHandle?.ClawMachineGame?.TryDismissCursorTrap() == true)
            return;

        PlayerCtrlManager playerCtrlManager = PlayerCtrlManager.Instance;
        if (playerCtrlManager != null
            && playerCtrlManager.EscapeClosesFrontWindow
            && osHandle != null
            && osHandle.TryCloseFrontmostNormalWindow())
        {
            return;
        }

        ToggleOptionWindow();
    }

    void OnDisable()
    {
        KillFade();
        SetActiveAlpha(0f);
    }

    void OnDestroy()
    {
        KillFade();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        FadeActiveImageTo(1f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        FadeActiveImageTo(0f);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            ToggleOptionWindow();
    }

    public bool ToggleOptionWindow()
    {
        if (AdditionHandle.Instance?.BlocksPaperInput == true) return false;
        if (OS_Handle.Instance?.Chapter01Ending?.IsPresenting == true) return false;
        if (lastToggleFrame == Time.frameCount)
            return false;

        ResolveReferences();
        if (optionWindow == null)
            return false;

        lastToggleFrame = Time.frameCount;
        bool wasOpen = optionWindow.IsOpen;
        bool wasMinimized = optionWindow.IsMinimized;
        bool wasActive = optionWindow.gameObject.activeSelf;

        if (!optionWindow.IsOpen)
            optionWindow.OpenStandalone();
        else if (optionWindow.IsMinimized
                 || !optionWindow.gameObject.activeSelf)
            optionWindow.RestoreAndBringToFront();
        else
            optionWindow.Minimize();

        if (OS_Handle.Instance?.CursorExperiment?.IsActive == true)
            OS_Handle.Instance.CursorExperiment.NotifySettingsWindow(optionWindow);

        bool changed = wasOpen != optionWindow.IsOpen
                       || wasMinimized != optionWindow.IsMinimized
                       || wasActive != optionWindow.gameObject.activeSelf;
        if (changed)
            OS_Handle.Instance?.PlayInteractionFeedback();

        return changed;
    }

    void FadeActiveImageTo(float alpha)
    {
        ResolveReferences();
        if (activeImage == null)
            return;

        activeImage.DOKill(false);
        float duration = Mathf.Max(0f, fadeDuration);
        if (duration <= 0f)
        {
            SetActiveAlpha(alpha);
            return;
        }

        activeImage
            .DOFade(Mathf.Clamp01(alpha), duration)
            .SetEase(Ease.OutSine)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
    }

    void ResolveReferences()
    {
        if (activeImage == null)
            activeImage = GetComponent<Image>();

        if (optionWindow != null)
            return;

        OS_Handle osHandle = GetComponentInParent<OS_Handle>();
        Transform root = osHandle != null
            ? osHandle.transform
            : transform.root;
        optionWindow = root
            ?.Find("Windows/WindowUnit_Option")
            ?.GetComponent<OSWindowUnit>();
    }

    void KillFade()
    {
        activeImage?.DOKill(false);
    }

    void SetActiveAlpha(float alpha)
    {
        if (activeImage == null)
            return;

        Color color = activeImage.color;
        color.a = Mathf.Clamp01(alpha);
        activeImage.color = color;
    }

    void Reset()
    {
        ResolveReferences();
        SetActiveAlpha(0f);
    }
}
