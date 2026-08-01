#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Holds the package assets a player needs and cannot look up by name.
    /// </summary>
    /// <remarks>
    /// A build includes an asset because something in it is referenced; a player then needs a way back to
    /// that reference, and <c>PlayerSettings.preloadedAssets</c> loads an object without giving any way to
    /// find it. So the preloaded object is this one, and it publishes itself as it loads.
    /// <para>
    /// The alternative was a <c>Resources</c> folder, which is a lookup by name and needs no build step at
    /// all. It was measured at 46 ms of engine startup against 21 ms for this, on a build of this
    /// repository's own sample scene, and Unity documents the folder as the thing to avoid.
    /// </para>
    /// </remarks>
    public sealed class VelvetRuntimeAssets : ScriptableObject
    {
        [SerializeField]
        private StyleSheet? _styleUtilities;

        private static VelvetRuntimeAssets? s_instance;

        /// <summary>The loaded instance, or null in an editor that has not opened the asset.</summary>
        internal static VelvetRuntimeAssets? Instance => s_instance;

        internal StyleSheet? StyleUtilities => _styleUtilities;

        // Unity calls this as the preloaded asset is loaded, before the first scene's Awake, which is what
        // makes the reference available to anything a scene does on start.
        private void OnEnable() => s_instance = this;

        private void OnDisable()
        {
            if (ReferenceEquals(s_instance, this))
            {
                s_instance = null;
            }
        }
    }
}
