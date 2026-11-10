using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSToolLineUnit : MonoBehaviour
{
    [Header("References")]
    [SerializeField] TMP_Text label;
    [SerializeField] Image image;
    [SerializeField] LayoutElement imageLayoutElement;

    [Header("Image Output")]
    [SerializeField, Min(1f)] float defaultImageHeight = 80f;

    public TMP_Text Label => label;
    public Image Image => image;
    public RectTransform RectTransform => transform as RectTransform;
    public bool IsInUse => gameObject.activeSelf;

    void Awake()
    {
        ResolveReferences();
    }

    public void BindText(string text)
    {
        OSToolCommandLink.Detach(this);
        ResolveReferences();
        if (label != null)
        {
            label.gameObject.SetActive(true);
            label.text = text ?? string.Empty;
        }

        if (image != null)
        {
            image.sprite = null;
            image.gameObject.SetActive(false);
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    public void BindImage(
        Sprite sprite,
        string caption = null,
        float preferredHeight = -1f)
    {
        OSToolCommandLink.Detach(this);
        ResolveReferences();
        if (label != null)
        {
            bool hasCaption = !string.IsNullOrEmpty(caption);
            label.gameObject.SetActive(hasCaption);
            label.text = hasCaption ? caption : string.Empty;
        }

        if (image != null)
        {
            image.sprite = sprite;
            image.preserveAspect = true;
            image.gameObject.SetActive(sprite != null);
        }

        if (imageLayoutElement != null)
        {
            imageLayoutElement.preferredHeight = preferredHeight > 0f
                ? preferredHeight
                : defaultImageHeight;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    public void Clear()
    {
        OSToolCommandLink.Detach(this);
        ResolveReferences();
        if (gameObject.activeSelf)
            gameObject.SetActive(false);

        if (label != null)
        {
            label.text = string.Empty;
            label.gameObject.SetActive(true);
        }

        if (image != null)
        {
            image.sprite = null;
            image.gameObject.SetActive(false);
        }
    }

    void ResolveReferences()
    {
        if (label == null)
            label = transform.Find("Text")?.GetComponent<TMP_Text>();

        if (image == null)
            image = transform.Find("Image")?.GetComponent<Image>();

        if (imageLayoutElement == null && image != null)
            imageLayoutElement = image.GetComponent<LayoutElement>();
    }

    void Reset()
    {
        ResolveReferences();
    }
}
