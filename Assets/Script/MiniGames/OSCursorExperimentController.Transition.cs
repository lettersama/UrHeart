using UnityEngine;

public sealed partial class OSCursorExperimentController
{
    const float FirstTestTransitionSeconds = .5f;
    const float FirstTestDoorSeconds = .14f;
    const float ThirdTestPointerFaultSeconds = .32f;
    readonly UnityEngine.UI.Image[] pointerFaultGhosts = new UnityEngine.UI.Image[2];
    bool firstTestTransitionActive, firstTestReleasePending;
    float firstTestTransitionTime;
    Vector2 firstTestPointerFrom;
    CanvasGroup firstTestCageFade, firstTestMazeFade;
    float firstTestCageOriginalAlpha = 1f, firstTestMazeOriginalAlpha = 1f;

    public bool IsFirstTestTransitionActive => firstTestTransitionActive;
    public float FirstTestTransitionProgress => Mathf.Clamp01(firstTestTransitionTime / FirstTestTransitionSeconds);
    public bool IsThirdTestPointerFaultActive => firstTestTransitionActive && round == 3 && firstTestTransitionTime < 0f;

    void StartFirstTestTransition()
    {
        // The caller changes phase immediately, so its repeated per-frame calls
        // cannot restart this short transition or reset its captured origin.
        firstTestPointerFrom = cagePointer;
        if (round == 1) GetCageStoryWeightCenter();
        // The third rule is applied visibly to the captive pointer before the
        // gate opens. Loading its rules checkpoint restores the finished change.
        firstTestTransitionTime = round == 3 ? -ThirdTestPointerFaultSeconds : 0f;
        if (round == 3) Chapter02Audio.Play("Chapter02_PointerGlitch", .72f);
        else Chapter02Audio.Play("Chapter02_CageDoor", .6f, 1.12f);
        firstTestReleasePending = false;
        firstTestTransitionActive = true;
        InitializeFirstTestFades();
        pointer = layout.Start;
        ResetTrail();
        SetPhase(ExperimentPhase.RoundIntro);
        RenderPresentation();
    }

    void InitializeFirstTestFades()
    {
        if (firstTestCageFade == null && intro != null)
        {
            firstTestCageFade = intro.GetComponent<CanvasGroup>();
            if (firstTestCageFade == null) firstTestCageFade = intro.gameObject.AddComponent<CanvasGroup>();
            firstTestCageOriginalAlpha = firstTestCageFade.alpha;
        }
        var stage = stages[Mathf.Clamp(round - 1, 0, 2)];
        if (stage != null && (firstTestMazeFade == null || firstTestMazeFade.transform != stage))
        {
            if (firstTestMazeFade != null) firstTestMazeFade.alpha = firstTestMazeOriginalAlpha;
            firstTestMazeFade = stage.GetComponent<CanvasGroup>();
            if (firstTestMazeFade == null) firstTestMazeFade = stage.gameObject.AddComponent<CanvasGroup>();
            firstTestMazeOriginalAlpha = firstTestMazeFade.alpha;
        }
    }

    void TickFirstTestTransition(float dt)
    {
        float previousTime = firstTestTransitionTime;
        firstTestTransitionTime = Mathf.Min(FirstTestTransitionSeconds, firstTestTransitionTime + dt);
        if (round == 3 && previousTime < 0f && firstTestTransitionTime >= 0f)
            Chapter02Audio.Play("Chapter02_CageDoor", .6f, 1.12f);
        if (firstTestTransitionTime + .00001f < FirstTestTransitionSeconds) return;
        bool release = firstTestReleasePending;
        firstTestTransitionActive = false;
        firstTestReleasePending = false;
        RestoreFirstTestFades();
        HidePointerFaultGhosts();
        // Force the ordinary renderer to restore stage visibility and parenting,
        // even if the temporary overlap reused an already-selected stage.
        currentShot = -2;
        inputWarmup = true;
        if (release) SetPhase(ExperimentPhase.Playing);
        else phaseTime = 0f;
    }

    void RenderFirstTestTransition()
    {
        SetShot(3);
        if (intro != null) intro.gameObject.SetActive(true);
        var stage = stages[Mathf.Clamp(round - 1, 0, 2)];
        if (stage != null) stage.gameObject.SetActive(!IsThirdTestPointerFaultActive);
        if (cageGestureHint != null) cageGestureHint.gameObject.SetActive(false);
        transferRoot.gameObject.SetActive(false);
        contactFlash.gameObject.SetActive(false);
        dynamicRoot.gameObject.SetActive(true);
        if (IsThirdTestPointerFaultActive)
        {
            SetCageDoor(0f);
            HideWeight();
            RenderGravityPresentation();
            RenderThirdTestPointerFault();
            return;
        }
        HidePointerFaultGhosts();

        float time = firstTestTransitionTime;
        SetCageDoor(Ease(time / FirstTestDoorSeconds));
        if (firstTestCageFade != null)
            firstTestCageFade.alpha = firstTestCageOriginalAlpha * (1f - Ease((time - .17f) / .24f));
        if (firstTestMazeFade != null)
            firstTestMazeFade.alpha = firstTestMazeOriginalAlpha * Ease((time - .08f) / .40f);

        // Keep the normal cage-front occlusion while the gate opens. Once it is
        // swung fully to the left, carry pointer and ball above both fading backdrops.
        if (time >= FirstTestDoorSeconds && dynamicRoot.parent != rootRect)
        {
            dynamicRoot.SetParent(rootRect, false);
            Place(dynamicRoot, Vector2.zero);
            dynamicRoot.SetAsLastSibling();
        }
        visiblePointer = FirstTestTransferPointerPosition();
        SetPointerSprite(round == 3 ? hackedPointer : normalPointer);
        PlacePointer(visiblePointer);
        // Move the pin, then let the same constrained weight follow. No scripted
        // radius change or fresh seed when control moves into the first maze.
        if (round == 1) RenderShortStoryTether(visiblePointer, TetherBallForPresentation(visiblePointer));
        else HideWeight();
        RenderGravityPresentation();
        PresentVirtualPointer();
    }

    void RenderThirdTestPointerFault()
    {
        float elapsed = firstTestTransitionTime + ThirdTestPointerFaultSeconds;
        int beat = Mathf.FloorToInt(elapsed / .04f);
        bool inverted = (beat & 1) != 0;
        visiblePointer = firstTestPointerFrom;
        Vector2 nudge = new Vector2((beat % 3 - 1) * 2, (beat & 1) == 0 ? 0 : 1);
        SetPointerSprite(inverted ? hackedPointer : normalPointer);
        PlacePointer(visiblePointer + nudge);
        for (int i = 0; i < pointerFaultGhosts.Length; i++)
        {
            if (pointerFaultGhosts[i] == null)
                pointerFaultGhosts[i] = MakeImage("PointerRuleFault" + i, dynamicRoot, normalPointer);
            var ghost = pointerFaultGhosts[i];
            ghost.gameObject.SetActive(beat != 3 && (beat + i) % 3 != 0);
            ghost.color = new Color(Ink.r, Ink.g, Ink.b, i == 0 ? .45f : .25f);
            Place(ghost.rectTransform, visiblePointer + new Vector2(i == 0 ? -4 : 4, i == 0 ? 1 : -1));
        }
        // Only the picture splits. Input remains captured at its logical cage
        // point, and the hardware mouse never gains a second interaction target.
        PresentVirtualPointer();
    }

    void HidePointerFaultGhosts()
    {
        foreach (var ghost in pointerFaultGhosts)
            if (ghost != null) ghost.gameObject.SetActive(false);
    }

    Vector2 FirstTestTransferPointerPosition()
    {
        float travel = Ease((firstTestTransitionTime - FirstTestDoorSeconds) / (FirstTestTransitionSeconds - FirstTestDoorSeconds));
        return Vector2.Lerp(firstTestPointerFrom, MazePointerTopLeft(layout.Start), travel);
    }

    void RestoreFirstTestFades()
    {
        if (firstTestCageFade != null) firstTestCageFade.alpha = firstTestCageOriginalAlpha;
        if (firstTestMazeFade != null) firstTestMazeFade.alpha = firstTestMazeOriginalAlpha;
    }

    void ResetFirstTestTransition()
    {
        firstTestTransitionActive = firstTestReleasePending = false;
        firstTestTransitionTime = 0f;
        HidePointerFaultGhosts();
        RestoreFirstTestFades();
        currentShot = -2;
    }
}
