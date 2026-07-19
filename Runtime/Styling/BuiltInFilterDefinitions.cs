using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The two first-party custom-filter definitions that back the brightness-* and saturate-* utilities.
    // UI Toolkit's FilterFunctionType enum has no Brightness/Saturate member, so the only way to render either
    // is a FilterFunctionType.Custom function bound to a FilterFunctionDefinition. These are the definitions:
    // one single-pass unlit shader each (Velvet/FilterBrightness, Velvet/FilterSaturate), authored to the full
    // CSS range (over-brighten / over-saturate) that the old Tint / grayscale(1-N) approximations could not
    // reach.
    //
    // Each definition is a PROCESS-WIDE singleton, never rebuilt per resolve. This is load-bearing beyond
    // GPU/material economy: UI Toolkit's filter-list transition interpolation matches functions in part by
    // referring to the SAME definition on both sides of a tween, so a fresh CreateInstance per BuildFilter
    // call would silently break transition-all on brightness/saturate (the from/to list shapes would mismatch
    // and snap instead of tweening). Built lazily off Shader.Find, mirroring DropShadowBaker's EnsureMaterial.
    internal static class BuiltInFilterDefinitions
    {
        private static FilterFunctionDefinition? s_brightness;
        private static FilterFunctionDefinition? s_saturate;

        // Shader paths already warned about as missing, so a build that permanently lacks a filter shader logs
        // once instead of on every resolve (the properties rebuild whenever the cached definition is null, so a
        // missing shader is retried every access). Cleared on the editor reset below.
        private static readonly HashSet<string> s_missingShaderWarned = new();

        internal static FilterFunctionDefinition? Brightness
            => IsUsable(s_brightness) ? s_brightness : s_brightness = Build("Velvet/FilterBrightness", "_Brightness", "velvet-brightness");

        internal static FilterFunctionDefinition? Saturate
            => IsUsable(s_saturate) ? s_saturate : s_saturate = Build("Velvet/FilterSaturate", "_Saturate", "velvet-saturate");

        // Identity checks against the CACHED definitions only (never forcing a lazy Build): a caller probing an
        // arbitrary function's definition must not load the brightness/saturate shaders as a side effect. A
        // first-party custom exists on an element only after its definition was built, so a live built-in
        // function always matches the cached reference here.
        internal static bool IsBrightness(FilterFunctionDefinition? def) => def != null && ReferenceEquals(def, s_brightness);

        internal static bool IsSaturate(FilterFunctionDefinition? def) => def != null && ReferenceEquals(def, s_saturate);

        // True for either first-party built-in definition — the ones that interpolate like a native float
        // filter, as opposed to a user filter-[name:args] custom whose parameters carry no tween semantics.
        internal static bool IsBuiltIn(FilterFunctionDefinition? def) => IsBrightness(def) || IsSaturate(def);

        // A cached definition is reusable only while both it and the material its single pass binds are live.
        // A shader reimport can destroy the pass material out from under a surviving definition; UI Toolkit's
        // == treats a destroyed object as null, so serving that definition would bind a dead material. Rebuild
        // on demand instead. The definition itself going fake-null is caught by the same check.
        private static bool IsUsable(FilterFunctionDefinition? def)
        {
            if (def == null)
            {
                return false;
            }
            var passes = def!.passes;
            return passes != null && passes.Length > 0 && passes[0].material != null;
        }

        // Builds the definition for one shader, or null when the shader is unavailable (a stripped player
        // build that dropped it from Always Included Shaders) — the same un-mitigated gap the bake shaders
        // already have. Degrade to "layer omitted" with a single warning rather than throw, mirroring
        // DropShadowBaker.EnsureMaterial.
        private static FilterFunctionDefinition? Build(string shaderPath, string propertyName, string filterName)
        {
            var shader = Shader.Find(shaderPath);
            if (shader == null)
            {
                if (s_missingShaderWarned.Add(shaderPath))
                {
                    FiberLogger.LogWarning("Filter", $"Shader not found: {shaderPath}. " +
                        "Ensure it is included in the build (Always Included Shaders); the brightness/saturate layer is omitted.");
                }
                return null;
            }

            var def = ScriptableObject.CreateInstance<FilterFunctionDefinition>();
            def.hideFlags = HideFlags.HideAndDontSave;
            def.filterName = filterName;
            // Exactly one declared parameter, defaulting to the CSS identity (brightness(1)/saturate(1) are
            // no-ops). It is also the value UI Toolkit falls back to when padding a cross-transition, per
            // FilterParameterDeclaration.interpolationDefaultValue.
            def.parameters = new[]
            {
                new FilterParameterDeclaration
                {
                    name = "amount",
                    interpolationDefaultValue = new FilterParameter(1f),
                },
            };
            def.passes = new[]
            {
                new PostProcessingPass
                {
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave },
                    passIndex = 0,
                    // Binds FilterParameter index 0 to the shader's amount property.
                    parameterBindings = new[]
                    {
                        new ParameterBinding { index = 0, name = propertyName },
                    },
                },
            };
            return def;
        }

#if UNITY_EDITOR
        // These HideAndDontSave definitions survive a play-mode cycle that skips the domain reload, so drop the
        // cached references on subsystem registration to force the next resolve to rebuild — picking up a shader
        // edit rather than serving a definition built in the prior session. Unlike DropShadowBaker's reset, this
        // must NOT destroy the objects: a definition is referenced LIVE by every element that applied
        // brightness-* / saturate-* (the FilterFunction in its style.filter holds the definition directly, where
        // a bake material is only a throwaway tool no element retains), so destroying it would strand those
        // already-applied filters at a dead object that no re-resolve heals. Dropping the reference alone lets a
        // fresh element build a new definition while an existing one keeps rendering against the live object; the
        // orphaned prior definition is a negligible editor-only object reclaimed on the next domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            s_brightness = null;
            s_saturate = null;
            // Re-arm the one-shot missing-shader warning so a shader edit that fixes availability (or newly
            // breaks it) is reported once in the next session rather than staying silent.
            s_missingShaderWarned.Clear();
        }
#endif
    }
}
