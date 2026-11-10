using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    public static T Instance { get; private set; }

    // Keep the old project-facing name while new code uses Instance.
    public static T m_Instence => Instance;

    protected bool IsSingletonInstance => Instance == this;

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(gameObject);
            return;
        }

        Instance = (T)this;
    }

    protected virtual void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
