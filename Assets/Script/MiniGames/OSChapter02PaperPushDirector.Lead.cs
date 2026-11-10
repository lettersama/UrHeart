using UnityEngine;

// During the last line her red pointer leaves the bench, flies to 状态检查 and
// waits there with a hop now and then. In the dark her own light shows the icon.
public sealed partial class OSChapter02PaperPushDirector
{
    const float LeadFlightSeconds = 1.4f, LeadHopSeconds = 2.4f;
    bool choreoLeading;
    float choreoLeadTime;

    bool TickStateCheckLead(float dt)
    {
        var talk = os != null ? os.Chapter02FinalTalk : null;
        if (talk == null || !talk.CanLeadToStateCheck) { choreoLeading = false; return false; }
        var icon = StateCheckIcon();
        if (icon == null) return false;
        actor.ScriptActorSetVisible(this, true);
        actor.ScriptActorSetPressed(this, false); actor.ScriptActorSetTilt(this, 0f);
        if (!choreoLeading)
        {
            // The tip rests on the icon's lower right, so the icon sits in her light.
            Vector2 perch = ChoreoScreenPoint(icon, new Vector2(icon.rect.xMax - 2f, icon.rect.center.y - 2f));
            choreoLeading = true; choreoLeadTime = 0f;
            actor.ScriptActorMoveTo(this, perch, LeadFlightSeconds);
            return true;
        }
        if (actor.ScriptActorIsMoving) return true;
        var night = talk.Nightfall;
        if (night != null && (night.IsThundering || night.HasEnded)) return true;
        choreoLeadTime += Mathf.Clamp(dt, 0f, .05f);
        if (choreoLeadTime >= LeadHopSeconds) { choreoLeadTime = 0f; actor.ScriptActorHop(this); }
        return true;
    }

    RectTransform StateCheckIcon()
    {
        if (os == null) return null;
        foreach (var unit in os.DesktopProcessUnits)
            if (unit != null && unit.ProcessKey == OSDesktopProcessKeys.StateCheck
                && unit.isActiveAndEnabled && unit.ProcessIcon != null)
                return unit.ProcessIcon.rectTransform;
        return null;
    }
}
