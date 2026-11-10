using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class Aya : MonoBehaviour
{
    [Header("Fly Away")]
    [SerializeField] Vector3 flightOffset = new Vector3(-8f, 6f, 0f);
    [SerializeField] Vector3 flightControlOffsetA = new Vector3(-2.5f, 3.5f, 0f);
    [SerializeField] Vector3 flightControlOffsetB = new Vector3(-7.5f, 9.5f, 0f);
    [SerializeField, Min(0.01f)] float flightDuration = 2.25f;
    [SerializeField] Ease flightEase = Ease.InQuad;
    [SerializeField] bool deactivateOnComplete;

    static readonly int IsFlyingParam = Animator.StringToHash("IsFlying");

    Animator _animator;
    Tween _flightTween;
    bool _hasFlown;
    bool _initialStateCaptured;
    Vector3 _initialLocalPosition;
    Quaternion _initialLocalRotation;
    Vector3 _initialLocalScale;

    public bool HasFlown => _hasFlown;

    void Awake()
    {
        CaptureInitialState();

        if (!TryGetComponent(out _animator))
            enabled = false;
    }

    void LateUpdate()
    {
        if (_hasFlown && _animator != null && !_animator.GetBool(IsFlyingParam))
            _animator.SetBool(IsFlyingParam, true);
    }

    public void FlyAway()
    {
        if (_hasFlown || _animator == null || !gameObject.activeInHierarchy)
            return;

        _hasFlown = true;
        _animator.SetBool(IsFlyingParam, true);

        _flightTween?.Kill();
        Vector3 startPosition = transform.localPosition;
        Vector3[] path =
        {
            startPosition + flightControlOffsetA,
            startPosition + flightControlOffsetB,
            startPosition + flightOffset,
        };

        _flightTween = transform
            .DOLocalPath(path, flightDuration, PathType.CatmullRom, PathMode.Ignore)
            .SetEase(flightEase)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (deactivateOnComplete)
                    gameObject.SetActive(false);
            })
            .OnKill(() => _flightTween = null);
    }

    public void ResetToInitialState()
    {
        if (!_initialStateCaptured)
            CaptureInitialState();

        _flightTween?.Kill();
        _flightTween = null;
        _hasFlown = false;
        gameObject.SetActive(true);

        if (_animator == null && !TryGetComponent(out _animator))
        {
            enabled = false;
            return;
        }

        _animator.Rebind();
        _animator.SetBool(IsFlyingParam, false);
        _animator.Update(0f);

        transform.localPosition = _initialLocalPosition;
        transform.localRotation = _initialLocalRotation;
        transform.localScale = _initialLocalScale;
    }

    void CaptureInitialState()
    {
        _initialLocalPosition = transform.localPosition;
        _initialLocalRotation = transform.localRotation;
        _initialLocalScale = transform.localScale;
        _initialStateCaptured = true;
    }

    void OnDestroy()
    {
        _flightTween?.Kill();
    }
}
