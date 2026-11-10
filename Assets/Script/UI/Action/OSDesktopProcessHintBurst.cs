using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSDesktopProcessHintBurst : MonoBehaviour
{
    [SerializeField, Range(3, 16)] int particleCount = 7;
    [SerializeField] Vector2 particleSizeRange = new Vector2(3f, 6f);
    [SerializeField] Vector2 travelDistanceRange = new Vector2(13f, 25f);
    [SerializeField, Min(0.05f)] float duration = 0.42f;

    readonly List<HollowSquare> particles = new List<HollowSquare>();
    RectTransform effectRoot;
    Sequence burstSequence;

    public void Play(RectTransform target, Color color)
    {
        if (target == null)
            return;

        EnsureParticles();
        KillBurst();
        effectRoot.gameObject.SetActive(true);
        effectRoot.SetAsLastSibling();
        effectRoot.localPosition = transform.InverseTransformPoint(
            target.TransformPoint(target.rect.center));

        float effectDuration = Mathf.Max(0.05f, duration);
        Sequence sequence = DOTween.Sequence();
        burstSequence = sequence;
        for (int i = 0; i < particles.Count; i++)
        {
            HollowSquare particle = particles[i];
            float angle = 360f * i / particles.Count
                          + Random.Range(-18f, 18f);
            Vector2 direction = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad));
            float distance = Random.Range(
                Mathf.Min(travelDistanceRange.x, travelDistanceRange.y),
                Mathf.Max(travelDistanceRange.x, travelDistanceRange.y));
            float size = Random.Range(
                Mathf.Min(particleSizeRange.x, particleSizeRange.y),
                Mathf.Max(particleSizeRange.x, particleSizeRange.y));

            particle.SetSize(size);
            particle.SetColor(color);
            particle.Rect.anchoredPosition = Random.insideUnitCircle * 2f;
            particle.Rect.localRotation = Quaternion.Euler(
                0f,
                0f,
                Random.Range(-28f, 28f));
            particle.Rect.localScale = Vector3.one;

            sequence.Join(particle.Rect
                .DOAnchorPos(direction * distance, effectDuration)
                .SetEase(Ease.OutCubic));
            sequence.Join(particle.Rect
                .DORotate(
                    new Vector3(0f, 0f, Random.Range(-70f, 70f)),
                    effectDuration,
                    RotateMode.LocalAxisAdd)
                .SetEase(Ease.OutSine));
            sequence.Join(particle.Rect
                .DOScale(0.55f, effectDuration)
                .SetEase(Ease.InQuad));
            sequence.Join(DOVirtual.Float(
                    color.a,
                    0f,
                    effectDuration,
                    particle.SetAlpha)
                .SetEase(Ease.InQuad));
        }

        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() => FinishBurst(sequence))
            .OnKill(() => FinishBurst(sequence));
    }

    void OnDisable()
    {
        KillBurst();
    }

    void OnDestroy()
    {
        KillBurst();
    }

    void EnsureParticles()
    {
        if (effectRoot == null)
        {
            GameObject rootObject = new GameObject(
                "TutorialHintBurst",
                typeof(RectTransform));
            rootObject.layer = gameObject.layer;
            effectRoot = rootObject.GetComponent<RectTransform>();
            effectRoot.SetParent(transform, false);
            effectRoot.anchorMin = new Vector2(0.5f, 0.5f);
            effectRoot.anchorMax = new Vector2(0.5f, 0.5f);
            effectRoot.pivot = new Vector2(0.5f, 0.5f);
            effectRoot.sizeDelta = Vector2.zero;
        }

        int targetCount = Mathf.Clamp(particleCount, 3, 16);
        while (particles.Count < targetCount)
            particles.Add(CreateHollowSquare(particles.Count));
        while (particles.Count > targetCount)
        {
            int lastIndex = particles.Count - 1;
            HollowSquare particle = particles[lastIndex];
            particles.RemoveAt(lastIndex);
            if (particle.Rect != null)
                Destroy(particle.Rect.gameObject);
        }
    }

    HollowSquare CreateHollowSquare(int index)
    {
        GameObject squareObject = new GameObject(
            $"Square_{index:00}",
            typeof(RectTransform));
        squareObject.layer = effectRoot.gameObject.layer;
        RectTransform square = squareObject.GetComponent<RectTransform>();
        square.SetParent(effectRoot, false);
        square.anchorMin = new Vector2(0.5f, 0.5f);
        square.anchorMax = new Vector2(0.5f, 0.5f);
        square.pivot = new Vector2(0.5f, 0.5f);

        Image[] edges =
        {
            CreateEdge("Top", square),
            CreateEdge("Bottom", square),
            CreateEdge("Left", square),
            CreateEdge("Right", square)
        };
        return new HollowSquare(square, edges);
    }

    static Image CreateEdge(string objectName, RectTransform parent)
    {
        GameObject edgeObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        edgeObject.layer = parent.gameObject.layer;
        RectTransform edgeRect = edgeObject.GetComponent<RectTransform>();
        edgeRect.SetParent(parent, false);
        edgeRect.anchorMin = new Vector2(0.5f, 0.5f);
        edgeRect.anchorMax = new Vector2(0.5f, 0.5f);
        edgeRect.pivot = new Vector2(0.5f, 0.5f);
        Image edge = edgeObject.GetComponent<Image>();
        edge.raycastTarget = false;
        return edge;
    }

    void KillBurst()
    {
        if (burstSequence != null)
        {
            Sequence sequence = burstSequence;
            burstSequence = null;
            sequence.Kill(false);
        }

        if (effectRoot != null)
            effectRoot.gameObject.SetActive(false);
    }

    void FinishBurst(Sequence sequence)
    {
        if (burstSequence != sequence)
            return;

        burstSequence = null;
        if (effectRoot != null)
            effectRoot.gameObject.SetActive(false);
    }

    sealed class HollowSquare
    {
        public HollowSquare(RectTransform rect, Image[] edges)
        {
            Rect = rect;
            Edges = edges;
        }

        public RectTransform Rect { get; }
        Image[] Edges { get; }

        public void SetSize(float size)
        {
            float clampedSize = Mathf.Max(1f, size);
            float border = Mathf.Clamp(clampedSize * 0.18f, 0.55f, 1f);
            float half = clampedSize * 0.5f;
            float innerSize = Mathf.Max(0f, clampedSize - border * 2f);
            Rect.sizeDelta = Vector2.one * clampedSize;
            SetEdge(Edges[0], new Vector2(clampedSize, border),
                new Vector2(0f, half - border * 0.5f));
            SetEdge(Edges[1], new Vector2(clampedSize, border),
                new Vector2(0f, -half + border * 0.5f));
            SetEdge(Edges[2], new Vector2(border, innerSize),
                new Vector2(-half + border * 0.5f, 0f));
            SetEdge(Edges[3], new Vector2(border, innerSize),
                new Vector2(half - border * 0.5f, 0f));
        }

        public void SetColor(Color color)
        {
            for (int i = 0; i < Edges.Length; i++)
                Edges[i].color = color;
        }

        public void SetAlpha(float alpha)
        {
            float clampedAlpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < Edges.Length; i++)
            {
                Color color = Edges[i].color;
                color.a = clampedAlpha;
                Edges[i].color = color;
            }
        }

        static void SetEdge(Image edge, Vector2 size, Vector2 position)
        {
            edge.rectTransform.sizeDelta = size;
            edge.rectTransform.anchoredPosition = position;
        }
    }
}
