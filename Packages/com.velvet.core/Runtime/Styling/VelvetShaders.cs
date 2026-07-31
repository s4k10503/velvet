using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // The names below are the package's whole shader surface, and they are what the build-time inclusion
    // mechanism resolves — so a shader added to the package without a name here would ship unreachable.
    // BundledShaderInclusionTests pins the two against each other by walking the runtime tree.
    //
    // Velvet.Editor.BundledShaderBuildInclusion is what keeps these resolvable in a player;
    // Documentation~/player-builds.md owns that mechanism and what it costs.
    internal static class VelvetShaders
    {
        internal const string DropShadow = "Velvet/DropShadow";
        internal const string GradientSilhouette = "Velvet/GradientSilhouette";
        internal const string FilterBrightness = "Velvet/FilterBrightness";
        internal const string FilterSaturate = "Velvet/FilterSaturate";

        internal static readonly string[] Names =
        {
            DropShadow,
            GradientSilhouette,
            FilterBrightness,
            FilterSaturate,
        };

        // Every lookup goes through here so a name that does not resolve is reported once per run rather than
        // once per ask: nothing caches a failed lookup, so a drop-shadow caster asks again on every bake and a
        // filter definition asks again on every resolve.
        private static readonly HashSet<string> s_missingWarned = new();

        internal static Shader? Find(string shaderName, string logTag, string omitted)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null && s_missingWarned.Add(shaderName))
            {
                FiberLogger.LogWarning(logTag,
                    $"Shader not found: {shaderName}. It ships with the package; the {omitted} is omitted.");
            }
            return shader;
        }

#if UNITY_EDITOR
        // Re-arm so a shader edit that fixes availability, or newly breaks it, is reported in the next play
        // session rather than staying silent behind a gate the prior session closed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWarnings() => s_missingWarned.Clear();
#endif
    }
}
