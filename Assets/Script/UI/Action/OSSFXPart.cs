using UnityEngine;

public abstract class OSSFXPart : MonoBehaviour
{
    public abstract string PartName { get; }
    public abstract bool IsEffectEnabled { get; }

    public abstract void SetEffectEnabled(bool enabled);
}
