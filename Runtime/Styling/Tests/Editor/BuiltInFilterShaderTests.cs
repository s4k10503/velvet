using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of the shader-backed <c>brightness-*</c> / <c>saturate-*</c> utilities:
    /// <list type="bullet">
    /// <item>Each resolves to a <see cref="FilterFunctionType.Custom"/> function bound to its first-party
    /// <see cref="FilterFunctionDefinition"/> (<see cref="BuiltInFilterDefinitions.Brightness"/> /
    /// <see cref="BuiltInFilterDefinitions.Saturate"/>), carrying the raw CSS factor as its single
    /// parameter — including the over-brighten / over-saturate range (N &gt; 1) the old Tint /
    /// grayscale(1-N) approximations could not reach.</item>
    /// <item>Both keep their own slot in canonical CSS filter order rather than routing through
    /// <c>LayerMap.Customs</c> (which would compose them after every built-in), so
    /// <c>contrast-125 brightness-50</c> still puts brightness first.</item>
    /// <item>The definitions are process-wide singletons, so two elements with different values share the
    /// same definition reference — the identity UI Toolkit's filter transition interpolation matches on.</item>
    /// <item>Each definition declares exactly one parameter defaulting to the CSS identity (1), so the
    /// always-supplied count equals the declared count and no stale material-property state can leak.</item>
    /// <item>The editor subsystem-registration reset only DROPS the cached references; it never destroys a
    /// definition, so an already-applied filter that holds one by reference keeps a live object.</item>
    /// </list>
    /// </summary>
    internal sealed class BuiltInFilterShaderTests
    {
        private static void ApplyToken(VisualElement el, string token)
        {
            Assume.That(StyleArbitraryValueResolver.TryParse(token, out var style), Is.True,
                $"Precondition: {token} is a recognized utility");
            StyleArbitraryValueResolver.Apply(el, in style);
        }

        [Test]
        public void Given_ABrightnessBracketValue_When_Applied_Then_TheInlineFilterBindsTheBuiltInBrightnessDefinition()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            ApplyToken(el, "brightness-[1.5]");

            // Assert
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].type, f[0].customDefinition, f[0].GetParameter(0).floatValue),
                Is.EqualTo((1, FilterFunctionType.Custom, BuiltInFilterDefinitions.Brightness, 1.5f)));
        }

        [Test]
        public void Given_ASaturateBracketValue_When_Applied_Then_TheInlineFilterBindsTheBuiltInSaturateDefinition()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            ApplyToken(el, "saturate-[1.5]");

            // Assert
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].type, f[0].customDefinition, f[0].GetParameter(0).floatValue),
                Is.EqualTo((1, FilterFunctionType.Custom, BuiltInFilterDefinitions.Saturate, 1.5f)));
        }

        [Test]
        public void Given_BrightnessAppliedAfterContrast_When_Composed_Then_BrightnessStillPrecedesContrastInTheList()
        {
            // Arrange — the built-in arrives first; brightness (now a Custom function) must still slot ahead
            // of it in canonical CSS order rather than composing after every built-in like LayerMap.Customs.
            var el = new VisualElement();
            ApplyToken(el, "contrast-125");

            // Act
            ApplyToken(el, "brightness-50");

            // Assert
            var f = el.style.filter.value;
            Assert.That((f[0].customDefinition, f[1].type),
                Is.EqualTo((BuiltInFilterDefinitions.Brightness, FilterFunctionType.Contrast)));
        }

        [Test]
        public void Given_SaturateAppliedBeforeSepia_When_Composed_Then_SaturateStillPrecedesSepia()
        {
            // Arrange — the symmetric guard at the other end of canonical order: saturate precedes sepia.
            var el = new VisualElement();
            ApplyToken(el, "sepia");

            // Act
            ApplyToken(el, "saturate-50");

            // Assert
            var f = el.style.filter.value;
            Assert.That((f[0].customDefinition, f[1].type),
                Is.EqualTo((BuiltInFilterDefinitions.Saturate, FilterFunctionType.Sepia)));
        }

        [Test]
        public void Given_TwoElementsWithDifferentBrightnessValues_When_BothResolved_Then_TheyShareTheSameDefinitionInstance()
        {
            // Arrange — different values on two elements must still refer to the ONE shared definition, the
            // reference UI Toolkit's filter transition interpolation matches on; a per-resolve CreateInstance
            // would break transition-all on brightness without failing any per-element test. Asserting the
            // Custom type alongside the shared identity keeps this RED against the old Tint path, where both
            // customDefinitions are null and a bare shared-reference check passes on null == null.
            var a = new VisualElement();
            var b = new VisualElement();
            ApplyToken(a, "brightness-[0.4]");

            // Act
            ApplyToken(b, "brightness-[0.9]");

            // Assert
            var fa = a.style.filter.value[0];
            var fb = b.style.filter.value[0];
            Assert.That((fa.type, fb.type, ReferenceEquals(fa.customDefinition, fb.customDefinition)),
                Is.EqualTo((FilterFunctionType.Custom, FilterFunctionType.Custom, true)));
        }

        [Test]
        public void Given_ABrightnessValueAboveOne_When_Applied_Then_TheShaderParameterCarriesTheUnclampedValue()
        {
            // Arrange — a value that could not even reach Apply before the parser widening.
            var el = new VisualElement();

            // Act
            ApplyToken(el, "brightness-[2]");

            // Assert
            Assert.That(el.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(2f));
        }

        [Test]
        public void Given_ASaturateValueAboveOne_When_Applied_Then_TheShaderParameterCarriesTheUnclampedValue()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            ApplyToken(el, "saturate-[2]");

            // Assert
            Assert.That(el.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(2f));
        }

        [Test]
        public void Given_TheBuiltInBrightnessDefinition_When_Inspected_Then_ItDeclaresExactlyOneParameterDefaultingToIdentity()
        {
            // Arrange
            var def = BuiltInFilterDefinitions.Brightness;
            Assume.That(def, Is.Not.Null, "Precondition: the brightness shader resolved into a definition");

            // Act
            var parameters = def.parameters;

            // Assert — declared count is exactly one, so the always-supplied count matches and no stale
            // material-property state can leak; the default is the CSS identity.
            Assert.That((parameters.Length, parameters[0].interpolationDefaultValue.floatValue),
                Is.EqualTo((1, 1f)));
        }

        [Test]
        public void Given_TheBuiltInSaturateDefinition_When_Inspected_Then_ItDeclaresExactlyOneParameterDefaultingToIdentity()
        {
            // Arrange
            var def = BuiltInFilterDefinitions.Saturate;
            Assume.That(def, Is.Not.Null, "Precondition: the saturate shader resolved into a definition");

            // Act
            var parameters = def.parameters;

            // Assert
            Assert.That((parameters.Length, parameters[0].interpolationDefaultValue.floatValue),
                Is.EqualTo((1, 1f)));
        }

        [Test]
        public void Given_AnAppliedBrightnessFilter_When_TheEditorCachesReset_Then_TheLiveDefinitionItReferencesIsNotDestroyed()
        {
            // Arrange — an applied filter holds its definition by reference; the subsystem-registration reset
            // that runs on a domain-reload-skipping play-mode enter must only drop the cached reference, never
            // destroy the object. Destroying it (the pre-fix behavior) strands this live reference at a dead
            // Unity object that no re-resolve heals.
            var el = new VisualElement();
            ApplyToken(el, "brightness-[1.5]");
            var applied = el.style.filter.value[0].customDefinition;
            Assume.That(applied != null, Is.True, "Precondition: the filter bound a live definition");

            // Act
            InvokeEditorReset();

            // Assert — a destroyed Unity object compares equal to null under UI Toolkit's overloaded operator;
            // a surviving one does not.
            Assert.That(applied != null, Is.True);
        }

        // Drives the editor-only subsystem-registration reset directly (no PlayerLoop tick in EditMode) via
        // reflection, exercising the internal hook without a production test seam.
        private static void InvokeEditorReset()
        {
            var reset = typeof(BuiltInFilterDefinitions).GetMethod(
                "ResetStaticCaches", BindingFlags.NonPublic | BindingFlags.Static);
            Assume.That(reset, Is.Not.Null, "Precondition: the editor cache-reset hook exists");
            reset!.Invoke(null, null);
        }
    }
}
