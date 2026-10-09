using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class StyleRuleOrderTests
    {
        [Test]
        public void Given_AnInvisibleAndABackgroundRuleOfOneVariant_When_Ordered_Then_InvisibleTakesTheEarlierPlace()
        {
            // Arrange — Tailwind 4.3.3 emits hover:invisible before hover:bg-red-500, its property order putting
            // visibility before background-color.
            var classNames = new[] { "hover:bg-red-500", "hover:invisible" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(classNames);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Given_ACachedClassBuffer_When_ItsTokensChange_Then_TheNewOrderIsUsed()
        {
            // Arrange
            var names = new[] { "hover:w-[10px]", "hover:w-[20px]" };
            StyleRuleOrder.OrdinalOf(names, 0);

            // Act
            names[0] = "hover:w-[30px]";
            var ordinal = StyleRuleOrder.OrdinalOf(names, 0);

            // Assert
            Assert.That(ordinal, Is.EqualTo(1));
        }

        [Test]
        public void Given_ACachedClassBuffer_When_ItsTokensStayTheSame_Then_TheCachedArrayIsRetained()
        {
            // Arrange
            var names = new[] { "hover:w-[20px]", "hover:w-[10px]" };
            StyleRuleOrder.OrdinalOf(names, 0);
            var cache = typeof(StyleRuleOrder).GetField("s_lastOrdinals", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = cache!.GetValue(null);

            // Act
            StyleRuleOrder.OrdinalOf(names, 1);

            // Assert
            Assert.That(cache.GetValue(null), Is.SameAs(previous));
        }

        [Test]
        public void Given_DuplicateCandidates_When_Ordered_Then_TheyShareOnePlace()
        {
            // Arrange
            var names = new[] { "hover:w-[20px]", "hover:w-[10px]", "hover:w-[20px]" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0, 1 }));
        }

        [Test]
        public void Given_ARepeatedHoverVariant_When_ComparedWithOneHover_Then_ThePropertyOrderDecides()
        {
            // Arrange
            var names = new[] { "hover:hover:h-[10px]", "hover:w-[20px]" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Given_TwoArbitraryStructuralSelectors_When_TheirPropertiesDisagree_Then_TheSelectorOrdersFirst()
        {
            // Arrange
            var names = new[] { "[&:nth-child(1)]:h-[10px]", "[&:first-child]:w-[20px]" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Given_AChildAndAnUnwrappedRule_When_TheirPropertiesDisagree_Then_TheChildVariantOrdersLater()
        {
            // Arrange
            var names = new[] { "[&>*]:h-[10px]", "w-[20px]" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AChildPayloadWhoseRuleMoves_When_DeclarationsChange_Then_ItTakesItsNewPlace(bool initiallyEmpty)
        {
            // Arrange
            var context = new ReconcilerContext();
            var container = new VisualElement();
            var child = new VisualElement();
            container.Add(child);
            var manipulator = new StyleChildVariantManipulator(context, new[] { "w-[10px]" },
                initiallyEmpty ? System.Array.Empty<int>() : new[] { 0 });
            container.AddManipulator(manipulator);
            StyleArbitraryValueResolver.TryParse("w-[20px]", out var competing);
            StyleArbitraryValueResolver.Apply(child, competing,
                StyleLayerPriority.WithRule(StyleLayerPriority.ChildVariant, 1));

            // Act
            manipulator.UpdatePayloads(new[] { "w-[10px]" }, new[] { 2 });
            var width = child.style.width.value.value;
            container.RemoveManipulator(manipulator);

            // Assert
            Assert.That(width, Is.EqualTo(10f));
        }

        [Test]
        public void Given_OneSelectorOnOpacityAndWidth_When_Ordered_Then_PropertyOrderPrecedesCandidateOrder()
        {
            // Arrange
            var names = new[] { "[&:first-child]:opacity-[0.5]", "[&:first-child]:w-[20px]" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Given_AnUnlistedFontPropertyAndWidth_When_Ordered_Then_TheListedPropertyComesFirst()
        {
            // Arrange
            var names = new[] { "hover:font-bold", "hover:w-[20px]" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AnUnchangedChildDeclarationArray_When_Updated_Then_NoResolverCallbackIsRepeated(bool empty)
        {
            // Arrange
            var context = new ReconcilerContext();
            var container = new VisualElement();
            container.Add(new VisualElement());
            var count = 0;
            context.VariantGatedReSync = _ => count++;
            var declarations = empty ? Array.Empty<int>() : new[] { 2 };
            var manipulator = new StyleChildVariantManipulator(context, new[] { "leading-[20px]" }, declarations);
            container.AddManipulator(manipulator);

            // Act
            manipulator.UpdatePayloads(new[] { "leading-[20px]" }, empty ? new int[0] : new[] { 2 });
            var callbacks = count;
            container.RemoveManipulator(manipulator);

            // Assert
            Assert.That(callbacks, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARetainedStackedHoverRule_When_ReopenedAtANewPlace_Then_ItBeatsTheIntermediateWriter()
        {
            // Arrange
            var context = new ReconcilerContext();
            var target = new VisualElement();
            var owner = new object();
            context.GateStackedVariant(target, owner, "hover:w-[10px]", true, StyleLayerPriority.Data, 0);
            context.GateStackedVariant(target, owner, "hover:w-[10px]", false, StyleLayerPriority.Data, 0);
            StyleArbitraryValueResolver.TryParse("w-[20px]", out var competing);
            StyleArbitraryValueResolver.Apply(target, competing,
                StyleLayerPriority.WithRule(StyleLayerPriority.Stack(StyleLayerPriority.Data, StyleLayerPriority.Hover), 1));

            // Act
            context.GateStackedVariant(target, owner, "hover:w-[10px]", true, StyleLayerPriority.Data, 2);
            using (var over = PointerOverEvent.GetPooled()) target.SimulateEvent(over);
            var width = target.style.width.value.value;
            context.DropStackedVariants(owner);

            // Assert
            Assert.That(width, Is.EqualTo(10f));
        }

        [Test]
        public void Given_TwoValuesForOneDataKey_When_PropertiesDisagree_Then_TheAttributeValueOrdersFirst()
        {
            // Arrange
            var names = new[] { "data-[state=a]:w-[20px]", "data-[state=z]:h-[10px]" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(names);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 0, 1 }));
        }

        // GREEN_ON_BASE(characterization): an unchanged tracked gate already avoids a redundant resolver callback.
        [TestCase("font-[Example]")]
        [TestCase("leading-[20px]")]
        public void Given_AnActiveFontOrLeadingPayload_When_TheSameGateIsAppliedAgain_Then_ItDoesNotResyncAgain(string token)
        {
            // Arrange
            var context = new ReconcilerContext();
            var target = new VisualElement();
            var count = 0;
            context.VariantGatedReSync = _ => count++;
            StyleVariantPayload.Apply(target, new[] { token }, true, StyleLayerPriority.Hover, context);

            // Act
            StyleVariantPayload.Apply(target, new[] { token }, true, StyleLayerPriority.Hover, context);

            // Assert
            Assert.That(count, Is.EqualTo(1));
        }
    }
}

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class RuleOrderCascadeBoundaryTests
    {
        private static object LayerMap(VisualElement element)
        {
            var projection = StyleArbitraryValueResolver.GetOrCreateProjection(element);
            return typeof(StyleClassProjection.Model).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(projection)!;
        }

        private static void Floors(VisualElement element, Dictionary<ArbitraryProperty, long> floors)
            => ((StyleClassProjection.ILayerHost)LayerMap(element)).ApplyFloors(element, floors);

        // GREEN_ON_BASE(refactor): floor changes use the shared ResolveMoved pass while retaining the existing floor replacement behavior.
        [Test]
        public void Given_AFlooredWidth_When_ItsFloorMovesAboveTheTopLayer_Then_TheInlineWidthClears()
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.TryParse("w-[20px]", out var style);
            StyleArbitraryValueResolver.Apply(element, style, 3);
            Floors(element, new Dictionary<ArbitraryProperty, long> { [ArbitraryProperty.Width] = 2 });

            // Act
            Floors(element, new Dictionary<ArbitraryProperty, long> { [ArbitraryProperty.Width] = 4 });

            // Assert
            Assert.That(element.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(refactor): floor removals use the shared ResolveMoved pass while retaining the existing restoration behavior.
        [Test]
        public void Given_TwoFlooredProperties_When_OneFloorIsRemoved_Then_ThatInlineValueReturns()
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.TryParse("w-[20px]", out var width);
            StyleArbitraryValueResolver.TryParse("h-[30px]", out var height);
            StyleArbitraryValueResolver.Apply(element, width, 3);
            StyleArbitraryValueResolver.Apply(element, height, 3);
            Floors(element, new Dictionary<ArbitraryProperty, long> { [ArbitraryProperty.Width] = 4, [ArbitraryProperty.Height] = 4 });

            // Act
            Floors(element, new Dictionary<ArbitraryProperty, long> { [ArbitraryProperty.Height] = 4 });

            // Assert
            Assert.That(element.style.width.value.value, Is.EqualTo(20f));
        }

        // GREEN_ON_BASE(characterization): inclusive floor masking predates rule ordinals; this pins its equality boundary.
        [Test]
        public void Given_AWidthLayer_When_ItsFloorEqualsItsKey_Then_TheInlineWidthClears()
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.TryParse("w-[20px]", out var style);
            StyleArbitraryValueResolver.Apply(element, style, 3);

            // Act
            Floors(element, new Dictionary<ArbitraryProperty, long> { [ArbitraryProperty.Width] = 3 });

            // Assert
            Assert.That(element.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_TwoUnrelatedInlineProperties_When_CollectingConnectedWriters_Then_NoWriterListIsBuilt()
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.TryParse("w-[20px]", out var width);
            StyleArbitraryValueResolver.TryParse("mt-[30px]", out var margin);
            StyleArbitraryValueResolver.Apply(element, width, 3);
            StyleArbitraryValueResolver.Apply(element, margin, 2);
            var map = LayerMap(element);
            var method = typeof(StyleArbitraryValueResolver).GetMethod("ConnectedWriters", BindingFlags.Static | BindingFlags.NonPublic);

            // Act
            var writers = method!.Invoke(null, new object[] { ArbitraryProperty.Width, map });

            // Assert
            Assert.That(writers, Is.Null);
        }

        private static object? s_sink;

        [Test]
        public void Given_AColourBesideUnrelatedLayers_When_CollectingConnectedWriters_Then_NothingIsAllocated()
        {
            // Arrange — a compiled call, since a reflected Invoke allocates its argument array; the canary
            // allocates one object, so a probe stuck at zero cannot pass.
            var element = new VisualElement();
            foreach (var token in new[] { "w-[20px]", "mt-[30px]", "bg-[#ff0000]" })
            {
                StyleArbitraryValueResolver.TryParse(token, out var style);
                StyleArbitraryValueResolver.Apply(element, style, 2);
            }
            var map = LayerMap(element);
            var method = typeof(StyleArbitraryValueResolver).GetMethod("ConnectedWriters", BindingFlags.Static | BindingFlags.NonPublic)!;
            var property = System.Linq.Expressions.Expression.Parameter(typeof(ArbitraryProperty));
            var host = System.Linq.Expressions.Expression.Parameter(typeof(object));
            var collect = System.Linq.Expressions.Expression.Lambda<Func<ArbitraryProperty, object, object>>(
                System.Linq.Expressions.Expression.Call(method, property,
                    System.Linq.Expressions.Expression.Convert(host, method.GetParameters()[1].ParameterType)),
                property, host).Compile();
            void Once() => collect(ArbitraryProperty.BackgroundColor, map);
            for (var i = 0; i < 64; i++)
            {
                Once();
            }

            // Act
            var canary = GCAllocationProbe.MedianBlocksDuring(() => s_sink = new object());
            var blocks = GCAllocationProbe.MedianBlocksDuring(Once);

            // Assert
            Assert.That((canary > 0, blocks), Is.EqualTo((true, 0)));
        }

        private static List<(long Key, ArbitraryStyle Style)>? CollectWriters(ArbitraryProperty property,
            params string[] tokens)
        {
            var type = typeof(StyleArbitraryValueResolver).GetNestedType("LayerMap", BindingFlags.NonPublic)!;
            var map = (Dictionary<ArbitraryProperty, SortedList<long, ArbitraryStyle>>)Activator.CreateInstance(type)!;
            for (var index = 0; index < tokens.Length; index++)
            {
                StyleArbitraryValueResolver.TryParse(tokens[index], out var style);
                if (!map.TryGetValue(style.Property, out var layers))
                {
                    layers = new SortedList<long, ArbitraryStyle>();
                    map.Add(style.Property, layers);
                }
                layers.Add(index + 1, style);
            }
            var method = typeof(StyleArbitraryValueResolver).GetMethod("ConnectedWriters", BindingFlags.Static | BindingFlags.NonPublic)!;
            return (List<(long Key, ArbitraryStyle Style)>?)method.Invoke(null, new object[] { property, map });
        }

        // GREEN_ON_BASE(characterization): the connected component already includes reverse-order transitive writers.
        [Test]
        public void Given_AReverseOrderedPaddingChain_When_CollectingConnectedWriters_Then_TheTransitiveWritersAreIncluded()
        {
            // Arrange
            var tokens = new[] { "pb-[40px]", "py-[30px]", "px-[10px]", "w-[50px]", "pt-[5px]" };

            // Act
            var writers = CollectWriters(ArbitraryProperty.PaddingTop, tokens);
            var roster = writers!.ConvertAll(writer => $"{writer.Key}:{writer.Style.Property}");
            roster.Sort(StringComparer.Ordinal);

            // Assert
            Assert.That(roster, Is.EqualTo(new[] { "1:PaddingBottom", "2:PaddingY", "5:PaddingTop" }));
        }

        // GREEN_ON_BASE(characterization): overlapping shorthands already contribute one writer per property.
        [Test]
        public void Given_ACycleOfOverlappingPaddingWriters_When_CollectingConnectedWriters_Then_EachWriterAppearsOnce()
        {
            // Arrange
            var tokens = new[] { "p-[20px]", "px-[10px]", "py-[30px]", "pt-[5px]", "w-[50px]" };

            // Act
            var writers = CollectWriters(ArbitraryProperty.PaddingTop, tokens);
            var roster = writers!.ConvertAll(writer => $"{writer.Key}:{writer.Style.Property}");
            roster.Sort(StringComparer.Ordinal);

            // Assert
            Assert.That(roster, Is.EqualTo(new[] { "1:Padding", "2:PaddingX", "3:PaddingY", "4:PaddingTop" }));
        }

        // GREEN_ON_BASE(characterization): collection already returns the highest layer's key and value.
        [Test]
        public void Given_MultipleLayersOfAConnectedWriter_When_CollectingConnectedWriters_Then_TheHighestLayerIsReturned()
        {
            // Arrange
            var tokens = new[] { "p-[20px]", "pt-[5px]", "p-[40px]" };

            // Act
            var writers = CollectWriters(ArbitraryProperty.PaddingTop, tokens);
            var roster = writers!.ConvertAll(writer => $"{writer.Key}:{writer.Style.Property}:{writer.Style.Value}");
            roster.Sort(StringComparer.Ordinal);

            // Assert
            Assert.That(roster, Is.EqualTo(new[] { "2:PaddingTop:5", "3:Padding:40" }));
        }

        // GREEN_ON_BASE(characterization): this adjacent-radius sequence already leaves the bottom writer on its corner.
        [Test]
        public void Given_EqualKeyAdjacentRadiusWriters_When_ResolvingAConnectedCorner_Then_TheLaterBottomWriterKeepsItsCorner()
        {
            // Arrange
            var element = new VisualElement();
            foreach (var token in new[] { "rounded-l-[40px]", "rounded-b-[30px]", "rounded-r-[20px]", "rounded-t-[10px]" })
            {
                StyleArbitraryValueResolver.TryParse(token, out var style);
                StyleArbitraryValueResolver.Apply(element, style, 1);
            }
            var map = LayerMap(element);
            var method = typeof(StyleArbitraryValueResolver).GetMethod("ResolveSharedLonghands", BindingFlags.Static | BindingFlags.NonPublic)!;

            // Act
            method.Invoke(null, new object[] { element, ArbitraryProperty.BorderTopRightRadius, map });

            // Assert
            Assert.That(element.style.borderBottomLeftRadius.value.value, Is.EqualTo(30f));
        }

        [Test]
        public void Given_AnEqualKeyRadiusRootInsertedFirst_When_ResolvingItsLonghands_Then_TheLaterWritersStayInPlace()
        {
            // Arrange
            var element = new VisualElement();
            foreach (var token in new[] { "rounded-t-[10px]", "rounded-l-[40px]", "rounded-r-[20px]" })
            {
                StyleArbitraryValueResolver.TryParse(token, out var style);
                StyleArbitraryValueResolver.Apply(element, style, 1);
            }
            StyleArbitraryValueResolver.TryParse("rounded-br-[7px]", out var higher);
            StyleArbitraryValueResolver.Apply(element, higher, 2);
            var before = new[] { element.style.borderTopLeftRadius.value.value,
                element.style.borderTopRightRadius.value.value, element.style.borderBottomRightRadius.value.value };
            var map = LayerMap(element);
            var method = typeof(StyleArbitraryValueResolver).GetMethod("ResolveSharedLonghands", BindingFlags.Static | BindingFlags.NonPublic)!;

            // Act
            method.Invoke(null, new object[] { element, ArbitraryProperty.BorderTopRadius, map });

            // Assert
            Assert.That(new[] { before[0], before[1], before[2], element.style.borderTopLeftRadius.value.value,
                element.style.borderTopRightRadius.value.value, element.style.borderBottomRightRadius.value.value },
                Is.EqualTo(new[] { 40f, 20f, 7f, 40f, 20f, 7f }));
        }

        [TestCase("w-[20px]", false)]
        [TestCase("mt-[30px]", true)]
        public void Given_AHigherWriter_When_CheckingMarginOwnership_Then_ItOutranksOnlyWhatItWrites(string token, bool expected)
        {
            // Arrange
            StyleArbitraryValueResolver.TryParse(token, out var style);
            var writers = new List<(long Key, ArbitraryStyle Style)> { (3, style) };
            var method = typeof(StyleArbitraryValueResolver).GetMethod("OutrankedOn", BindingFlags.Static | BindingFlags.NonPublic);

            // Act
            var outranked = method!.Invoke(null, new object[] { StyleLonghand.MarginTop, 2L, writers });

            // Assert
            Assert.That(outranked, Is.EqualTo(expected));
        }

        private static IEnumerable<StyleLonghandSet> ReachablePropertySets()
        {
            var rules = (StyleUtilityRule[])typeof(StyleUtilityProperties)
                .GetField("Rules", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            foreach (var rule in rules) yield return rule.Properties;
            foreach (ArbitraryProperty property in Enum.GetValues(typeof(ArbitraryProperty)))
                yield return StyleArbitraryLonghands.Of(property);
        }

        // GREEN_ON_BASE(construction): this pins the source sets that make line-height sorting redundant;
        // adding FontSize to `StyleArbitraryLonghands.Of(ArbitraryProperty.Padding)` invalidates it.
        [Test]
        public void Given_TheReachablePropertySets_When_ASetWritesFontSize_Then_ItWritesNoOtherLonghand()
        {
            // Arrange
            var expected = StyleLonghandSet.Of(StyleLonghand.FontSize);
            var violations = new List<StyleLonghandSet>();

            // Act
            foreach (var set in ReachablePropertySets())
                if (set.Contains(StyleLonghand.FontSize) && set != expected) violations.Add(set);

            // Assert
            Assert.That(violations, Is.Empty);
        }

        // GREEN_ON_BASE(construction): this pins the source sets that share the mapped background-position index;
        // adding BackgroundPositionX to `StyleArbitraryLonghands.Of(ArbitraryProperty.Padding)` invalidates it.
        [Test]
        public void Given_TheReachablePropertySets_When_ASetWritesBackgroundPosition_Then_ItWritesTheSharedObjectFitSet()
        {
            // Arrange
            var expected = StyleLonghandSet.Of(StyleLonghand.BackgroundSize)
                .Union(StyleLonghandSet.Of(StyleLonghand.BackgroundPositionX))
                .Union(StyleLonghandSet.Of(StyleLonghand.BackgroundPositionY));
            var violations = new List<StyleLonghandSet>();

            // Act
            foreach (var set in ReachablePropertySets())
                if ((set.Contains(StyleLonghand.BackgroundPositionX) || set.Contains(StyleLonghand.BackgroundPositionY))
                    && set != expected) violations.Add(set);

            // Assert
            Assert.That(violations, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): a later narrow margin already wins its side; this also drives the connected-writer loop.
        [Test]
        public void Given_ABroadAndANarrowMarginWriter_When_TheNarrowWriterHasTheLaterKey_Then_ItKeepsItsSide()
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.TryParse("m-[20px]", out var broad);
            StyleArbitraryValueResolver.TryParse("mt-[30px]", out var narrow);
            StyleArbitraryValueResolver.Apply(element, broad, 1);

            // Act
            StyleArbitraryValueResolver.Apply(element, narrow, 2);

            // Assert
            Assert.That(new[] { element.style.marginTop.value.value, element.style.marginLeft.value.value },
                Is.EqualTo(new[] { 30f, 20f }));
        }

        // GREEN_ON_BASE(construction): this pins the adjacent indexes used by the font-size-only equivalence;
        // moving line-height before font-size in `s_tailwindPropertyOrder` invalidates it.
        [Test]
        public void Given_ThePropertyOrder_When_FontSizeIsLocated_Then_LineHeightImmediatelyFollows()
        {
            // Arrange
            var indexes = (int[])typeof(StyleRuleOrder).GetField("s_tailwindIndex", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var longhands = (StyleLonghand[])Enum.GetValues(typeof(StyleLonghand));
            var fontSize = indexes[Array.IndexOf(longhands, StyleLonghand.FontSize)];

            // Act
            var lineHeight = (int)typeof(StyleRuleOrder).GetField("s_lineHeightIndex", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

            // Assert
            Assert.That(lineHeight, Is.EqualTo(fontSize + 1));
        }

        // GREEN_ON_BASE(construction): this pins the aliases under the duplicate-index equivalence;
        // mapping -unity-font-definition to font-size in `s_cssNames` invalidates it.
        [Test]
        public void Given_TheLonghandPropertyIndexes_When_IndexesAreShared_Then_TheBackgroundPositionAxesShareTheIndex()
        {
            // Arrange
            var indexes = (int[])typeof(StyleRuleOrder).GetField("s_tailwindIndex", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var longhands = (StyleLonghand[])Enum.GetValues(typeof(StyleLonghand));
            var groups = new Dictionary<int, List<StyleLonghand>>();
            var aliases = new List<string>();

            // Act
            for (var i = 0; i < indexes.Length; i++)
            {
                if (indexes[i] < 0) continue;
                if (!groups.TryGetValue(indexes[i], out var group)) groups[indexes[i]] = group = new List<StyleLonghand>();
                group.Add(longhands[i]);
            }
            foreach (var group in groups.Values)
                if (group.Count > 1) aliases.Add(string.Join(",", group));
            aliases.Sort(StringComparer.Ordinal);

            // Assert
            Assert.That(aliases, Is.EqualTo(new[] { "BackgroundPositionX,BackgroundPositionY" }));
        }
    }
}
