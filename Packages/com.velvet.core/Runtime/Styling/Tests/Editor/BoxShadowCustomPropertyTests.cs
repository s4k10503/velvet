using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What the box-shadow Tailwind composes from an element's ring, shadow and inset utilities draws where one of
    /// them reads a custom property, and which layers a colour class reaches. Every expected outcome is Chromium's
    /// computed box-shadow for the same classes under Tailwind v4.3.3: none where the declaration is invalid, and a
    /// layer Tailwind writes no colour variable into left as it was written. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class BoxShadowCustomPropertyTests
    {
        // The properties the cases read, declared on the element.
        private const string Declared = "[--x:0_0_5px_#ff0000] [--a:5px] [--b:#ff0000] [--w:3px] ";

        private static string Describe(ShadowSpec layer)
            => (float.IsNaN(layer.Color.r) ? "current" : ColorUtility.ToHtmlStringRGBA(layer.Color)) + "/"
                + layer.Blur.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // The layers of the box-shadow an element with classes paints, colour then blur, or "none".
        private static string Layers(string classes)
        {
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { V.Div(className: classes) });
            return scope.Reconciler.Context.ShadowBindings.TryGetValue(scope.Root[0], out var binding)
                ? string.Join(",", Array.ConvertAll(binding.Layers, Describe))
                : "none";
        }

        // The spread of the ring an element with classes paints, 0 where it paints none.
        private static float Ring(string classes)
        {
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: classes, name: "card") });
            var binding = RingOverlay.TryGet(scope.Root.Q<VisualElement>("card"));
            return binding == null ? 0f : binding.Spec.RingSpread;
        }

        private static string Palette(string name)
        {
            VelvetPalette.TryResolveColorToken(name, out var colour);
            return ColorUtility.ToHtmlStringRGBA(colour);
        }

        // GREEN_ON_BASE(characterization): the base draws the shadow, a bracket of any type but a length being the colour.
        [Test]
        public void Given_ARingBracketOfAnotherTypeReadingAnUndeclaredProperty_When_ABoxShadowIsBeside_Then_TheShadowIsDrawn()
        {
            // Act — Tailwind types the bracket as the ring's colour, which falls back to currentcolor.
            var layers = Layers("shadow-lg ring-[number:var(--w)]");

            // Assert
            Assert.That(layers, Is.Not.EqualTo("none"));
        }

        [Test]
        public void Given_ARejectedColourClassBesideALayerWhoseSourceReadsNoColour_When_TheLayerResolves_Then_TheRingIsDrawn()
        {
            // Act — the resolved layer reads no colour variable, which only its source says.
            var ring = Ring("[--a:5px] [--b:red] ring shadow-[#ggg] shadow-[0_0_var(--a)_var(--b)]");

            // Assert
            Assert.That(ring, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ARejectedColourClassBesideALayerWhoseSourceReadsNoColour_When_TheLayerResolves_Then_TheLayerIsDrawn()
        {
            // Act
            var layers = Layers("[--a:5px] [--b:#ff0000] ring shadow-[#ggg] shadow-[0_0_var(--a)_var(--b)]");

            // Assert
            Assert.That(layers, Is.EqualTo("FF0000FF/5"));
        }

        [TestCase("shadow-[var(--x)] shadow-blue-500")]
        [TestCase("shadow-(--x) shadow-blue-500")]
        [TestCase("shadow-[var(--x)]/50 shadow-blue-500")]
        [TestCase("shadow-[var(--x)] shadow-blue-500/50")]
        public void Given_AColourClassBesideALayerWhoseSourceReadsNoColour_When_TheLayerResolves_Then_ItKeepsItsOwnColour(
            string classes)
        {
            // Act
            var layers = Layers(Declared + classes);

            // Assert
            Assert.That(layers, Is.EqualTo("FF0000FF/5"));
        }

        [Test]
        public void Given_AColourClassBesideAListWhoseLayersAreEachWritten_When_TheLayersResolve_Then_OnlyTheLayerReadingColourTakesIt()
        {
            // Act — the first layer is a var() alone, the second names a colour of its own.
            var layers = Layers("[--l:0_0_3px_#00ff00] shadow-[var(--l),0_0_5px_#ff0000] shadow-blue-500");

            // Assert
            Assert.That(layers, Is.EqualTo("00FF00FF/3," + Palette("blue-500") + "/5"));
        }

        [Test]
        public void Given_AVariableHoldingAListOfLayers_When_ItsSourceReadsNoColour_Then_EveryLayerKeepsItsOwnColour()
        {
            // Act
            var layers = Layers("[--l:0_0_3px_#00ff00,0_0_5px_#ff0000] shadow-[var(--l)] shadow-blue-500");

            // Assert
            Assert.That(layers, Is.EqualTo("00FF00FF/3,FF0000FF/5"));
        }

        // GREEN_ON_BASE(characterization): the base takes the colour class into a layer whose source names the colour variable.
        [Test]
        public void Given_AColourClassBesideALayerWhoseSourceReadsTheColourVariable_When_TheLayerResolves_Then_ItTakesTheColour()
        {
            // Act — the colour is the one var(), which Tailwind replaces.
            var layers = Layers("[--c:#00ff00] shadow-[0_0_5px_var(--c)] shadow-blue-500");

            // Assert
            Assert.That(layers, Is.EqualTo(Palette("blue-500") + "/5"));
        }

        [Test]
        public void Given_AnInsetSizeAndAReaderTailwindEmitsAfterItBesideARing_When_TheReaderResolves_Then_ItsLayerHoldsTheShadow()
        {
            // Act — Tailwind emits .inset-shadow-2xs ahead of .inset-shadow-[…], and the ring, which it emits later still,
            // sits before both in the class list.
            var layers = Layers(Declared + "ring-2 inset-shadow-2xs inset-shadow-[var(--x)]");

            // Assert
            Assert.That(layers, Is.EqualTo("FF0000FF/5"));
        }

        [Test]
        public void Given_AnInsetReaderWhosePropertyHoldsNoShadow_When_BesideARing_Then_TheRingGoesWithIt()
        {
            // Act — the control gives the property a shadow.
            var ring = Ring(Declared + "ring-2 inset-shadow-[var(--x)]") == 2f
                ? Ring("[--x:0] ring-2 inset-shadow-[var(--x)]")
                : float.NaN;

            // Assert
            Assert.That(ring, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AReaderWithANumericModifier_When_ItsPropertyIsDeclared_Then_TheLayerIsDrawn()
        {
            // Act
            var layers = Layers(Declared + "shadow-[var(--x)]/50");

            // Assert
            Assert.That(layers, Is.EqualTo("FF0000FF/5"));
        }

        [Test]
        public void Given_AnImportantReaderAndAnImportantSizeTailwindEmitsBeforeIt_When_TheReaderResolves_Then_TheReaderHoldsTheShadow()
        {
            // Act — Tailwind emits !shadow-2xs ahead of !shadow-[…], so the reader wins.
            var layers = Layers(Declared + "!shadow-[var(--x)] !shadow-2xs");

            // Assert
            Assert.That(layers, Is.EqualTo("FF0000FF/5"));
        }

        // GREEN_ON_BASE(characterization): the base keeps an important reader over a plain size, as the cascade does.
        [Test]
        public void Given_AnImportantReaderAndAPlainSize_When_TheReaderResolves_Then_TheReaderHoldsTheShadow()
        {
            // Act
            var layers = Layers(Declared + "!shadow-[var(--x)] shadow-xl");

            // Assert
            Assert.That(layers, Is.EqualTo("FF0000FF/5"));
        }

        // A layer Tailwind writes no colour variable into: a length it cannot tell from a word, or a function it takes for
        // a colour candidate beside another.
        [TestCase("shadow-[0_0_hypot(1px)_hypot(2px)]")]
        [TestCase("shadow-[+1px_+1px]")]
        [TestCase("shadow-[round(1px,1px)_round(2px,1px)]")]
        [TestCase("inset-shadow-[+1px_+1px]")]
        [TestCase("shadow-[+1px_+1px] shadow-red-500")]
        [TestCase("shadow-[round(1px,1px)_round(2px,1px)] shadow-red-500")]
        [TestCase("shadow-[0_0_hypot(1px)_hypot(2px)] shadow-red-500")]
        [TestCase("inset-shadow-[+1px_+1px] inset-shadow-red-500")]
        public void Given_ALayerTailwindWritesNoColourVariableInto_When_Extracted_Then_ItIsDrawnInCurrentColour(string classes)
        {
            // Act
            var found = StyleShadowClass.TryExtract(StyleSpacingPrecedence.Normalize(classes.Split(' ')), out var layers);

            // Assert
            Assert.That(found && float.IsNaN(layers[0].Color.r), Is.True);
        }

        [Test]
        public void Given_ALayerNamingNoColour_When_Extracted_Then_ItIsDrawnInCurrentColour()
        {
            // Act — Tailwind writes var(--tw-shadow-color, currentcolor) into it.
            var found = StyleShadowClass.TryExtract(new[] { "shadow-[1px_1px]" }, out var layers);

            // Assert
            Assert.That(found && float.IsNaN(layers[0].Color.r), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base paints a colour class into a layer whose offsets are lengths.
        [TestCase("shadow-[1px_1px] shadow-red-500")]
        [TestCase("shadow-[0_0_calc(1px)_calc(2px)] shadow-red-500")]
        public void Given_ALayerTailwindWritesTheColourVariableInto_When_ABracketColourIsBeside_Then_ItTakesTheColour(string classes)
        {
            // Act
            var found = StyleShadowClass.TryExtract(classes.Split(' '), out var layers);

            // Assert
            Assert.That(found ? ColorUtility.ToHtmlStringRGBA(layers[0].Color) : "none", Is.EqualTo(Palette("red-500")));
        }

        // How far the payload a reader resolves to stands past the literal on the class source the passes read, the payload
        // being the family's token that is neither the literal nor the reader's own; a distance no placement gives where
        // the family holds none.
        private static int PayloadPast(string classes, string prefix, string literal, string reader)
        {
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { V.Div(className: classes) });
            var composed = scope.Reconciler.Context.VariantGateClasses[scope.Root[0]].Resolved ?? Array.Empty<string>();
            var payload = Array.FindIndex(composed,
                token => token.StartsWith(prefix, StringComparison.Ordinal) && token != literal && token != reader);
            return payload < 0 ? int.MinValue : payload - Array.IndexOf(composed, literal);
        }

        // Chromium computes the reader's value where both are important and Tailwind emits the reader after the literal:
        // gap 8px, skew 10deg, weight 700, a 5px divider.
        [TestCase("[--g:8px] !gap-4 !gap-[var(--g)]", "!gap-", "!gap-4", "!gap-[var(--g)]")]
        [TestCase("[--s:10deg] !skew-x-6 !skew-x-[var(--s)]", "!skew-x-", "!skew-x-6", "!skew-x-[var(--s)]")]
        [TestCase("[--w:700] !font-[300] !font-[var(--w)]", "!font-", "!font-[300]", "!font-[var(--w)]")]
        [TestCase("[--d:5px] !divide-x-2 !divide-x-[var(--d)]", "!divide-x-", "!divide-x-2", "!divide-x-[var(--d)]")]
        public void Given_AnImportantReaderTailwindEmitsAfterAnImportantLiteral_When_ItResolves_Then_ItsPayloadFollowsTheLiteral(
            string classes, string prefix, string literal, string reader)
        {
            // Act
            var past = PayloadPast(classes, prefix, literal, reader);

            // Assert
            Assert.That(past, Is.GreaterThan(0));
        }

        // Tailwind 4.3.3 writes a rule for an opacity modifier after a bracket of each of these, measured per class.
        [TestCase("shadow-[var(--x)]/50")]
        [TestCase("inset-shadow-[var(--x)]/50")]
        [TestCase("drop-shadow-[var(--x)]/50")]
        [TestCase("shadow-[length:var(--x)]/50")]
        [TestCase("bg-[var(--x)]/50")]
        [TestCase("text-[var(--x)]/50")]
        [TestCase("border-[var(--x)]/50")]
        [TestCase("ring-[var(--x)]/50")]
        [TestCase("outline-[var(--x)]/50")]
        [TestCase("ring-offset-[var(--x)]/50")]
        [TestCase("inset-ring-[var(--x)]/50")]
        [TestCase("from-[var(--x)]/50")]
        [TestCase("via-[var(--x)]/50")]
        [TestCase("to-[var(--x)]/50")]
        [TestCase("border-t-[var(--x)]/50")]
        [TestCase("caret-[var(--x)]/50")]
        [TestCase("divide-[var(--x)]/50")]
        public void Given_ANumericModifierAfterABracketOfAFamilyTailwindWritesARuleFor_When_Read_Then_ItIsAReader(string cls)
        {
            // Act
            var reader = StyleCustomPropertyClass.Consumer(cls);

            // Assert
            Assert.That(reader, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base reads none of these, which Tailwind writes no rule for.
        [TestCase("gap-[var(--g)]/50")]
        [TestCase("skew-x-[var(--s)]/50")]
        [TestCase("ring-[length:var(--w)]/50")]
        [TestCase("w-[var(--w)]/50")]
        public void Given_ANumericModifierAfterABracketOfAUtilityWithNoRuleForOne_When_Read_Then_ItIsNoReader(string cls)
        {
            // Act
            var reader = StyleCustomPropertyClass.Consumer(cls);

            // Assert
            Assert.That(reader, Is.Null);
        }

        // Tailwind mixes the value of a bracket not typed as a colour into one when it has an opacity modifier, which the
        // browser finds invalid where it is used (Chromium computes no box-shadow for `ring-2 ring-[number:5px]/50`).
        [TestCase("ring-2 ring-[number:5px]/50")]
        [TestCase("ring ring-offset-2 ring-offset-[number:5px]/50")]
        [TestCase("shadow-lg inset-ring inset-ring-[percentage:5%]/50")]
        public void Given_ARingBracketOfAnotherTypeWithAModifier_When_Extracted_Then_TheBoxShadowIsDiscarded(string classes)
        {
            // Act
            var drawn = StyleShadowClass.TryExtract(StyleSpacingPrecedence.Normalize(classes.Split(' ')), out _)
                || StyleRingClass.TryExtract(StyleSpacingPrecedence.Normalize(classes.Split(' ')), out _);

            // Assert
            Assert.That(drawn, Is.False);
        }

        [TestCase("ring-[number:var(--x)]/50")]
        [TestCase("ring-offset-[number:var(--x)]/50")]
        [TestCase("inset-ring-[percentage:var(--x)]/50")]
        public void Given_AModifierOnARingBracketOfAnotherTypeReadingAProperty_When_Read_Then_ItIsAReader(string cls)
        {
            // Act
            var reader = StyleCustomPropertyClass.Consumer(cls);

            // Assert
            Assert.That(reader, Is.Not.Null);
        }

        [Test]
        public void Given_ARingBracketOfAnotherTypeWithAModifierReadingADeclaredProperty_When_Mounted_Then_NoRingIsDrawn()
        {
            // Act
            var ring = Ring("[--x:5px] ring-2 ring-[number:var(--x)]/50");

            // Assert
            Assert.That(ring, Is.EqualTo(0f));
        }

        // Tailwind mixes the value of a colour utility's bracket typed as something else into a colour when it has an
        // opacity modifier, which the browser finds invalid: the property computes as unset.
        [TestCase("caret-[number:5px]/50")]
        [TestCase("divide-[number:5px]/50")]
        [TestCase("bg-[number:5]/50")]
        [TestCase("border-[number:5]/50")]
        [TestCase("border-t-[number:5]/50")]
        [TestCase("outline-[line-width:5px]/50")]
        [TestCase("from-[number:5]/50")]
        [TestCase("text-[line-width:5px]/50")]
        public void Given_AColourUtilitysBracketOfAnotherTypeWithAModifier_When_Read_Then_ItIsAReader(string cls)
        {
            // Act
            var reader = StyleCustomPropertyClass.Consumer(cls);

            // Assert
            Assert.That(reader, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base reads none of these, whose hint is the utility's own width or size.
        [TestCase("border-[length:5px]/50")]
        [TestCase("outline-[length:5px]/50")]
        [TestCase("text-[length:5px]/6")]
        [TestCase("from-[length:5px]/50")]
        public void Given_AColourUtilitysBracketTypedAsItsWidthOrSizeWithAModifier_When_Read_Then_ItIsNoReader(string cls)
        {
            // Act
            var reader = StyleCustomPropertyClass.Consumer(cls);

            // Assert
            Assert.That(reader, Is.Null);
        }

        // The class source the passes read for an element with classes, once its readers resolve.
        private static string[] ComposedFor(string classes)
        {
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { V.Div(className: classes) });
            return scope.Reconciler.Context.VariantGateClasses[scope.Root[0]].Resolved ?? Array.Empty<string>();
        }

        // Chromium computes the divider's colour as currentcolor, the property's initial value, for
        // `divide-x-2 divide-[#f00] divide-[number:var(--x)]/50`, and the parent's caret colour for the caret.
        [Test]
        public void Given_ADivideBracketOfAnotherTypeWithAModifierAfterAColour_When_ItsReaderIsRead_Then_TheDividerHasNoColour()
        {
            // Act
            StyleDivideClass.TryExtract(ComposedFor("divide-x-2 divide-[#ff0000] divide-[number:5px]/50"), out var spec);

            // Assert
            Assert.That(spec.HasColor, Is.False);
        }

        [Test]
        public void Given_ACaretBracketOfAnotherTypeWithAModifierAfterAColour_When_ItsReaderIsRead_Then_TheCaretInheritsIt()
        {
            // Act
            var tokens = ComposedFor("caret-[#ff0000] caret-[number:5px]/50");

            // Assert
            Assert.That(tokens[tokens.Length - 1], Is.EqualTo("caret-inherit"));
        }
    }
}
