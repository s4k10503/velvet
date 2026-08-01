#nullable enable
using UnityEngine;
using UnityEngine.Scripting;
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
    /// all. <c>Documentation~/player-builds.md</c> holds what each one cost.
    /// </para>
    /// <para>
    /// Kept explicitly rather than left to the linker: a build that strips this type or its enable message
    /// leaves the sheet unreachable for the whole run, and no suite in this repository builds under managed
    /// stripping, so nothing here would report it.
    /// </para>
    /// </remarks>
    [Preserve]
    public sealed class VelvetRuntimeAssets : ScriptableObject
    {
        [SerializeField]
        private StyleSheet? _styleUtilities;

        private static VelvetRuntimeAssets? s_instance;

        /// <summary>The loaded instance, or null before anything has loaded the asset.</summary>
        internal static VelvetRuntimeAssets? Instance => s_instance;

        internal StyleSheet? StyleUtilities => _styleUtilities;

        [Preserve]
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
