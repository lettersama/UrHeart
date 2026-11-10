using UnityEngine;

// EditorOnly strips the authoring group from builds; Awake also hides it in Play Mode.
[DisallowMultipleComponent]
public sealed class OSMemorySubwindowPreview : MonoBehaviour
{
    void Awake()
    {
        if (Application.isPlaying)
            gameObject.SetActive(false);
    }
}
