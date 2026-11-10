using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSToolInputRow : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    [Header("References")]
    [SerializeField] OSToolWindowController controller;
    [SerializeField] TMP_InputField commandInput;
    [SerializeField] Image hoverBackground;
    [SerializeField] CanvasGroup placeholderGroup;

    [Header("Feedback")]
    [SerializeField] Color idleColor = new Color(0.25f, 0.25f, 0.25f, 0.35f);
    [SerializeField] Color hoverColor = new Color(0.3f, 0.3f, 0.3f, 0.7f);
    [SerializeField] Color focusedColor = new Color(0.32f, 0.32f, 0.32f, 0.75f);
    [FormerlySerializedAs("idleCaretBlinkRate")]
    [SerializeField, Min(0.1f)] float activeCaretBlinkRate = 2f;

    OSWindowUnit window;
    bool isPointerInside;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        isPointerInside = false;
        RefreshFeedback();
    }

    void OnDisable()
    {
        isPointerInside = false;
        if (hoverBackground != null)
            hoverBackground.color = Color.clear;
        if (placeholderGroup != null)
            placeholderGroup.alpha = 0f;
    }

    void LateUpdate()
    {
        RefreshFeedback();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        RefreshFeedback();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        RefreshFeedback();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !CanShowFeedback())
            return;

        OS_Handle.Instance?.NotifyWindowPointerPressed(window);
        controller.RequestInputFocus();
    }

    bool CanShowFeedback()
    {
        OS_Handle os = OS_Handle.Instance;
        return controller != null && controller.IsReady
            && commandInput != null && commandInput.interactable
            && window != null && window.IsOpen && !window.IsMinimized
            && (os == null || (os.IsOSVisible && !os.IsSystemInputLocked));
    }

    void RefreshFeedback()
    {
        bool available = CanShowFeedback();
        bool focused = available && commandInput.isFocused;
        if (hoverBackground != null)
            hoverBackground.color = !available ? Color.clear
                : focused ? focusedColor : isPointerInside ? hoverColor : idleColor;

        // TMP toggles its placeholder when text changes; alpha also hides it while editing an empty command.
        if (placeholderGroup != null)
            placeholderGroup.alpha = available && !focused && string.IsNullOrEmpty(commandInput.text) ? 1f : 0f;
    }

    void ResolveReferences()
    {
        if (controller == null)
            controller = GetComponentInParent<OSToolWindowController>();
        if (window == null && controller != null)
            window = controller.GetComponent<OSWindowUnit>();
        if (commandInput == null)
            commandInput = GetComponentInChildren<TMP_InputField>(true);
        if (commandInput != null)
            commandInput.caretBlinkRate = activeCaretBlinkRate;
        if (hoverBackground == null)
            hoverBackground = GetComponent<Image>();
        if (placeholderGroup == null && commandInput != null && commandInput.placeholder != null)
            placeholderGroup = commandInput.placeholder.GetComponent<CanvasGroup>();
    }
}
