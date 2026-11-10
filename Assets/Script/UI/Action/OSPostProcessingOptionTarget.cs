using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Volume))]
public sealed class OSPostProcessingOptionTarget : MonoBehaviour
{
    Volume targetVolume;
    GameDataManager boundDataManager;

    void Awake()
    {
        targetVolume = GetComponent<Volume>();
    }

    void OnEnable()
    {
        Bind();
    }

    void Start()
    {
        Bind();
    }

    void OnDisable()
    {
        Unbind();
    }

    void OnDestroy()
    {
        Unbind();
    }

    void Bind()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        if (boundDataManager != dataManager)
        {
            Unbind();
            boundDataManager = dataManager;
            boundDataManager.OSPostProcessingOptionChanged +=
                HandleOptionChanged;
        }

        HandleOptionChanged(
            boundDataManager.GameOptions.isOSPostProcessingEnabled);
    }

    void Unbind()
    {
        if (boundDataManager == null)
            return;

        boundDataManager.OSPostProcessingOptionChanged -=
            HandleOptionChanged;
        boundDataManager = null;
    }

    void HandleOptionChanged(bool isEnabled)
    {
        if (targetVolume == null)
            targetVolume = GetComponent<Volume>();

        if (targetVolume != null)
            targetVolume.enabled = isEnabled;
    }
}
