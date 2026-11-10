using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(AudioListener))]
public sealed class OSCameraHandle : Singleton<OSCameraHandle>
{
    [SerializeField] Camera osCamera;
    [SerializeField] AudioListener osAudioListener;

    public Camera Camera => osCamera;
    public bool PreserveDuringScenePresentation => true;

    readonly HashSet<Object> activePresentationOwners = new HashSet<Object>();
    bool legacyActiveRequest;

    protected override void Awake()
    {
        base.Awake();
        if (!IsSingletonInstance)
            return;

        ResolveComponents();
    }

    public void SetOSCameraActive(bool isActive)
    {
        legacyActiveRequest = isActive;
        ApplyRequestedState();
    }

    public void SetPresentationActive(Object owner, bool isActive)
    {
        if (owner == null)
            return;

        if (isActive)
            activePresentationOwners.Add(owner);
        else
            activePresentationOwners.Remove(owner);

        ApplyRequestedState();
    }

    void ApplyRequestedState()
    {
        ResolveComponents();

        activePresentationOwners.RemoveWhere(owner => owner == null);
        bool isActive = legacyActiveRequest || activePresentationOwners.Count > 0;

        if (osCamera != null)
            osCamera.enabled = isActive;

        if (osAudioListener != null)
            osAudioListener.enabled = isActive;
    }

    void ResolveComponents()
    {
        if (osCamera == null)
            osCamera = GetComponent<Camera>();

        if (osAudioListener == null)
            osAudioListener = GetComponent<AudioListener>();
    }

    void Reset()
    {
        ResolveComponents();
    }
}
