using UnityEngine;
using System.Collections.Generic;

public sealed partial class OS_Handle
{
    [SerializeField] OSPaperPushStoryDirector paperPushStory;
    bool paperPushOpenedFolder;
    bool paperPushRestoredFolder;

    public bool RevealPaperPushForSeal()
    {
        var grid = GetDesktopProcessGridContaining(paperPushDesktopProcess);
        if (grid == null || grid.ContainerId != OSDesktopProcessContainerIds.Folder) return false;
        paperPushOpenedFolder = folderWindow != null && !folderWindow.IsOpen;
        paperPushRestoredFolder = folderWindow != null && folderWindow.IsMinimized;
        OpenFolderWindow();return true;
    }

    public void RestorePaperPushSealFolder()
    {
        if (folderWindow != null)
        {
            if (paperPushOpenedFolder) folderWindow.Close();
            else if (paperPushRestoredFolder) folderWindow.Minimize();
        }
        paperPushOpenedFolder = paperPushRestoredFolder = false;
    }

    // Use the normal desktop grid and layout save path, including icons moved into folders.
    public bool PlacePaperPushForSeal(out Vector2 screenPosition)
    {
        screenPosition = Vector2.zero;
        var unit = paperPushDesktopProcess;
        var source = GetDesktopProcessGridContaining(unit);
        var target = GetDesktopProcessGrid(OSDesktopProcessContainerIds.Desktop);
        if (unit == null || source == null || target == null || !source.TryGetSlot(unit, out int sourceIndex)) return false;
        var rect = target.RectTransform;
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 desired = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(
            new Vector2(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, .35f), Mathf.Lerp(rect.rect.yMin, rect.rect.yMax, .65f))));
        // The charger is usually still open here. Pick a nearby visible desktop slot
        // so the stamp does not land invisibly behind that window.
        var covers = new List<Rect>();var corners = new Vector3[4];
        Vector2 margin = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector2(31, 23)))
            - RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(Vector2.zero));
        margin = new Vector2(Mathf.Abs(margin.x), Mathf.Abs(margin.y));
        foreach (var visible in transform.Find("Windows").GetComponentsInChildren<OSWindowUnit>(false))
        {
            if (!visible.IsOpen || visible.IsMinimized || !visible.gameObject.activeInHierarchy) continue;
            visible.WindowRect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = min;
            for (int i = 1; i < 4; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, p);max = Vector2.Max(max, p);
            }
            covers.Add(Rect.MinMaxRect(min.x - margin.x, min.y - margin.y, max.x + margin.x, max.y + margin.y));
        }
        // A wide sticker also needs breathing room from adjacent desktop icons.
        foreach (var other in DesktopProcessUnits)
        {
            if (other == null || other == unit || !other.gameObject.activeInHierarchy || !target.Contains(other)) continue;
            var otherRect = other.transform as RectTransform;
            otherRect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            covers.Add(Rect.MinMaxRect(min.x - margin.x, min.y - margin.y, max.x + margin.x, max.y + margin.y));
        }
        int slot;
        if (!target.TryFindNearestAvailableSlot(desired, camera, unit, out slot, covers)
            && !target.TryFindNearestAvailableSlot(desired, camera, unit, out slot)) return false;
        if (!MoveDesktopProcessToGrid(unit, source, sourceIndex, target, slot)) return false;
        var icon = unit.ProcessIcon.rectTransform;
        screenPosition = RectTransformUtility.WorldToScreenPoint(camera, icon.TransformPoint(icon.rect.center));
        return true;
    }
}
