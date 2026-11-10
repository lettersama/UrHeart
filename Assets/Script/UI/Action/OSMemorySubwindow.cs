using MrTGame.Narrative;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSMemorySubwindow : MonoBehaviour
{
    [SerializeField] RectTransform contentHost;
    OSWindowUnit window;
    OSMemoryShotContent content;
    OSMemoryShotContent cachedPrefab;
    OSMemorySubwindowShot shot;

    public OSWindowUnit Window => window != null ? window : (window = GetComponent<OSWindowUnit>());
    public OSMemoryShotContent Content => content;
    public bool IsFullyOpen { get; private set; }
    public bool IsClosed => !Window.IsOpen;

    void Awake()
    {
        Window.OpenAnimationCompleted += HandleOpened;
        // The sprite-less root Image is only a hit surface. An opaque white underlay
        // would bleed through the illustrated background during a CanvasGroup fade.
        var hitSurface = GetComponent<UnityEngine.UI.Image>();
        if (hitSurface != null && hitSurface.sprite == null)
            hitSurface.color = Color.clear;
    }
    void OnDisable()
    {
        IsFullyOpen = false;
        ReleaseContent();
    }
    void OnDestroy()
    {
        if (window != null)
            window.OpenAnimationCompleted -= HandleOpened;
    }

    public void Prepare(OSMemorySubwindowShot source, Vector2 position, Vector2 size)
    {
        Close();
        shot = source;
        if (contentHost == null)
            throw new System.InvalidOperationException("Subwindow content host is missing.");
        // One cached content instance per slot: bounded memory, reused on replay.
        if (cachedPrefab != source.ContentPrefab || content == null)
        {
            if (content != null)
            {
                content.gameObject.SetActive(false);
                Destroy(content.gameObject);
            }
            content = Instantiate(source.ContentPrefab, contentHost, false);
            cachedPrefab = source.ContentPrefab;
        }
        Window.ConfigureInteraction(false, false);
        Window.SetOpeningLayout(position, size);
        content.Prepare(source); // Images, localised text and caption alpha are ready before Open.
        content.gameObject.SetActive(true);
    }

    public void Open()
    {
        IsFullyOpen = false;
        Window.OpenStandalone();
    }

    public void Close()
    {
        IsFullyOpen = false;
        ReleaseContent();
        Window.CloseSilently();
        Window.PrepareRuntimeClosed();
    }

    public void CloseAnimated() => CloseAnimated(0.32f);

    public void CloseAnimated(float duration)
    {
        IsFullyOpen = false;
        // Keep content alive until the window fade has actually finished.
        Window.CloseAnimated(duration, false);
    }

    void ReleaseContent()
    {
        if (content != null && content.gameObject.activeSelf)
        {
            content.Release();
            content.gameObject.SetActive(false);
        }
    }

    void HandleOpened(OSWindowUnit opened)
    {
        IsFullyOpen = true;
        if (!string.IsNullOrWhiteSpace(shot.OpenedSfxKey))
            AudioManager.Instance?.PlaySfx(shot.OpenedSfxKey);
        content.PlayOpened(shot);
    }
}
