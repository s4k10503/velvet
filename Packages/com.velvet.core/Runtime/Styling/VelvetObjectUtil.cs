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

        // Marks a framework-owned scene GameObject (the hidden simulation and panel hosts): hidden
        // from the hierarchy and excluded from editor scene saves, but deliberately NOT the full
        // HideAndDontSave — the DontSaveInBuild flag pulls a GameObject out of its scene entirely
        // (scene.IsValid() turns false), and these hosts must stay ordinary scene objects: they are
        // framework-owned, destroyed with their owner, and a runtime-instantiated object never
        // reaches a build's serialized data anyway.
        internal static void HideFrameworkSceneObject(GameObject gameObject)
            => gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
    }
}
