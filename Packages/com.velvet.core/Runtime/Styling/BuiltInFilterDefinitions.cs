using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The first-party custom-filter definitions that back the brightness-*, saturate-* and drop-shadow-*
    // utilities. UI Toolkit's FilterFunctionType enum has no member for any of them, so the only way to render
    // one is a FilterFunctionType.Custom function bound to a FilterFunctionDefinition. Brightness and saturate
    // are one single-pass unlit shader each (Velvet/FilterBrightness, Velvet/FilterSaturate), authored to the
    // full CSS range (over-brighten / over-saturate) that the old Tint / grayscale(1-N) approximations could not
    // reach; drop-shadow is Velvet/FilterDropShadow's two passes.
    //
    // Each definition is a PROCESS-WIDE singleton, never rebuilt per resolve. This is load-bearing beyond
    // GPU/material economy: UI Toolkit's filter-list transition interpolation matches functions in part by
    // referring to the SAME definition on both sides of a tween, so a fresh CreateInstance per BuildFilter
    // call would silently break transition-all on brightness/saturate (the from/to list shapes would mismatch
    // and snap instead of tweening). Built lazily off VelvetShaders.Find, mirroring DropShadowBaker's
    // EnsureMaterial.
    internal static class BuiltInFilterDefinitions
    {
        private static FilterFunctionDefinition? s_brightness;
        private static FilterFunctionDefinition? s_saturate;
        private static FilterFunctionDefinition? s_dropShadow;

        internal static FilterFunctionDefinition? Brightness
            => IsUsable(s_brightness) ? s_brightness : s_brightness = Build(VelvetShaders.FilterBrightness, "_Brightness", "velvet-brightness");

        internal static FilterFunctionDefinition? Saturate
            => IsUsable(s_saturate) ? s_saturate : s_saturate = Build(VelvetShaders.FilterSaturate, "_Saturate", "velvet-saturate");

        // drop-shadow-*: offset x, offset y and standard deviation in points, then the colour.
        internal static FilterFunctionDefinition? DropShadow
            => IsUsable(s_dropShadow) ? s_dropShadow : s_dropShadow = BuildDropShadow();

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

        // An unavailable shader degrades to "layer omitted" rather than throwing, mirroring
        // DropShadowBaker.EnsureMaterial.
        private static FilterFunctionDefinition? Build(string shaderPath, string propertyName, string filterName)
        {
            var shader = VelvetShaders.Find(shaderPath, "Filter", "brightness/saturate layer");
            if (shader == null)
            {
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

        private static readonly int s_mainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int s_sourceId = Shader.PropertyToID("_DropShadowSource");
        private static readonly int s_offsetId = Shader.PropertyToID("_DropShadowOffset");
        private static readonly int s_sigmaId = Shader.PropertyToID("_DropShadowSigma");
        private static readonly int s_colorId = Shader.PropertyToID("_DropShadowColor");

        private static FilterFunctionDefinition? BuildDropShadow()
        {
            var shader = VelvetShaders.Find(VelvetShaders.FilterDropShadow, "Filter", "drop-shadow layer");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var def = ScriptableObject.CreateInstance<FilterFunctionDefinition>();
            def.hideFlags = HideFlags.HideAndDontSave;
            def.filterName = "velvet-drop-shadow";
            // Each slot pads to CSS's initial value for interpolation: every length 0, the colour transparent.
            def.parameters = new[]
            {
                new FilterParameterDeclaration { name = "offset-x", interpolationDefaultValue = new FilterParameter(0f) },
                new FilterParameterDeclaration { name = "offset-y", interpolationDefaultValue = new FilterParameter(0f) },
                new FilterParameterDeclaration { name = "deviation", interpolationDefaultValue = new FilterParameter(0f) },
                new FilterParameterDeclaration { name = "color", interpolationDefaultValue = new FilterParameter(Color.clear) },
            };
            def.passes = new[]
            {
                new PostProcessingPass
                {
                    material = material,
                    passIndex = 0,
                    applySettingsCallback = ApplyShadowAlphaSettings,
                    computeRequiredWriteMarginsCallback = ShadowExtent,
                },
                new PostProcessingPass
                {
                    material = material,
                    passIndex = 1,
                    applySettingsCallback = ApplyCompositeSettings,
                },
            };
            return def;
        }

        // The shadow's reach past the input on each side: its offset plus three deviations, and a point for the
        // bilinear read of a fractional offset.
        private static PostProcessingMargins ShadowExtent(FilterFunction function)
        {
            var x = function.GetParameter(0).floatValue;
            var y = function.GetParameter(1).floatValue;
            var reach = 3f * Mathf.Max(0f, function.GetParameter(2).floatValue) + 1f;
            return new PostProcessingMargins
            {
                left = Mathf.CeilToInt(Mathf.Max(0f, reach - x)),
                right = Mathf.CeilToInt(Mathf.Max(0f, reach + x)),
                top = Mathf.CeilToInt(Mathf.Max(0f, reach - y)),
                bottom = Mathf.CeilToInt(Mathf.Max(0f, reach + y)),
            };
        }

        private static void ApplyShadowAlphaSettings(MaterialPropertyBlock block, FilterPassContext context)
        {
            var function = context.filterFunction;
            block.SetTexture(s_sourceId, block.GetTexture(s_mainTexId));
            block.SetVector(s_offsetId, new Vector4(function.GetParameter(0).floatValue, function.GetParameter(1).floatValue));
            block.SetFloat(s_sigmaId, Deviation(context));
        }

        private static void ApplyCompositeSettings(MaterialPropertyBlock block, FilterPassContext context)
        {
            var color = context.filterFunction.GetParameter(3).colorValue;
            if (!context.readsGamma)
            {
                color = color.linear;
            }
            var alpha = Mathf.Clamp01(color.a);
            block.SetVector(s_colorId, new Vector4(color.r * alpha, color.g * alpha, color.b * alpha, alpha));
            block.SetFloat(s_sigmaId, Deviation(context));
        }

        private static float Deviation(FilterPassContext context)
            => Mathf.Max(0f, context.filterFunction.GetParameter(2).floatValue) * context.scaledPixelsPerPoint;

#if UNITY_EDITOR
        // These HideAndDontSave definitions survive a play-mode cycle that skips the domain reload, so drop the
        // cached references on subsystem registration to force the next resolve to rebuild — picking up a shader
        // edit rather than serving a definition built in the prior session. Unlike DropShadowBaker's reset, this
        // must NOT destroy the objects: a definition is referenced LIVE by every element that applied
        // brightness-* / saturate-* / drop-shadow-* (the FilterFunction in its style.filter holds the definition directly, where
        // a bake material is only a throwaway tool no element retains), so destroying it would strand those
        // already-applied filters at a dead object that no re-resolve heals. Dropping the reference alone lets a
        // fresh element build a new definition while an existing one keeps rendering against the live object; the
        // orphaned prior definition is a negligible editor-only object reclaimed on the next domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            s_brightness = null;
            s_saturate = null;
            s_dropShadow = null;
        }
#endif
    }
}
