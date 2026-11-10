using UnityEngine;
using UnityEngine.EventSystems;

// The OS owns the shared art reference; each receiving window reuses its own small effect pool.
[DisallowMultipleComponent]
public sealed class OSUIEnergySpendPresenter : MonoBehaviour
{
    [SerializeField] Sprite batteryFrame;
    [SerializeField] RectTransform energySource;
    [SerializeField] RectTransform windowsRoot;

    public static bool Show(Component origin, RectTransform anchor = null, bool outsideRight = false,
        TMPro.TMP_Text textAnchor = null)
    {
        if (origin == null)
            return false;
        var presenter = origin.GetComponentInParent<OSUIEnergySpendPresenter>();
        return presenter != null && presenter.Play(origin, anchor, outsideRight, textAnchor);
    }

    bool Play(Component origin, RectTransform anchor, bool outsideRight, TMPro.TMP_Text textAnchor)
    {
        if (batteryFrame == null || !isActiveAndEnabled)
            return false;
        OSWindowUnit owner = origin.GetComponentInParent<OSWindowUnit>();
        if (owner == null)
            owner = OS_Handle.Instance != null ? OS_Handle.Instance.MouseActiveWindow : null;
        if (owner == null || !owner.IsOpen || owner.IsMinimized || owner.IsClosing)
            return false;
        if (anchor == null && EventSystem.current != null)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.GetComponentInParent<OSWindowUnit>() == owner)
                anchor = selected.transform as RectTransform;
        }
        if (anchor == null)
            anchor = owner.WindowRect;
        var effect = owner.GetComponent<OSUIEnergySpendEffect>();
        if (effect == null)
            effect = owner.gameObject.AddComponent<OSUIEnergySpendEffect>();
        effect.Configure(batteryFrame, energySource, windowsRoot);
        return effect.Play(anchor, outsideRight, textAnchor);
    }
}
