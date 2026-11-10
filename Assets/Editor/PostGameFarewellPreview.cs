using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Open the real farewell UI without completing the game or consuming its one-time receipt.</summary>
public static class PostGameFarewellPreview
{
    const string MenuPath = "Tools/MrTGame/Post Game Farewell/Open Window (Play Mode)";

    [MenuItem(MenuPath, false, 0)]
    public static void Open()
    {
        if (!CanOpen())
        {
            Debug.LogWarning("[Farewell Preview] 请先进入 Play Mode，等待游戏 UI 初始化完成。");
            return;
        }
        if (PostGameFarewellWindow.Current != null) return;
        if (PostGameFarewellQA.Status == "running")
        {
            Debug.LogWarning("[Farewell Preview] 请等窗口自动检查结束后再打开手动预览。");
            return;
        }

        var menu = MainMenuHandle.Instance;
        var sourceCanvas = menu.GetComponent<Canvas>();
        var sourceScaler = menu.GetComponent<CanvasScaler>();
        // The main-menu Canvas is disabled during gameplay. Use a separate
        // runtime Canvas so this Tools entry also works from an active chapter.
        var root = new GameObject("PostGameFarewellPreview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.layer = menu.gameObject.layer;
        var canvas = root.GetComponent<Canvas>();
        var camera = sourceCanvas != null ? sourceCanvas.worldCamera : null;
        bool cameraAvailable = camera != null && camera.isActiveAndEnabled;
        canvas.renderMode = cameraAvailable ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = cameraAvailable ? camera : null;
        if (cameraAvailable) canvas.planeDistance = sourceCanvas.planeDistance;
        canvas.sortingOrder = PostGameFarewellWindow.SortingOrder;
        canvas.pixelPerfect = true;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = sourceScaler != null ? sourceScaler.referenceResolution : new Vector2(640f, 360f);
        scaler.screenMatchMode = sourceScaler != null ? sourceScaler.screenMatchMode : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = sourceScaler != null ? sourceScaler.matchWidthOrHeight : .5f;
        scaler.referencePixelsPerUnit = sourceScaler != null ? sourceScaler.referencePixelsPerUnit : 20f;

        var window = PostGameFarewellWindow.Show(root.transform, () => Object.Destroy(root), menu.transform);
        if (window == null)
        {
            Object.Destroy(root);
            Debug.LogWarning("[Farewell Preview] 窗口未能打开，请查看 Console。");
        }
    }

    [MenuItem(MenuPath, true)]
    static bool CanOpen() => Application.isPlaying
        && GameDataManager.Instance != null && GameDataManager.Instance.IsInitialized
        && MainMenuHandle.Instance != null && EventSystem.current != null
        && PostGameFarewellContent.IsReady();
}
