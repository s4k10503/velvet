using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the class readers that pass over a class before copying or parsing it still answer for the classes
    /// they have to read: a bang, a variant layer, a custom-property reader, a family's own class among others. GWT, one
    /// assert per case.
    /// </summary>
    internal sealed class ClassPrefilterReadTests
    {
        private delegate string[] ExtractVariant(string[] classNames, StyleVariantKind kind, out int[] declarations);

        private Reconciler _reconciler;

        [SetUp]
        public void SetUp() => _reconciler = new Reconciler();

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private static T Patcher<T>(string name) where T : Delegate
            => (T)Delegate.CreateDelegate(typeof(T),
                typeof(FiberNodePatcher).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // Whether TrackThemed, run on a patcher of this fixture's reconciler, leaves the element tracked.
        private bool TrackedAfterTracking(string className)
        {
            var element = new VisualElement();
            var track = (Action<VisualElement, string[]>)Delegate.CreateDelegate(typeof(Action<VisualElement, string[]>),
                new FiberNodePatcher(_reconciler.Context),
                typeof(FiberNodePatcher).GetMethod("TrackThemed", BindingFlags.NonPublic | BindingFlags.Instance));
            track(element, new[] { className });
            var table = typeof(StyleThemeTokens).GetField("s_trackedBy", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            var args = new object[] { element, null };
            return (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, args);
        }

        // GREEN_ON_BASE(characterization): the base reads a clip followed by a class of another family as wanting the clip.
        [Test]
        public void Given_AClipFollowedByAClassOfAnotherFamily_When_Extracted_Then_TheClipIsWanted()
        {
            // Act
            var found = StyleClipPathClass.TryExtract(new[] { "clip-path-[circle(50%)]", "flex" }, out _);

            // Assert
            Assert.That(found, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base turns away classes of no shadow family, an important one and an inset-* one among them.
        [Test]
        public void Given_ClassesOfNoShadowFamily_When_Gated_Then_HasShadowClassTurnsThemAway()
        {
            // Act
            var has = StyleShadowClass.HasShadowClass(new[] { "flex", "!bg-red-500", "inset-0" });

            // Assert
            Assert.That(has, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base lets an important shadow class through the gate.
        [TestCase("!shadow-lg")]
        [TestCase("!inset-shadow-sm")]
        public void Given_AnImportantShadowFamilyClass_When_Gated_Then_HasShadowClassLetsItThrough(string cls)
        {
            // Act
            var has = StyleShadowClass.HasShadowClass(new[] { cls });

            // Assert
            Assert.That(has, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base hands each layer its own colour back once shadow-initial is read after a colour.
        [Test]
        public void Given_ShadowInitialReadAfterAColour_When_Extracted_Then_TheLayersKeepTheirOwnColour()
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { "shadow-md", "shadow-red-500", "shadow-initial" }, out var spec);

            // Assert
            Assert.That(found ? string.Join(", ", spec.Select(layer => ColorUtility.ToHtmlStringRGBA(layer.Color))) : "none",
                Is.EqualTo("0000001A, 0000001A"));
        }

        // GREEN_ON_BASE(characterization): the base draws every layer in the element's text colour for shadow-[currentcolor].
        [Test]
        public void Given_AShadowColourOfCurrentColorInABracket_When_Extracted_Then_EveryLayerTakesTheTextColour()
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { "shadow-md", "shadow-[currentcolor]" }, out var spec);

            // Assert
            Assert.That(found ? string.Join(", ", spec.Select(layer => float.IsNaN(layer.Color.r) ? "current" : "other")) : "none",
                Is.EqualTo("current, current"));
        }

        // GREEN_ON_BASE(characterization): the base finds no palette colour in a shadow suffix of any of these lengths.
        [Test]
        public void Given_ShadowClassesNamingUnknownColoursOfEveryLength_When_Extracted_Then_NoneDrawsALayer()
        {
            // Act
            var drawing = Enumerable.Range(1, 32).Count(n => StyleShadowClass.TryExtract(new[] { "shadow-" + new string('q', n) }, out _));

            // Assert
            Assert.That(drawing, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base records a parent's changed shadow colour for the child inheriting it.
        [Test]
        public void Given_AParentWhoseShadowColourChanges_When_AChildInheritsIt_Then_ItIsTheNewColour()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();
            parent.Add(child);
            BoxShadowSilhouette.RecordDeclaredColours(parent, new[] { "shadow-lg", "shadow-red-500" });
            BoxShadowSilhouette.RecordDeclaredColours(parent, new[] { "shadow-lg", "shadow-blue-500" });
            var inherited = typeof(BoxShadowSilhouette).GetMethod("InheritedColour", BindingFlags.NonPublic | BindingFlags.Static);

            // Act
            var colour = (Color?)inherited.Invoke(null, new object[] { child, ShadowColourFamily.Shadow });

            // Assert
            Assert.That(colour is { } c ? ColorUtility.ToHtmlStringRGBA(c) : "none", Is.EqualTo("2B7FFFFF"));
        }

        // GREEN_ON_BASE(characterization): the base answers whether each class may declare variant state as below.
        [TestCase("hover:bg-red-500", true)]
        [TestCase("bg-red-500", false)]
        public void Given_AClassOutsideAnyThemeScope_When_AskedWhetherItMayDeclareVariantState_Then_ItIsAnsweredAsBelow(
            string cls, bool expected)
        {
            // Arrange
            var mayDeclare = Patcher<Func<string[], bool>>("MayDeclareVariantState");

            // Act
            var may = mayDeclare(new[] { cls });

            // Assert
            Assert.That(may, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base tracks a class shaped like one a theme variable makes, important or not, and not flex.
        [TestCase("bg-brandtest", true)]
        [TestCase("!bg-brandtest", true)]
        [TestCase("flex", false)]
        public void Given_AClassOutsideAnyThemeScope_When_TheElementIsTrackedForThemeClasses_Then_ItIsTrackedAsBelow(
            string cls, bool expected)
        {
            // Act
            var tracked = TrackedAfterTracking(cls);

            // Assert
            Assert.That(tracked, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base reads a custom-property reader with no variant layer as a gate payload.
        [TestCase("bg-(--brand)")]
        [TestCase("[width:var(--w)]")]
        public void Given_ACustomPropertyReaderWithNoVariant_When_AskedWhetherItDeclaresAGatePayload_Then_ItDoes(string cls)
        {
            // Act
            var declares = StyleVariantPayload.DeclaresGatePayload(new[] { cls });

            // Assert
            Assert.That(declares, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads a stacked hover under dark: as an element-local inner.
        [Test]
        public void Given_AStackedHoverUnderDark_When_AskedWhetherItStacksAnElementLocalInner_Then_ItDoes()
        {
            // Arrange
            var stacks = Patcher<Func<string[], bool>>("StacksElementLocalInner");

            // Act
            var stacked = stacks(new[] { "sm:p-2", "dark:hover:bg-red-500" });

            // Assert
            Assert.That(stacked, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base extracts the payloads of the kind asked for, in the order written.
        [Test]
        public void Given_VariantClassesOfSeveralKinds_When_TheHoverPayloadsAreExtracted_Then_TheyAreTheHoverOnes()
        {
            // Arrange
            var extract = Patcher<ExtractVariant>("ExtractVariant");

            // Act
            var payloads = extract(new[] { "hover:bg-red-500", "focus:p-2", "hover:p-4" }, StyleVariantKind.Hover, out _);

            // Assert
            Assert.That(string.Join(" ", payloads), Is.EqualTo("bg-red-500 p-4"));
        }

        // GREEN_ON_BASE(characterization): the base cuts the payload after the prefix of each of these variant forms.
        [TestCase("@md:p-2")]
        [TestCase("max-md:p-2")]
        [TestCase("max-[600px]:p-2")]
        [TestCase("group-hover/sidebar:p-2")]
        public void Given_AVariantOfAFormNoKeywordSpellsAlone_When_Parsed_Then_ItsPayloadIsWhatFollowsThePrefix(string cls)
        {
            // Act
            var parsed = StyleVariantClass.TryParse(cls, out _, out _, out var payload);

            // Assert
            Assert.That(parsed ? payload : "not a variant", Is.EqualTo("p-2"));
        }

        // GREEN_ON_BASE(characterization): the base builds one binding for a container query beside a hover class.
        [Test]
        public void Given_AContainerQueryBesideAHoverClass_When_TheContainerBindingsAreBuilt_Then_OneIsBuilt()
        {
            // Arrange
            var build = Patcher<Func<string[], ContainerBinding[]>>("BuildContainerBindings");

            // Act
            var bindings = build(new[] { "@md:p-2", "hover:bg-red-500" });

            // Assert
            Assert.That(bindings.Length, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base builds one config for a group-hover class beside a hover class, holding its payload.
        [Test]
        public void Given_AGroupHoverBesideAHoverClass_When_TheRelationalConfigsAreBuilt_Then_OneHoldsTheGroupHoverPayload()
        {
            // Arrange
            var build = Patcher<Func<string[], List<RelationalBindingConfig>>>("BuildRelationalConfigs");

            // Act
            var configs = build(new[] { "group-hover:bg-red-500", "hover:p-2" });

            // Assert
            Assert.That(configs == null ? "none" : string.Join(" | ", configs.Select(c => string.Join(" ", c.Payloads.Hover))),
                Is.EqualTo("bg-red-500"));
        }
    }
}
