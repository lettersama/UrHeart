using System.Collections;
using UnityEngine;

/// <summary>Attach a subclass to an authored hotspot for bespoke effects.
/// Yield until it is complete; the controller changes scenes only after return.
/// Cancel must stop any external animation started by the effect.</summary>
public abstract class OSMemoryPicEffect : MonoBehaviour
{
    public abstract IEnumerator Play(OSMemoryPicController context);
    public virtual void Cancel() { }
}
