using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class GameTestOSCameraBridge : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] string mainOSCanvasName = "Main_OS_Canvs";
    [SerializeField] string setupScene = "GameSetUp";
    [SerializeField] string sideScrollScene = "Game_Main";
    [SerializeField] string osScene = "GameUI";

    Canvas osCanvas;
    CanvasGroup osCanvasGroup;
    OS_Handle osHandle;
    OSCameraHandle cameraHandle;
    CursorHandle cursorHandle;
    bool hasLastVisibility;
    bool lastVisibility;
    bool ownsCameraPresentation;
    bool ownsCursorPresentation;
    bool ownsScenePresentation;
    bool hasStarted;
    bool hasWarnedMissingCamera;

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
    }

    void Start()
    {
        if (!GameSetup.IsTestMode)
        {
            enabled = false;
            return;
        }

        hasStarted = true;
        SynchronizePresentation(true);
    }

    void LateUpdate()
    {
        if (hasStarted)
            SynchronizePresentation(false);
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;

        if (Application.isPlaying)
            ReleaseTestPresentation();

        hasStarted = false;
        hasLastVisibility = false;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InvalidateCanvasReference();
        if (hasStarted)
            SynchronizePresentation(true);
    }

    void HandleSceneUnloaded(Scene scene)
    {
        if (osCanvas == null || osCanvas.gameObject.scene == scene)
            InvalidateCanvasReference();

        if (hasStarted)
            SynchronizePresentation(true);
    }

    void SynchronizePresentation(bool force)
    {
        if (!GameSetup.IsTestMode)
        {
            ReleaseTestPresentation();
            return;
        }

        bool referencesChanged = ResolveReferences();
        bool isVisible = IsCanvasVisible();
        osHandle?.SetAdditionBackgroundSuppressed(isVisible);
        Camera targetCamera = cameraHandle != null
            ? cameraHandle.Camera
            : null;
        bool needsCanvasBinding = osCanvas != null
                                  && targetCamera != null
                                  && (osCanvas.renderMode
                                          != RenderMode.ScreenSpaceCamera
                                      || osCanvas.worldCamera
                                          != targetCamera);

        if (!force
            && !referencesChanged
            && !needsCanvasBinding
            && hasLastVisibility
            && lastVisibility == isVisible)
        {
            return;
        }

        if (osCanvas != null && targetCamera != null)
        {
            osCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            osCanvas.worldCamera = targetCamera;
            hasWarnedMissingCamera = false;
        }
        else if (isVisible && !hasWarnedMissingCamera)
        {
            hasWarnedMissingCamera = true;
            Debug.LogWarning(
                "[GameTest] Main_OS_Canvs is visible, but OSCameraHandle "
                + "or its Camera is missing.",
                this);
        }

        bool shouldUseOSCamera = isVisible && targetCamera != null;
        if (shouldUseOSCamera)
            AcquireTestPresentation();
        else
            ReleaseTestPresentation();

        lastVisibility = isVisible;
        hasLastVisibility = true;
    }

    bool ResolveReferences()
    {
        Canvas previousCanvas = osCanvas;
        OS_Handle previousOSHandle = osHandle;
        OSCameraHandle previousCameraHandle = cameraHandle;
        CursorHandle previousCursorHandle = cursorHandle;

        if (osHandle == null)
        {
            osHandle = OS_Handle.Instance;
            if (osHandle == null)
            {
                osHandle = Object.FindAnyObjectByType<OS_Handle>(
                    FindObjectsInactive.Include);
            }
        }

        if (osCanvas == null)
            osCanvas = FindOSCanvas();

        if (osCanvas != previousCanvas || osCanvasGroup == null)
        {
            osCanvasGroup = osCanvas != null
                ? osCanvas.GetComponent<CanvasGroup>()
                : null;
        }

        OSCameraHandle nextCameraHandle = OSCameraHandle.Instance;
        if (nextCameraHandle != previousCameraHandle
            && ownsCameraPresentation)
        {
            if (previousCameraHandle != null)
                previousCameraHandle.SetPresentationActive(this, false);
            ownsCameraPresentation = false;
        }

        if (nextCameraHandle != previousCameraHandle
            && ownsCursorPresentation)
        {
            if (cursorHandle != null)
                cursorHandle.SetCameraPresentation(this, null, false);
            ownsCursorPresentation = false;
        }

        cameraHandle = nextCameraHandle;
        CursorHandle nextCursorHandle = CursorHandle.Instance;
        if (nextCursorHandle == null)
        {
            nextCursorHandle = Object.FindAnyObjectByType<CursorHandle>(
                FindObjectsInactive.Include);
        }
        if (nextCursorHandle != previousCursorHandle
            && ownsCursorPresentation)
        {
            if (previousCursorHandle != null)
            {
                previousCursorHandle.SetCameraPresentation(
                    this,
                    null,
                    false);
            }
            ownsCursorPresentation = false;
        }

        cursorHandle = nextCursorHandle;
        return osCanvas != previousCanvas
               || osHandle != previousOSHandle
               || cameraHandle != previousCameraHandle
               || cursorHandle != previousCursorHandle;
    }

    Canvas FindOSCanvas()
    {
        OS_Handle resolvedHandle = osHandle != null
            ? osHandle
            : OS_Handle.Instance;
        if (resolvedHandle == null)
        {
            resolvedHandle = Object.FindAnyObjectByType<OS_Handle>(
                FindObjectsInactive.Include);
        }

        Canvas resolvedCanvas = resolvedHandle != null
            ? resolvedHandle.GetComponent<Canvas>()
            : null;
        if (resolvedCanvas != null)
            return resolvedCanvas;

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas candidate = canvases[i];
            if (candidate != null && candidate.name == mainOSCanvasName)
                return candidate;
        }

        return null;
    }

    bool IsCanvasVisible()
    {
        return osCanvas != null
               && osCanvas.isActiveAndEnabled
               && (osCanvasGroup == null || osCanvasGroup.alpha > 0f);
    }

    void AcquireTestPresentation()
    {
        if (!ownsScenePresentation)
            ownsScenePresentation = ConfigureScenePresentation(true);

        if (!ownsCursorPresentation
            && cursorHandle != null
            && cameraHandle != null
            && cameraHandle.Camera != null)
        {
            cursorHandle.SetCameraPresentation(
                this,
                cameraHandle.Camera,
                true);
            ownsCursorPresentation = true;
        }

        if (!ownsCameraPresentation && cameraHandle != null)
        {
            cameraHandle.SetPresentationActive(this, true);
            ownsCameraPresentation = true;
        }
    }

    void ReleaseTestPresentation()
    {
        osHandle?.SetAdditionBackgroundSuppressed(false);
        if (ownsCursorPresentation && cursorHandle != null)
            cursorHandle.SetCameraPresentation(this, null, false);
        ownsCursorPresentation = false;

        if (ownsCameraPresentation && cameraHandle != null)
            cameraHandle.SetPresentationActive(this, false);
        ownsCameraPresentation = false;

        if (ownsScenePresentation)
            ConfigureScenePresentation(false);
        ownsScenePresentation = false;
    }

    bool ConfigureScenePresentation(bool showOS)
    {
        SceneLoadManager loader = SceneLoadManager.Instance;
        if (loader == null)
            return false;

        if (showOS)
        {
            bool setupHandled = loader.SetScenePresentationEnabled(
                setupScene,
                false);
            bool sideScrollHandled = loader.SetScenePresentationEnabled(
                sideScrollScene,
                false);
            bool osHandled = loader.SetScenePresentationEnabled(
                osScene,
                true);
            return setupHandled || sideScrollHandled || osHandled;
        }

        loader.SetScenePresentationEnabled(sideScrollScene, true);
        bool sideScrollHasCamera = loader.HasEnabledCamera(
            sideScrollScene);
        loader.SetScenePresentationEnabled(
            setupScene,
            !sideScrollHasCamera);
        loader.SetScenePresentationEnabled(osScene, false);
        return true;
    }

    void InvalidateCanvasReference()
    {
        osCanvas = null;
        osCanvasGroup = null;
        hasLastVisibility = false;
    }
}
