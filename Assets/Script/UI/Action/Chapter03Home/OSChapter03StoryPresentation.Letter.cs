using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSChapter03StoryPresentation
{
    // pancake 10-26: the screen dims a little and the envelope art slides up from below on a tilt.
    // It sits above the night mask but under both pointers; the player and Live's red pointer press it
    // together, the letter rises out, the envelope drops away, and an X in the screen corner closes it.
    const int LetterShadeLevel = 7, EnvelopeOrder = 1201; // between the night mask (1200) and the red pointer (1202)
    const float EnvelopeEnterSeconds = .75f, EnvelopeDrift = 56f, EnvelopeTilt = -12f;
    const float LetterPressSeconds = .18f, LetterOpenSeconds = 1.05f, LetterRise = 34f;
    const float EnvelopeDropDrift = 22f, EnvelopeDropTurn = 16f;
    static readonly Vector2 EnvelopePartnerOffset = new Vector2(20f, -12f); // the right edge of the wax seal
    const float FoldedWidth = .24f;

    RectTransform letterLayer, letterEnvelope, letterPartnerPoint, letterCollectionTarget;
    UnityEngine.UI.Button letterScreenClose;
    CanvasGroup letterSheetGroup;
    bool letterEntering, letterOpening, letterCollecting, letterUnfolded, letterPlayerClicked;
    float letterEntranceClock, letterOpenClock, letterPartnerReady, letterCollectionClock, letterCollectionSeconds;
    Vector2 letterCollectionStart;
    Vector3 letterCollectionScale;
    Action letterUnfoldedCallback, letterCollected;

    /// <summary>The envelope has settled and waits for both presses.</summary>
    public bool IsLetterEnvelopeReady => modalKind == ModalKind.Letter && letterEnvelope != null
        && !letterEntering && !letterOpening && !letterUnfolded && !letterCollecting;
    /// <summary>True for the moment both pointers hold the envelope down.</summary>
    public bool IsLetterEnvelopePressed => modalKind == ModalKind.Letter && letterOpening && letterOpenClock < LetterPressSeconds;

    public void BeginLetter(string title, string body, Action unfolded, Action closed = null)
    {
        OpenModal(ModalKind.Letter, title, new Vector2(PaperWidth("letter"), PaperMaxHeight), closed);
        modalShade.SetLevel(LetterShadeLevel);
        BuildPaperBody(title, body, "letter");
        letterUnfoldedCallback = unfolded;
        letterEntering = true; letterEntranceClock = 0f;
        letterOpening = letterUnfolded = letterPlayerClicked = false;
        letterPartnerReady = letterOpenClock = 0f;
        // The sheet waits inside the envelope; a zero-alpha group also keeps the night from lighting it.
        letterSheetGroup = modalFrame.gameObject.AddComponent<CanvasGroup>();
        SetLetterSheetShown(false);
        closeButton.gameObject.SetActive(false);
        modalFrame.anchoredPosition = PixelCentre(Vector2.zero, modalFrame.rect.size);

        letterLayer = Rect("EnvelopeLayer", modalRoot, Vector2.zero, Vector2.zero);
        Stretch(letterLayer);
        var canvas = letterLayer.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = EnvelopeOrder;
        letterLayer.gameObject.AddComponent<GraphicRaycaster>();
        var sprite = RequireArtSprite("envelope");
        var art = PaperImage(letterLayer, "Envelope", sprite, Vector2.zero, sprite.rect.size);
        art.raycastTarget = true;
        art.gameObject.AddComponent<OSChapter03PresentationHit>().Clicked = e => PressEnvelope();
        letterEnvelope = art.rectTransform;
        letterPartnerPoint = Rect("LivePress", letterEnvelope, EnvelopePartnerOffset, Vector2.one);

        var half = windowParent.rect.size * .5f;
        letterScreenClose = PixelButton(letterLayer, "LetterClose", "", new Vector2(half.x - 16f, half.y - 14f),
            new Vector2(20f, 18f), CloseLetterFromInput);
        DrawClose(PixelContent(letterScreenClose));
        letterScreenClose.gameObject.SetActive(false);
        PlaceEnvelope(0f);
    }

    void SetLetterSheetShown(bool shown)
    {
        if (letterSheetGroup == null) return;
        letterSheetGroup.alpha = shown ? 1f : 0f;
        letterSheetGroup.interactable = letterSheetGroup.blocksRaycasts = shown;
    }

    // Enters from straight below: the bottom of the screen, a little to the right, tilted.
    void PlaceEnvelope(float t)
    {
        if (letterEnvelope == null) return;
        float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
        float startY = -(windowParent.rect.height + letterEnvelope.rect.height) * .5f - 16f;
        var position = Vector2.Lerp(new Vector2(EnvelopeDrift, startY), Vector2.zero, e);
        letterEnvelope.anchoredPosition = PixelCentre(position, letterEnvelope.rect.size);
        letterEnvelope.localRotation = Quaternion.Euler(0f, 0f, t >= 1f ? 0f : Mathf.Lerp(EnvelopeTilt, 0f, e));
    }

    void PressEnvelope()
    {
        if (!CanInteract || !IsLetterEnvelopeReady) return;
        letterPlayerClicked = true;
        TryOpenEnvelope();
    }

    /// <summary>0..1: how far Live's red pointer has come onto the seal. At 1 it is ready to press.</summary>
    public void SetLetterPartnerPull(float normalized)
    {
        if (modalKind != ModalKind.Letter || letterUnfolded || letterOpening || letterCollecting || paused) return;
        letterPartnerReady = Mathf.Clamp01(normalized);
        TryOpenEnvelope();
    }

    void TryOpenEnvelope()
    {
        if (!IsLetterEnvelopeReady || !letterPlayerClicked || letterPartnerReady < .98f) return;
        letterOpening = true; letterOpenClock = 0f;
        letterEnvelope.anchoredPosition = PixelCentre(Vector2.zero, letterEnvelope.rect.size) + Vector2.down;
        RefreshLetterPressVisuals();
        os?.Chapter03Story?.PlayLetterOpenSfx();
    }

    void RefreshLetterPressVisuals()
    {
        bool pressed = !paused && IsLetterEnvelopePressed;
        if (modalKind == ModalKind.Letter && letterOpening)
            os?.Chapter03Story?.SetLetterPressVisual(pressed);
        var cursor = CursorHandle.Instance;
        if (cursor == null) return;
        if (pressed) cursor.SetContextState(this, CursorVisualState.Pressed);
        else cursor.ClearContextState(this);
    }

    void TickOpening(float dt)
    {
        letterOpenClock += dt;
        RefreshLetterPressVisuals();
        var rest = PixelCentre(Vector2.zero, letterEnvelope.rect.size);
        if (letterOpenClock < LetterPressSeconds) { letterEnvelope.anchoredPosition = rest + Vector2.down; return; }
        float t = Mathf.Clamp01((letterOpenClock - LetterPressSeconds) / (LetterOpenSeconds - LetterPressSeconds));
        // The letter rises out from behind while the envelope falls away under its own weight.
        SetLetterSheetShown(true);
        if (letterScreenClose != null) letterScreenClose.gameObject.SetActive(true);
        letterSheetGroup.interactable = letterSheetGroup.blocksRaycasts = false;
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .7f));
        modalFrame.anchoredPosition = PixelCentre(new Vector2(0f, -LetterRise * (1f - rise)), modalFrame.rect.size);
        float fall = t * t;
        float drop = (windowParent.rect.height + letterEnvelope.rect.height) * .5f + 24f;
        letterEnvelope.anchoredPosition = PixelCentre(new Vector2(EnvelopeDropDrift * fall, -drop * fall), letterEnvelope.rect.size);
        letterEnvelope.localRotation = Quaternion.Euler(0f, 0f, -EnvelopeDropTurn * fall);
        if (t >= 1f) FinishOpening();
    }

    void FinishOpening()
    {
        if (letterUnfolded) return;
        letterEntering = letterOpening = false; letterUnfolded = true;
        if (letterEnvelope != null) letterEnvelope.gameObject.SetActive(false);
        modalFrame.anchoredPosition = PixelCentre(Vector2.zero, modalFrame.rect.size);
        SetLetterSheetShown(true);
        paperBodyGroup.alpha = 1f;
        paperBodyGroup.interactable = paperBodyGroup.blocksRaycasts = true;
        if (letterScreenClose != null) letterScreenClose.gameObject.SetActive(true);
        RebuildPagination();
        var callback = letterUnfoldedCallback; letterUnfoldedCallback = null;
        callback?.Invoke();
    }

    // The corner X dismisses the letter immediately from any page.
    // Reading forward remains on the paper and the page controls.
    void CloseLetterFromInput()
    {
        if (!CanInteract || modalKind != ModalKind.Letter || !letterUnfolded) return;
        HideModal(true);
    }

    public void AdvanceLetterPage()
    {
        if (modalKind == ModalKind.Letter && letterUnfolded && !letterCollecting && !paused) TurnPage(1);
    }

    public void RestoreUnfoldedLetter(int page)
    {
        if (modalKind != ModalKind.Letter) return;
        FinishOpening();
        for (int i = 0; i < page; i++) AdvanceLetterPage();
    }

    /// <summary>Visible text lines on the current page, in reading order; indices are zero based.</summary>
    public bool TryGetLetterLineTarget(int lineOnPage, out Vector2 screenPoint)
    {
        screenPoint = Vector2.zero;
        var indices = LetterLineIndices();
        if (lineOnPage < 0 || lineOnPage >= indices.Count) return false;
        var line = paperBody.textInfo.lineInfo[indices[lineOnPage]];
        Vector2 local = new Vector2(line.lineExtents.min.x - 5f, (line.ascender + line.descender) * .5f);
        screenPoint = RectTransformUtility.WorldToScreenPoint(EventCamera, paperBody.rectTransform.TransformPoint(local));
        return true;
    }

    List<int> LetterLineIndices()
    {
        var indices = new List<int>();
        if (modalKind != ModalKind.Letter || !letterUnfolded || paperBody == null || letterCollecting) return indices;
        paperBody.ForceMeshUpdate(true);
        var info = paperBody.textInfo;
        int previous = -1;
        for (int i = 0; i < info.characterCount; i++)
        {
            var character = info.characterInfo[i];
            if (character.pageNumber != paperPage || !character.isVisible || character.lineNumber == previous) continue;
            previous = character.lineNumber; indices.Add(previous);
        }
        return indices;
    }

    public void CollectLetterInto(RectTransform liveTarget, float seconds, Action done)
    {
        if (modalKind != ModalKind.Letter || modalFrame == null) { done?.Invoke(); return; }
        if (!letterUnfolded) FinishOpening();
        letterEntering = letterOpening = false; letterCollecting = true;
        letterCollectionClock = 0f; letterCollectionSeconds = Mathf.Max(.3f, seconds);
        letterCollectionStart = modalFrame.anchoredPosition; letterCollectionScale = modalFrame.localScale;
        letterCollectionTarget = liveTarget; letterCollected = done;
        letterUnfoldedCallback = null; modalClosed = null;
        if (letterScreenClose != null) letterScreenClose.gameObject.SetActive(false);
        closeButton.gameObject.SetActive(false);
        paperBodyGroup.interactable = paperBodyGroup.blocksRaycasts = false;
    }

    void TickLetter(float dt)
    {
        if (dt <= 0f || modalKind != ModalKind.Letter || modalFrame == null) return;
        if (letterEntering)
        {
            letterEntranceClock += dt;
            float t = Mathf.Clamp01(letterEntranceClock / EnvelopeEnterSeconds);
            PlaceEnvelope(t);
            if (t >= 1f) { letterEntering = false; TryOpenEnvelope(); }
        }
        if (letterOpening && letterEnvelope != null) TickOpening(dt);
        if (!letterCollecting) return;
        letterCollectionClock += dt;
        float p = Mathf.Clamp01(letterCollectionClock / letterCollectionSeconds);
        float fold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / .48f));
        float fly = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - .48f) / .52f));
        paperBodyGroup.alpha = 1f - fold;
        Vector2 destination = letterCollectionStart;
        if (letterCollectionTarget != null)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(EventCamera,
                letterCollectionTarget.TransformPoint(letterCollectionTarget.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(modalRoot, point, EventCamera, out destination);
        }
        var scale = new Vector2(Mathf.Lerp(letterCollectionScale.x, FoldedWidth, fold), letterCollectionScale.y)
            * Mathf.Lerp(1f, .08f, fly);
        var size = modalFrame.rect.size;
        var drawnSize = new Vector2(Mathf.Max(1f, Mathf.Round(size.x * scale.x)), Mathf.Max(1f, Mathf.Round(size.y * scale.y)));
        modalFrame.anchoredPosition = PixelCentre(Vector2.Lerp(letterCollectionStart, destination, fly), drawnSize);
        modalFrame.localScale = new Vector3(drawnSize.x / size.x, drawnSize.y / size.y, 1f);
        modalOpacity = 1f - Mathf.Clamp01((fly - .8f) / .2f);
        modalGroup.alpha = paused ? 0f : modalOpacity;
        if (p < 1f) return;
        var callback = letterCollected; letterCollected = null;
        HideModal(false); callback?.Invoke();
    }

    void ResetLetter()
    {
        if (letterOpening) os?.Chapter03Story?.SetLetterPressVisual(false);
        CursorHandle.Instance?.ClearContextState(this);
        // Every part lives under the modal root and is destroyed with it.
        letterLayer = letterEnvelope = letterPartnerPoint = letterCollectionTarget = null;
        letterScreenClose = null; letterSheetGroup = null;
        letterEntering = letterOpening = letterCollecting = letterUnfolded = letterPlayerClicked = false;
        letterUnfoldedCallback = letterCollected = null;
        letterEntranceClock = letterOpenClock = letterPartnerReady = letterCollectionClock = 0f;
    }
}
