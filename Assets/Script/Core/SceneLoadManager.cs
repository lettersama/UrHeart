using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central additive scene loader. GameSetUp remains loaded while UI and gameplay scenes
/// are loaded, unloaded, or replaced through this service.
/// </summary>
public class SceneLoadManager : Singleton<SceneLoadManager>
{
    sealed class PendingLoad
    {
        public bool SetActive;
        public float Progress;
        public readonly List<Action<Scene>> Completed = new List<Action<Scene>>();
        public readonly List<Action<string>> Failed = new List<Action<string>>();
        public readonly List<Action<float>> Progressed = new List<Action<float>>();
    }

    sealed class PendingUnload
    {
        public readonly List<Action<string>> Completed = new List<Action<string>>();
        public readonly List<Action<string>> Failed = new List<Action<string>>();
    }

    readonly struct BehaviourState
    {
        public BehaviourState(Behaviour behaviour)
        {
            Behaviour = behaviour;
            WasEnabled = behaviour.enabled;
        }

        public Behaviour Behaviour { get; }
        public bool WasEnabled { get; }
    }

    readonly Dictionary<string, PendingLoad> _pendingLoads =
        new Dictionary<string, PendingLoad>(StringComparer.Ordinal);
    readonly Dictionary<string, PendingUnload> _pendingUnloads =
        new Dictionary<string, PendingUnload>(StringComparer.Ordinal);
    readonly Dictionary<string, List<BehaviourState>> _presentationStates =
        new Dictionary<string, List<BehaviourState>>(StringComparer.Ordinal);

    public event Action<string> SceneLoadStarted;
    public event Action<string, float> SceneLoadProgressed;
    public event Action<Scene> SceneLoadCompleted;
    public event Action<string, string> SceneLoadFailed;
    public event Action<string> SceneUnloadStarted;
    public event Action<string> SceneUnloadCompleted;
    public event Action<string, string> SceneUnloadFailed;

    public bool IsBusy => _pendingLoads.Count > 0 || _pendingUnloads.Count > 0;

    public bool IsSceneLoaded(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return false;

        Scene scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    public bool TrySetActiveScene(string sceneName)
    {
        Scene scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded && SceneManager.SetActiveScene(scene);
    }

    public bool HasEnabledCamera(string sceneName)
    {
        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (camera.enabled && camera.gameObject.activeInHierarchy)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enables or suppresses a scene's cameras and audio listeners while preserving
    /// each component's original enabled state.
    /// </summary>
    public bool SetScenePresentationEnabled(string sceneName, bool enabled)
    {
        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        if (enabled)
        {
            if (!_presentationStates.TryGetValue(sceneName, out List<BehaviourState> states))
                return true;

            foreach (BehaviourState state in states)
            {
                if (state.Behaviour != null)
                    state.Behaviour.enabled = state.WasEnabled;
            }

            _presentationStates.Remove(sceneName);
            return true;
        }

        if (_presentationStates.ContainsKey(sceneName))
            return true;

        var capturedStates = new List<BehaviourState>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            CaptureAndDisable(root.GetComponentsInChildren<Camera>(true), capturedStates);
            CaptureAndDisable(root.GetComponentsInChildren<AudioListener>(true), capturedStates);
        }

        _presentationStates.Add(sceneName, capturedStates);
        return true;
    }

    public void LoadSceneAdditive(
        string sceneName,
        Action onLoadStarted = null,
        Action<Scene> onLoadCompleted = null,
        Action<string> onLoadFailed = null,
        bool setActiveScene = false,
        Action<float> onLoadProgress = null)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            onLoadFailed?.Invoke("Scene name is empty.");
            return;
        }

        onLoadStarted?.Invoke();

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (loadedScene.IsValid() && loadedScene.isLoaded)
        {
            if (setActiveScene)
                SceneManager.SetActiveScene(loadedScene);
            SceneLoadProgressed?.Invoke(sceneName, 1f);
            onLoadProgress?.Invoke(1f);
            onLoadCompleted?.Invoke(loadedScene);
            return;
        }

        if (_pendingLoads.TryGetValue(sceneName, out PendingLoad existing))
        {
            existing.SetActive |= setActiveScene;
            if (onLoadCompleted != null) existing.Completed.Add(onLoadCompleted);
            if (onLoadFailed != null) existing.Failed.Add(onLoadFailed);
            if (onLoadProgress != null)
            {
                existing.Progressed.Add(onLoadProgress);
                onLoadProgress.Invoke(existing.Progress);
            }
            return;
        }

        var pending = new PendingLoad { SetActive = setActiveScene };
        if (onLoadCompleted != null) pending.Completed.Add(onLoadCompleted);
        if (onLoadFailed != null) pending.Failed.Add(onLoadFailed);
        if (onLoadProgress != null) pending.Progressed.Add(onLoadProgress);
        _pendingLoads.Add(sceneName, pending);

        SceneLoadStarted?.Invoke(sceneName);
        ReportLoadProgress(sceneName, pending, 0f, true);
        StartCoroutine(LoadRoutine(sceneName, pending));
    }

    public void UnloadScene(
        string sceneName,
        Action onUnloadStarted = null,
        Action<string> onUnloadCompleted = null,
        Action<string> onUnloadFailed = null)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            onUnloadFailed?.Invoke("Scene name is empty.");
            return;
        }

        onUnloadStarted?.Invoke();

        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            onUnloadCompleted?.Invoke(sceneName);
            return;
        }

        if (_pendingUnloads.TryGetValue(sceneName, out PendingUnload existing))
        {
            if (onUnloadCompleted != null) existing.Completed.Add(onUnloadCompleted);
            if (onUnloadFailed != null) existing.Failed.Add(onUnloadFailed);
            return;
        }

        var pending = new PendingUnload();
        if (onUnloadCompleted != null) pending.Completed.Add(onUnloadCompleted);
        if (onUnloadFailed != null) pending.Failed.Add(onUnloadFailed);
        _pendingUnloads.Add(sceneName, pending);

        SceneUnloadStarted?.Invoke(sceneName);
        StartCoroutine(UnloadRoutine(sceneName, scene, pending));
    }

    /// <summary>Loads the replacement first, then unloads the previous gameplay scene.</summary>
    public void SwitchAdditiveScene(
        string sceneToUnload,
        string sceneToLoad,
        Action onSwitchStarted = null,
        Action<Scene> onSwitchCompleted = null,
        Action<string> onSwitchFailed = null,
        bool setLoadedSceneActive = true)
    {
        onSwitchStarted?.Invoke();

        LoadSceneAdditive(
            sceneToLoad,
            onLoadCompleted: loadedScene =>
            {
                if (string.IsNullOrWhiteSpace(sceneToUnload) || sceneToUnload == sceneToLoad)
                {
                    onSwitchCompleted?.Invoke(loadedScene);
                    return;
                }

                UnloadScene(
                    sceneToUnload,
                    onUnloadCompleted: _ => onSwitchCompleted?.Invoke(loadedScene),
                    onUnloadFailed: onSwitchFailed);
            },
            onLoadFailed: onSwitchFailed,
            setActiveScene: setLoadedSceneActive);
    }

    public void ReloadSceneAdditive(
        string sceneName,
        Action onReloadStarted = null,
        Action<Scene> onReloadCompleted = null,
        Action<string> onReloadFailed = null,
        bool setActiveScene = false)
    {
        onReloadStarted?.Invoke();

        void Load()
        {
            LoadSceneAdditive(
                sceneName,
                onLoadCompleted: onReloadCompleted,
                onLoadFailed: onReloadFailed,
                setActiveScene: setActiveScene);
        }

        if (IsSceneLoaded(sceneName))
        {
            UnloadScene(
                sceneName,
                onUnloadCompleted: _ => Load(),
                onUnloadFailed: onReloadFailed);
        }
        else
        {
            Load();
        }
    }

    IEnumerator LoadRoutine(string sceneName, PendingLoad pending)
    {
        AsyncOperation operation;
        try
        {
            operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        }
        catch (Exception exception)
        {
            FailLoad(sceneName, pending, exception.Message);
            yield break;
        }

        if (operation == null)
        {
            FailLoad(sceneName, pending, "Unity did not create a load operation.");
            yield break;
        }

        while (!operation.isDone)
        {
            float normalizedProgress = Mathf.Clamp01(operation.progress / 0.9f);
            ReportLoadProgress(
                sceneName,
                pending,
                Mathf.Min(normalizedProgress * 0.95f, 0.95f));
            yield return null;
        }

        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            FailLoad(sceneName, pending, "The load operation completed but the scene is not loaded.");
            yield break;
        }

        _pendingLoads.Remove(sceneName);
        if (pending.SetActive)
            SceneManager.SetActiveScene(scene);

        ReportLoadProgress(sceneName, pending, 1f, true);
        SceneLoadCompleted?.Invoke(scene);
        foreach (Action<Scene> callback in pending.Completed)
            callback?.Invoke(scene);
    }

    void ReportLoadProgress(
        string sceneName,
        PendingLoad pending,
        float progress,
        bool force = false)
    {
        float normalizedProgress = Mathf.Clamp01(progress);
        if (!force && Mathf.Abs(normalizedProgress - pending.Progress) < 0.001f)
            return;

        pending.Progress = normalizedProgress;
        SceneLoadProgressed?.Invoke(sceneName, normalizedProgress);
        foreach (Action<float> callback in pending.Progressed)
            callback?.Invoke(normalizedProgress);
    }

    IEnumerator UnloadRoutine(string sceneName, Scene scene, PendingUnload pending)
    {
        AsyncOperation operation;
        try
        {
            operation = SceneManager.UnloadSceneAsync(scene);
        }
        catch (Exception exception)
        {
            FailUnload(sceneName, pending, exception.Message);
            yield break;
        }

        if (operation == null)
        {
            FailUnload(sceneName, pending, "Unity did not create an unload operation.");
            yield break;
        }

        yield return operation;

        _pendingUnloads.Remove(sceneName);
        _presentationStates.Remove(sceneName);
        SceneUnloadCompleted?.Invoke(sceneName);
        foreach (Action<string> callback in pending.Completed)
            callback?.Invoke(sceneName);
    }

    void FailLoad(string sceneName, PendingLoad pending, string reason)
    {
        _pendingLoads.Remove(sceneName);
        Debug.LogError($"[SceneLoad] Failed to load '{sceneName}': {reason}");
        SceneLoadFailed?.Invoke(sceneName, reason);
        foreach (Action<string> callback in pending.Failed)
            callback?.Invoke(reason);
    }

    void FailUnload(string sceneName, PendingUnload pending, string reason)
    {
        _pendingUnloads.Remove(sceneName);
        Debug.LogError($"[SceneLoad] Failed to unload '{sceneName}': {reason}");
        SceneUnloadFailed?.Invoke(sceneName, reason);
        foreach (Action<string> callback in pending.Failed)
            callback?.Invoke(reason);
    }

    static void CaptureAndDisable<T>(T[] behaviours, List<BehaviourState> states)
        where T : Behaviour
    {
        foreach (T behaviour in behaviours)
        {
            if (behaviour is Camera camera
                && camera.TryGetComponent(out OSCameraHandle osCameraHandle)
                && osCameraHandle.PreserveDuringScenePresentation)
            {
                continue;
            }

            states.Add(new BehaviourState(behaviour));
            behaviour.enabled = false;
        }
    }
}
