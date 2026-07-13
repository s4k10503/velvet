using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of the custom filter registry and the <c>filter-[name:args]</c> utility:
    /// <list type="bullet">
    /// <item>A registered name resolves to a <see cref="FilterFunctionType.Custom"/> function carrying the
    /// registered <see cref="FilterFunctionDefinition"/>. Colon-separated arguments fill the definition's
    /// declared parameters in order and are parsed by each slot's DECLARED type (float slots take signed
    /// floats, color slots take the color grammar); a missing tail is padded from the declaration's
    /// defaults (the same values the USS parser pads with), so the composed function always carries the
    /// full declared parameter count — an under-supplied function would otherwise read stale
    /// material-property state at render time.</item>
    /// <item>Custom functions compose into the one inline <c>filter</c> list with the built-in filter
    /// utilities — built-ins first (canonical CSS order), then customs in first-application order; a
    /// repeated name replaces its own layer (and keeps its compose slot) instead of stacking a duplicate
    /// or re-slotting to the end.</item>
    /// <item>Custom filters participate in per-property layer priority like every other arbitrary value:
    /// a variant layer (hover:) over the same name wins while active and restores the base on clear.</item>
    /// <item>An unregistered name, an argument that fails its declared slot's grammar, more arguments than
    /// the declaration, or a malformed argument is not claimed (the token stays an inert class), and the
    /// variant tokenizer never mistakes the colon inside the brackets for a variant separator.</item>
    /// <item>Lifecycle: clearing an applied token still removes its layer after the name was unregistered
    /// (the clear path resolves the name syntactically, not through the registry), and a definition
    /// destroyed after registration is skipped at compose time instead of throwing.</item>
    /// <item>Registration validates its inputs: reserved built-in family names (case-insensitively), null
    /// definitions and malformed names are rejected with a warning; a duplicate registration warns and
    /// overwrites.</item>
    /// </list>
    /// </summary>
    internal sealed class CustomFilterRegistryTests
    {
        private FilterFunctionDefinition _dissolveDef;
        private FilterFunctionDefinition _glowDef;
        private FilterFunctionDefinition _fadeDef;
        private FilterFunctionDefinition _waveDef;
        private readonly List<Object> _spawned = new();
        private readonly List<string> _registered = new();

        [SetUp]
        public void SetUp()
        {
            // Declarations model real definitions: dissolve/fade/wave declare one float slot, glow
            // declares a color slot then a float slot — argument parsing and padding are driven by them.
            _dissolveDef = CreateDefinition(new FilterParameter(0.25f));
            _glowDef = CreateDefinition(new FilterParameter(Color.white), new FilterParameter(1f));
            _fadeDef = CreateDefinition(new FilterParameter(1f));
            _waveDef = CreateDefinition(new FilterParameter(0f));
            RegisterForTestRun("dissolve", _dissolveDef);
            RegisterForTestRun("glow", _glowDef);
            RegisterForTestRun("fade", _fadeDef);
            RegisterForTestRun("wave", _waveDef);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var name in _registered)
            {
                VelvetFilters.Unregister(name);
            }
            _registered.Clear();
            foreach (var obj in _spawned)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _spawned.Clear();
        }

        // Each parameterDefault becomes one declared parameter slot whose TYPE and padding default the
        // resolver must honor (a float FilterParameter declares a float slot, a color one a color slot).
        private FilterFunctionDefinition CreateDefinition(params FilterParameter[] parameterDefaults)
        {
            var def = ScriptableObject.CreateInstance<FilterFunctionDefinition>();
            if (parameterDefaults.Length > 0)
            {
                var declarations = new FilterParameterDeclaration[parameterDefaults.Length];
                for (var i = 0; i < parameterDefaults.Length; i++)
                {
                    declarations[i] = new FilterParameterDeclaration
                    {
                        name = "p" + i,
                        interpolationDefaultValue = parameterDefaults[i],
                    };
                }
                def.parameters = declarations;
            }
            _spawned.Add(def);
            return def;
        }

        private void RegisterForTestRun(string name, FilterFunctionDefinition def)
        {
            VelvetFilters.Register(name, def);
            _registered.Add(name);
        }

        #region Resolution and composition

        [Test]
        public void Given_ARegisteredCustomFilter_When_TheBracketTokenIsApplied_Then_TheInlineFilterCarriesTheCustomFunction()
        {
            // Arrange
            var el = new VisualElement();
            Assume.That(StyleArbitraryValueResolver.TryParse("filter-[dissolve:0.4]", out _), Is.True,
                "Precondition: a registered filter-[name:arg] token is claimed by the resolver");

            // Act
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.4]", StyleLayerPriority.Base);

            // Assert — one Custom function bound to the registered definition, carrying the float argument.
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].type, f[0].customDefinition, f[0].GetParameter(0).floatValue),
                Is.EqualTo((1, FilterFunctionType.Custom, _dissolveDef, 0.4f)));
        }

        [Test]
        public void Given_ACustomFilterStackedWithABuiltIn_When_Applied_Then_BuiltInsComposeBeforeTheCustom()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.4]", StyleLayerPriority.Base);

            // Act — the built-in arrives after the custom, yet must still compose ahead of it.
            StyleArbitraryValueResolver.ApplyClassToken(el, "blur-[2px]", StyleLayerPriority.Base);

            // Assert
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].type, f[1].type),
                Is.EqualTo((2, FilterFunctionType.Blur, FilterFunctionType.Custom)));
        }

        [Test]
        public void Given_TwoCustomFilters_When_Applied_Then_TheyComposeInApplicationOrder()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[fade:1]", StyleLayerPriority.Base);

            // Act
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.4]", StyleLayerPriority.Base);

            // Assert — application order is preserved (fade first), keyed by their definitions.
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].customDefinition, f[1].customDefinition),
                Is.EqualTo((2, _fadeDef, _dissolveDef)));
        }

        [Test]
        public void Given_ARepeatedCustomFilterName_When_ReApplied_Then_TheLatestArgumentsWin()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.2]", StyleLayerPriority.Base);

            // Act — the same name again replaces its layer instead of stacking a second function.
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.7]", StyleLayerPriority.Base);

            // Assert
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].GetParameter(0).floatValue), Is.EqualTo((1, 0.7f)));
        }

        [Test]
        public void Given_ACustomFilterCleared_When_ABuiltInSurvives_Then_TheListKeepsOnlyTheBuiltIn()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "blur-[2px]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.4]", StyleLayerPriority.Base);

            // Act
            StyleArbitraryValueResolver.ClearClassToken(el, "filter-[dissolve:0.4]", StyleLayerPriority.Base);

            // Assert
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].type), Is.EqualTo((1, FilterFunctionType.Blur)));
        }

        [Test]
        public void Given_ABuiltInCleared_When_ACustomSurvives_Then_TheListKeepsOnlyTheCustom()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "blur-[2px]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.4]", StyleLayerPriority.Base);

            // Act — clearing any filter member rewrites the shared list; the custom must survive the rewrite.
            StyleArbitraryValueResolver.ClearClassToken(el, "blur-[2px]", StyleLayerPriority.Base);

            // Assert
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].customDefinition), Is.EqualTo((1, _dissolveDef)));
        }

        [Test]
        public void Given_ASameNameArgumentChangeThroughClearAndReapply_When_Recomposed_Then_TheComposeSlotIsKept()
        {
            // Arrange — the class-diff path replaces a changed token by clearing the old one and applying
            // the new one; the name's compose slot must survive that churn or two co-applied custom
            // filters silently swap order on the first argument update.
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.4]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[fade:1]", StyleLayerPriority.Base);

            // Act
            StyleArbitraryValueResolver.ClearClassToken(el, "filter-[dissolve:0.4]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.9]", StyleLayerPriority.Base);

            // Assert — dissolve keeps its first-application slot ahead of fade.
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].customDefinition, f[1].customDefinition),
                Is.EqualTo((2, _dissolveDef, _fadeDef)));
        }

        [Test]
        public void Given_AHoverLayeredCustomFilter_When_TheHoverLayerClears_Then_TheBaseArgumentsAreRestored()
        {
            // Arrange — the same name layered at Base and at Hover; the hover layer wins while active.
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.3]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.9]", StyleLayerPriority.Hover);
            Assume.That(el.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(0.9f),
                "Precondition: the hover layer overrides the base while active");

            // Act
            StyleArbitraryValueResolver.ClearClassToken(el, "filter-[dissolve:0.9]", StyleLayerPriority.Hover);

            // Assert
            Assert.That(el.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(0.3f));
        }

        #endregion

        #region Argument grammar

        [Test]
        public void Given_ANoArgumentCustomFilter_When_Applied_Then_TheDeclaredDefaultsArePadded()
        {
            // Arrange
            var el = new VisualElement();

            // Act — a bare name applies the filter with the declaration's own defaults filled in, the same
            // values the USS parser pads with; an under-filled function would read stale material-property
            // state at render time instead of the declared defaults.
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve]", StyleLayerPriority.Base);

            // Assert
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].type, f[0].parameterCount, f[0].GetParameter(0).floatValue),
                Is.EqualTo((1, FilterFunctionType.Custom, 1, 0.25f)));
        }

        [Test]
        public void Given_AnUnderSuppliedArgumentList_When_Applied_Then_TheMissingTailIsPaddedFromTheDeclaration()
        {
            // Arrange — glow declares (color, float); only the color is supplied.
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[glow:#ff0000]", StyleLayerPriority.Base);

            // Assert — the float slot carries the declaration's default, not an unset zero.
            var fn = el.style.filter.value[0];
            Assert.That((fn.parameterCount, fn.GetParameter(1).floatValue), Is.EqualTo((2, 1f)));
        }

        [Test]
        public void Given_MoreArgumentsThanTheDeclaration_When_Parsed_Then_TheTokenIsNotClaimed()
        {
            // Act — dissolve declares a single slot; a second argument has no slot to land in.
            var ok = StyleArbitraryValueResolver.TryParse("filter-[dissolve:0.5:0.6]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AFloatArgumentForADeclaredColorSlot_When_Parsed_Then_TheTokenIsNotClaimed()
        {
            // Act — glow's first slot is a color; a bare number fails the slot's grammar rather than
            // silently binding a float where the shader expects a color.
            var ok = StyleArbitraryValueResolver.TryParse("filter-[glow:2]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AColorArgumentForADeclaredFloatSlot_When_Parsed_Then_TheTokenIsNotClaimed()
        {
            // Act — dissolve's only slot is a float; a color literal fails the slot's grammar.
            var ok = StyleArbitraryValueResolver.TryParse("filter-[dissolve:#ff0000]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AColorArgument_When_Applied_Then_TheParameterCarriesTheColor()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[glow:#ff0000]", StyleLayerPriority.Base);

            // Assert
            Assert.That(el.style.filter.value[0].GetParameter(0).colorValue,
                Is.EqualTo(new Color(1f, 0f, 0f, 1f)));
        }

        [Test]
        public void Given_MixedColorAndFloatArguments_When_Applied_Then_ParametersKeepTokenOrder()
        {
            // Arrange
            var el = new VisualElement();

            // Act
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[glow:#ff0000:2]", StyleLayerPriority.Base);

            // Assert
            var fn = el.style.filter.value[0];
            Assert.That((fn.parameterCount, fn.GetParameter(0).colorValue, fn.GetParameter(1).floatValue),
                Is.EqualTo((2, new Color(1f, 0f, 0f, 1f), 2f)));
        }

        [Test]
        public void Given_ANegativeFloatArgument_When_Applied_Then_TheParameterCarriesTheSignedValue()
        {
            // Arrange
            var el = new VisualElement();

            // Act — shader parameters are signed, so the argument grammar must accept a leading minus.
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[wave:-0.5]", StyleLayerPriority.Base);

            // Assert
            Assert.That(el.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(-0.5f));
        }

        [Test]
        public void Given_AnUnregisteredCustomFilterName_When_Parsed_Then_TheTokenIsNotClaimed()
        {
            // Act — an unknown name must fall through (an inert class), like any unrecognized bracket value.
            var ok = StyleArbitraryValueResolver.TryParse("filter-[never-registered:1]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AMalformedArgument_When_Parsed_Then_TheTokenIsNotClaimed()
        {
            // Act — a registered name with an unparseable argument rejects rather than half-applying.
            var ok = StyleArbitraryValueResolver.TryParse("filter-[dissolve:abc]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ADefinitionDeclaringMoreThanFourParameters_When_Registered_Then_TheRegistrationIsRejected()
        {
            // Arrange — a filter function holds at most 4 parameters (a fixed buffer that throws past its
            // cap), so a definition declaring more can never compose; rejecting it at registration keeps
            // the failure at the API boundary instead of a throw during style resolution.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[VelvetFilters\].*4"));
            var wide = CreateDefinition(new FilterParameter(0f), new FilterParameter(0f),
                new FilterParameter(0f), new FilterParameter(0f), new FilterParameter(0f));

            // Act
            VelvetFilters.Register("wide-x", wide);

            // Assert
            Assert.That(VelvetFilters.Unregister("wide-x"), Is.False);
        }

        #endregion

        #region Tokenization and reapply

        [Test]
        public void Given_AHoverPrefixedCustomFilterToken_When_VariantSplit_Then_ThePayloadKeepsTheBracketColon()
        {
            // Act
            var ok = StyleVariantClass.TryParse("hover:filter-[dissolve:0.4]", out var kind, out var payload);

            // Assert — the variant separator is the FIRST colon; the bracket colon stays in the payload.
            Assume.That(ok, Is.True, "Precondition: the hover-prefixed token is a variant token");
            Assert.That((kind, payload), Is.EqualTo((StyleVariantKind.Hover, "filter-[dissolve:0.4]")));
        }

        [Test]
        public void Given_APlainCustomFilterToken_When_VariantChecked_Then_TheBracketColonIsNotAVariantSeparator()
        {
            // Act — the colon inside the brackets must never make the token read as a variant.
            var isVariant = StyleVariantClass.IsVariant("filter-[dissolve:0.4]");

            // Assert
            Assert.That(isVariant, Is.False);
        }

        [Test]
        public void Given_AScrubbedElement_When_ReappliedFromItsClassList_Then_TheCustomFilterIsRestored()
        {
            // Arrange — simulate the pool-reset scrub: layers dropped, inline filter emptied.
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[fade:1]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ClearAll(el);
            el.style.filter = StyleKeyword.Null;
            el.style.filter.value?.Clear();

            // Act — the class-diff reapply path rebuilds inline values from the surviving class list.
            FiberNodePatcher.ReapplyArbitraryValues(el, new[] { "filter-[fade:1]", "w-[10px]" });

            // Assert
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].customDefinition), Is.EqualTo((1, _fadeDef)));
        }

        #endregion

        #region Lifecycle

        [Test]
        public void Given_ANameUnregisteredAfterApply_When_TheTokenIsCleared_Then_TheLayerStillLeavesTheFilter()
        {
            // Arrange — the clear must resolve the name syntactically: routing it through the registry
            // would make an unregister-while-applied leave the layer composed forever (a ghost filter).
            var el = new VisualElement();
            RegisterForTestRun("ghost-x", CreateDefinition(new FilterParameter(0f)));
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[ghost-x:0.5]", StyleLayerPriority.Base);
            VelvetFilters.Unregister("ghost-x");

            // Act — clear the token, then force a full recompose through an unrelated filter utility.
            StyleArbitraryValueResolver.ClearClassToken(el, "filter-[ghost-x:0.5]", StyleLayerPriority.Base);
            StyleArbitraryValueResolver.ApplyClassToken(el, "blur-[2px]", StyleLayerPriority.Base);

            // Assert — only the blur remains; the unregistered name's layer is gone.
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].type), Is.EqualTo((1, FilterFunctionType.Blur)));
        }

        [Test]
        public void Given_ANameUnregisteredWhileAHoverLayerIsActive_When_TheHoverTogglesOff_Then_TheBaseArgumentsAreRestored()
        {
            // Arrange — the variant off-toggle re-resolves its payload; that resolution must not depend on
            // the registry still knowing the name, or the hover layer survives the toggle.
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dissolve:0.3]", StyleLayerPriority.Base);
            StyleVariantPayload.Apply(el, new[] { "filter-[dissolve:0.9]" }, on: true, StyleLayerPriority.Hover);
            Assume.That(el.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(0.9f),
                "Precondition: the hover layer overrides the base while active");
            VelvetFilters.Unregister("dissolve");

            // Act
            StyleVariantPayload.Apply(el, new[] { "filter-[dissolve:0.9]" }, on: false, StyleLayerPriority.Hover);

            // Assert
            Assert.That(el.style.filter.value[0].GetParameter(0).floatValue, Is.EqualTo(0.3f));
        }

        [Test]
        public void Given_ADefinitionDestroyedAfterApply_When_TheFilterRecomposes_Then_TheDeadFunctionIsSkipped()
        {
            // Arrange — a destroyed definition compares equal to null (a dead asset), and the engine's
            // FilterFunction constructor throws on it; the compose must skip the dead layer instead.
            var el = new VisualElement();
            var doomed = CreateDefinition(new FilterParameter(0f));
            RegisterForTestRun("doomed-x", doomed);
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[doomed-x:0.5]", StyleLayerPriority.Base);
            Object.DestroyImmediate(doomed);

            // Act — an unrelated filter change forces the recompose that reads the cached layer.
            StyleArbitraryValueResolver.ApplyClassToken(el, "blur-[2px]", StyleLayerPriority.Base);

            // Assert — the recompose survives and carries only the live function.
            var f = el.style.filter.value;
            Assert.That((f.Count, f[0].type), Is.EqualTo((1, FilterFunctionType.Blur)));
        }

        #endregion

        #region Registration contract

        [Test]
        public void Given_ADuplicateRegistration_When_RegisteredAgain_Then_TheLaterDefinitionWinsWithAWarning()
        {
            // Arrange
            var first = CreateDefinition(new FilterParameter(0f));
            var second = CreateDefinition(new FilterParameter(0f));
            RegisterForTestRun("dup-name", first);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[VelvetFilters\].*already registered"));

            // Act
            VelvetFilters.Register("dup-name", second);

            // Assert — the overwrite is observable through resolution.
            var el = new VisualElement();
            StyleArbitraryValueResolver.ApplyClassToken(el, "filter-[dup-name:1]", StyleLayerPriority.Base);
            Assert.That(el.style.filter.value[0].customDefinition, Is.EqualTo(second));
        }

        [Test]
        public void Given_AReservedBuiltInName_When_Registered_Then_TheRegistrationIsRejected()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"\[VelvetFilters\].*reserved"));

            // Act
            VelvetFilters.Register("blur", CreateDefinition());

            // Assert — the reserved name never resolves as a custom filter.
            Assert.That(StyleArbitraryValueResolver.TryParse("filter-[blur:1]", out _), Is.False);
        }

        [Test]
        public void Given_ACaseVariantOfAReservedName_When_Registered_Then_TheRegistrationIsRejected()
        {
            // Arrange — the reservation is a contract about the FAMILY name, so a case-varied spelling
            // must not slip past it.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[VelvetFilters\].*reserved"));

            // Act
            VelvetFilters.Register("Blur", CreateDefinition());

            // Assert — nothing was stored under the case-varied name.
            Assert.That(VelvetFilters.Unregister("Blur"), Is.False);
        }

        [Test]
        public void Given_ANullDefinition_When_Registered_Then_TheRegistrationIsRejected()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"\[VelvetFilters\].*null definition"));

            // Act
            VelvetFilters.Register("null-def-test", null);

            // Assert — nothing was stored under the rejected name.
            Assert.That(VelvetFilters.Unregister("null-def-test"), Is.False);
        }

        [Test]
        public void Given_ANameWithABracketColonCharacter_When_Registered_Then_TheRegistrationIsRejected()
        {
            // Arrange — a ':' in the name would be unreachable from the token grammar (it reads as an argument
            // separator), so registration rejects it up front instead of storing a dead entry.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[VelvetFilters\].*must be non-empty"));

            // Act
            VelvetFilters.Register("bad:name", CreateDefinition());

            // Assert
            Assert.That(VelvetFilters.Unregister("bad:name"), Is.False);
        }

        [Test]
        public void Given_AReservedNameInUppercase_When_Registered_Then_TheWarningNamesTheCanonicalFamily()
        {
            // Arrange — the warning teaches which utility family owns the name, so it must spell the
            // canonical lowercase family, not echo the caller's casing back as a nonexistent "BLUR-*".
            LogAssert.Expect(LogType.Warning,
                "[VelvetFilters] Cannot register \"BLUR\": the name is reserved by the built-in blur-* utilities.");

            // Act
            VelvetFilters.Register("BLUR", CreateDefinition());

            // Assert — rejected: the reserved spelling never entered the registry.
            Assert.That(VelvetFilters.Unregister("BLUR"), Is.False);
        }

        [Test]
        public void Given_TheSameDefinitionReference_When_RegisteredAgain_Then_NoOverwriteWarningIsLogged()
        {
            // Arrange — re-registering the exact same name/definition pair is a true no-op, so the
            // "overwriting" warning would be noise pointing at a conflict that does not exist.
            RegisterForTestRun("pulse", _fadeDef);

            // Act
            VelvetFilters.Register("pulse", _fadeDef);
            LogAssert.NoUnexpectedReceived();

            // Assert — the registration is intact after the silent no-op.
            Assert.That(VelvetFilters.Unregister("pulse"), Is.True);
        }

        #endregion
    }
}
