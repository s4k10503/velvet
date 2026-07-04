using UnityEngine;

namespace Velvet
{
    // Play/Edit-mode-aware destroy for the UnityEngine.Objects Velvet creates at runtime (shadow
    // bake Materials, clip-path VectorImages). DestroyImmediate outside play mode so EditMode
    // teardown releases the object synchronously without the "Destroy may not be called in edit
    // mode" warning. Null-safe and idempotent — shared so any hardening of the destroy rules
    // (asset-vs-instance guards, validation-context checks) lands in one place.
    internal static class VelvetObjectUtil
    {
        internal static void Destroy(Object? obj)
        {
            if (obj == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj);
            }
        }
    }
}
