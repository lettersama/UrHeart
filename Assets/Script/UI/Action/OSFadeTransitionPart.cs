using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSFadeTransitionPart : MonoBehaviour
{
    const string CloseShaderName = "UI/OS/CloseTV";

    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int ReferenceResolutionId =
        Shader.PropertyToID("_ReferenceResolution");

    [Header("Presentation")]
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Image closeImage;
    [SerializeField] Material closeMaterialTemplate;

    [Header("Close Visual")]
    [SerializeField]
    Vector2Int referenceResolution = new Vector2Int(640, 360);
    [SerializeField, Min(0.05f)] float closeDuration = 0.42f;
    [SerializeField, Range(1f, 5f)] float closeEaseOutPower = 2.4f;

    [Header("Fade Out")]
    [SerializeField, Min(0f)] float fadeOutDelay = 0.3f;
    [SerializeField, Min(0f)] float fadeOutDuration = 1.2f;

    Material closeRuntimeMaterial;
    Material originalCloseMaterial;
    Coroutine transitionRoutine;
    Action transitionCompleted;
    Action transitionCanceled;
    bool transitionActive;

    void Awake()
    {
        ResolveReferences();
        CreateRuntimeMaterial();
        ResetVisual();
    }

    void OnDisable()
    {
        CancelTransition();
    }

    void OnDestroy()
    {
        CancelTransition();
        RestoreAndDestroyRuntimeMaterial();
    }

    public void FadeIN()
    {
        FadeIN(null, null);
    }

    public void FadeIN(Action onComplete)
    {
        FadeIN(onComplete, null);
    }

    public void FadeIN(Action onComplete, Action onCanceled)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "OS fade transition can only play in Play Mode.",
                this);
            InvokeSafely(onComplete);
            return;
        }

        gameObject.SetActive(true);
        ResolveReferences();
        CreateRuntimeMaterial();
        if (!ValidatePlaybackSetup())
        {
            ResetVisual();
            gameObject.SetActive(false);
            InvokeSafely(onComplete);
            return;
        }

        if (!TryBeginTransition(onComplete, onCanceled))
            return;

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;
        closeImage.enabled = true;
        closeImage.raycastTarget = true;
        ApplyCloseProgress(0f);
        Coroutine routine = StartCoroutine(PlayFadeIn());
        transitionRoutine = transitionActive ? routine : null;
    }

    public void FadeOUT()
    {
        FadeOUT(null, null);
    }

    public void FadeOUT(Action onComplete)
    {
        FadeOUT(onComplete, null);
    }

    public void FadeOUT(Action onComplete, Action onCanceled)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "OS fade transition can only play in Play Mode.",
                this);
            InvokeSafely(onComplete);
            return;
        }

        gameObject.SetActive(true);
        ResolveReferences();
        CreateRuntimeMaterial();
        if (!ValidatePlaybackSetup())
        {
            ResetVisual();
            gameObject.SetActive(false);
            InvokeSafely(onComplete);
            return;
        }

        if (!TryBeginTransition(onComplete, onCanceled))
            return;

        ApplyCloseProgress(1f);
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;
        closeImage.enabled = true;
        closeImage.raycastTarget = true;
        Coroutine routine = StartCoroutine(PlayFadeOut());
        transitionRoutine = transitionActive ? routine : null;
    }

    IEnumerator PlayFadeIn()
    {
        float duration = Mathf.Max(0.05f, closeDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float easePower = Mathf.Max(1f, closeEaseOutPower);
            float progress = 1f - Mathf.Pow(
                1f - normalizedTime,
                easePower);
            ApplyCloseProgress(progress);
            yield return null;
        }

        ApplyCloseProgress(1f);
        CompleteTransition(false);
    }

    IEnumerator PlayFadeOut()
    {
        float delay = Mathf.Max(0f, fadeOutDelay);
        float elapsed = 0f;
        while (elapsed < delay)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        float duration = Mathf.Max(0f, fadeOutDuration);
        if (duration > 0f)
        {
            elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
                yield return null;
            }
        }

        CompleteTransition(true);
    }

    void ResolveReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (closeImage == null)
        {
            Transform close = transform.Find("Close");
            if (close != null)
                closeImage = close.GetComponent<Image>();
        }
    }

    bool ValidatePlaybackSetup()
    {
        if (canvasGroup != null
            && closeImage != null
            && closeRuntimeMaterial != null)
        {
            return true;
        }

        Debug.LogError(
            "OSFadeTransitionPart requires a CanvasGroup, a Close Image, "
            + "and the OS close material.",
            this);
        return false;
    }

    void CreateRuntimeMaterial()
    {
        if (closeRuntimeMaterial != null || closeImage == null)
            return;

        originalCloseMaterial = closeImage.material;
        Material source = closeMaterialTemplate;
        if (source == null
            && originalCloseMaterial != null
            && originalCloseMaterial.shader != null
            && originalCloseMaterial.shader.name == CloseShaderName)
        {
            source = originalCloseMaterial;
        }

        if (source != null)
            closeRuntimeMaterial = new Material(source);
        else
        {
            Shader shader = Shader.Find(CloseShaderName);
            if (shader != null)
                closeRuntimeMaterial = new Material(shader);
        }

        if (closeRuntimeMaterial == null)
            return;

        closeRuntimeMaterial.name = "M_OSCloseTV_Transition (Runtime)";
        closeRuntimeMaterial.hideFlags = HideFlags.HideAndDontSave;
        closeImage.material = closeRuntimeMaterial;
        UpdateReferenceResolution();
    }

    void UpdateReferenceResolution()
    {
        if (closeRuntimeMaterial == null)
            return;

        float width = Mathf.Max(1, referenceResolution.x);
        float height = Mathf.Max(1, referenceResolution.y);
        closeRuntimeMaterial.SetVector(
            ReferenceResolutionId,
            new Vector4(width, height, 1f / width, 1f / height));
    }

    void ApplyCloseProgress(float progress)
    {
        if (closeRuntimeMaterial != null)
            closeRuntimeMaterial.SetFloat(ProgressId, Mathf.Clamp01(progress));
    }

    void ResetVisual()
    {
        ApplyCloseProgress(0f);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (closeImage != null)
        {
            closeImage.enabled = false;
            closeImage.raycastTarget = false;
        }
    }

    void CancelTransition()
    {
        if (!transitionActive)
        {
            ResetVisual();
            return;
        }

        transitionActive = false;
        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);

        transitionRoutine = null;
        Action onCanceled = transitionCanceled;
        transitionCompleted = null;
        transitionCanceled = null;
        ResetVisual();
        InvokeSafely(onCanceled);
    }

    void CompleteTransition(bool deactivate)
    {
        if (!transitionActive)
            return;

        transitionActive = false;
        transitionRoutine = null;
        Action onComplete = transitionCompleted;
        transitionCompleted = null;
        transitionCanceled = null;

        if (deactivate)
        {
            ResetVisual();
            gameObject.SetActive(false);
        }

        InvokeSafely(onComplete);
    }

    void RestoreAndDestroyRuntimeMaterial()
    {
        if (closeImage != null && closeImage.material == closeRuntimeMaterial)
            closeImage.material = originalCloseMaterial;

        if (closeRuntimeMaterial != null)
            Destroy(closeRuntimeMaterial);

        closeRuntimeMaterial = null;
    }

    void InvokeSafely(Action callback)
    {
        if (callback == null)
            return;

        try
        {
            callback.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }

    bool TryBeginTransition(Action onComplete, Action onCanceled)
    {
        if (transitionActive)
        {
            Debug.LogWarning(
                "OS fade transition is already running; the new request was canceled.",
                this);
            InvokeSafely(onCanceled);
            return false;
        }

        transitionActive = true;
        transitionCompleted = onComplete;
        transitionCanceled = onCanceled;
        return true;
    }
}
