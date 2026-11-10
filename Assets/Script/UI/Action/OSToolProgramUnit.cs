using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSToolProgramUnit : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] Image imageTip;
    [SerializeField] Image programIcon;
    [SerializeField] TMP_Text programName;
    [SerializeField] Image batteryIcon;
    [SerializeField] TMP_Text costText;
    [SerializeField] TMP_Text statusText;
    [SerializeField] Color normalTextColor = new Color(0.7f, 0.7f, 0.67f);
    [SerializeField] Color activeTextColor = Color.white;
    [SerializeField] Color hoverColor = new Color(0.3f, 0.3f, 0.3f, 0.7f);

    OSToolProgramLibrary owner;
    bool isHovered;

    public OSToolProgramDefinition Program { get; private set; }

    public void Bind(OSToolProgramLibrary library, OSToolProgramDefinition program, Sprite energyIcon)
    {
        owner = library;
        Program = program;
        programIcon.sprite = program.Icon;
        batteryIcon.sprite = energyIcon;
        Refresh();
    }

    public void Refresh()
    {
        if (Program == null)
            return;

        OSLocalizedFontUtility.ApplyCurrent(programName);
        OSLocalizedFontUtility.ApplyCurrent(costText);
        OSLocalizedFontUtility.ApplyCurrent(statusText);
        programName.text = Program.DisplayName;
        costText.text = OSToolProgramLibrary.Localize("ui.os.tool.program.cost", "{0} energy")
            .Replace("{0}", Program.BatteryCost.ToString());
        bool installed = GameDataManager.Instance != null
            && GameDataManager.Instance.IsToolProgramInstalled(Program.Id);
        statusText.gameObject.SetActive(installed);
        statusText.text = OSToolProgramLibrary.Localize("ui.os.tool.program.installed", "Installed");
        RefreshFeedback();
    }

    void OnDisable()
    {
        isHovered = false;
        RefreshFeedback();
    }

    void LateUpdate() => RefreshFeedback();

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        if (owner != null && owner.CanInteract)
            owner.RequestInstallationHint();
        RefreshFeedback();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        RefreshFeedback();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging
            || owner == null || !owner.CanInteract || Program == null)
            return;

        owner.SelectProgram(Program);
    }

    void RefreshFeedback()
    {
        bool active = isHovered && owner != null && owner.CanInteract;
        if (imageTip != null)
            imageTip.color = active ? hoverColor : Color.clear;
        if (programName != null)
            programName.color = active ? activeTextColor : normalTextColor;
    }
}
