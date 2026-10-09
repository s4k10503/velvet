using System.Globalization;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The nine-slice arbitrary values: <c>slice-[N]</c> and its <c>-x-</c> / <c>-y-</c> / <c>-t-</c> /
    /// <c>-r-</c> / <c>-b-</c> / <c>-l-</c> edges write the background image's <c>-unity-slice-*</c> insets,
    /// and <c>slice-scale-[N]</c> writes <c>-unity-slice-scale</c>, through the reconciler's class path. Every
    /// reading is of the inline style, so no panel is needed.
    /// </summary>
    [TestFixture]
    internal sealed class NineSliceArbitraryValueTests
    {
        [TestCase("slice-[12]", "12,12,12,12")]
        [TestCase("slice-[12px]", "12,12,12,12")]
        [TestCase("slice-x-[5]", "null,5,null,5")]
        [TestCase("slice-y-[5]", "5,null,5,null")]
        [TestCase("slice-t-[3]", "3,null,null,null")]
        [TestCase("slice-r-[3]", "null,3,null,null")]
        [TestCase("slice-b-[3]", "null,null,3,null")]
        [TestCase("slice-l-[3]", "null,null,null,3")]
        [TestCase("slice-[12_8]", "12,8,12,8")]
        [TestCase("slice-[12_8_4]", "12,8,4,8")]
        [TestCase("slice-[12_8_4_2]", "12,8,4,2")]
        [TestCase("slice-[12px_8]", "12,8,12,8")]
        [TestCase("slice-[12_8_fill]", "12,8,12,8")]
        [TestCase("slice-[fill_12_8]", "12,8,12,8")]
        public void Given_ASliceInsetClass_When_Mounted_Then_ItWritesTheEdgesItNames(string className, string expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), new VNode[] { V.Div(className) });

            // Assert — top, right, bottom, left.
            Assert.That(Insets(root.ElementAt(0)), Is.EqualTo(expected));
        }

        [TestCase("slice-scale-[1.5]", 1.5f)]
        [TestCase("slice-scale-[0]", 0f)]
        public void Given_ASliceScaleClass_When_Mounted_Then_ItWritesTheScale(string className, float expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), new VNode[] { V.Div(className) });

            // Assert — the keyword is what tells a written zero from the unset slot.
            var scale = root.ElementAt(0).style.unitySliceScale;
            Assert.That((scale.keyword, scale.value), Is.EqualTo((StyleKeyword.Undefined, expected)));
        }

        [TestCase("slice-[12.5]", "slice-[12]")]
        [TestCase("slice-[-3]", "slice-[3]")]
        [TestCase("-slice-[3]", "slice-[3]")]
        [TestCase("slice-t-[2em]", "slice-t-[2px]")]
        [TestCase("slice-scale-[-1]", "slice-scale-[1]")]
        [TestCase("-slice-scale-[1]", "slice-scale-[1]")]
        [TestCase("slice-scale-[2px]", "slice-scale-[2]")]
        [TestCase("slice-[1_2_3_4_5]", "slice-[1_2_3_4]")]
        [TestCase("slice-[12_]", "slice-[12_8]")]
        [TestCase("slice-[1__2]", "slice-[1_2]")]
        [TestCase("slice-[12_8.5]", "slice-[12_8]")]
        [TestCase("slice-x-[1_2]", "slice-x-[1]")]
        [TestCase("slice-[fill]", "slice-[12_fill]")]
        [TestCase("slice-[12_fill_8]", "slice-[12_8_fill]")]
        [TestCase("slice-[fill_12_fill]", "slice-[fill_12]")]
        [TestCase("slice-t-[3_fill]", "slice-t-[3]")]
        [TestCase("slice-[-5%]", "slice-[5%]")]
        [TestCase("slice-[5%px]", "slice-[5%]")]
        public void Given_AnInvalidSliceValue_When_Parsed_Then_ItIsDeclinedWhereItsValidNeighbourParses(
            string invalid, string valid)
        {
            // Arrange — the valid neighbour is what keeps a family that parses nothing from reading as one that
            // declines the invalid spelling.

            // Act
            var validParsed = StyleArbitraryValueResolver.TryParse(valid, out _);
            var invalidParsed = StyleArbitraryValueResolver.TryParse(invalid, out _);

            // Assert
            Assert.That((validParsed, invalidParsed), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_SliceShorthandsDifferingInTheLeftInsetAlone_When_TheirValueKeysAreCompared_Then_OnlyTheSameValueSharesAKey()
        {
            // Arrange — two spellings of one value share a key, so the key is read from the parsed edges rather
            // than from the class text.

            // Act
            var sameValue = FiberNodePatcher.ValueKey("slice-[1_2_3_4]") == FiberNodePatcher.ValueKey("slice-[1px_2_3_4]");
            var otherLeft = FiberNodePatcher.ValueKey("slice-[1_2_3_4]") == FiberNodePatcher.ValueKey("slice-[1_2_3_5]");

            // Assert
            Assert.That((sameValue, otherLeft), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnEdgeSliceClassOverASliceShorthand_When_TheEdgeClassIsRemoved_Then_TheShorthandHoldsThatEdgeAgain()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("slice-[12] slice-t-[3]") };
            var newTree = new VNode[] { V.Div("slice-[12]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            var before = Insets(root.ElementAt(0));

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((before, Insets(root.ElementAt(0))), Is.EqualTo(("3,12,12,12", "12,12,12,12")));
        }

        [Test]
        public void Given_SliceClasses_When_TheyAreRemoved_Then_EverySliceSlotIsUnsetAgain()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("slice-[12] slice-scale-[2]") };
            var newTree = new VNode[] { V.Div() };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            var before = Slots(root.ElementAt(0));

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((before, Slots(root.ElementAt(0))),
                Is.EqualTo(("12,12,12,12 2", "null,null,null,null null")));
        }

        [Test]
        public void Given_AHoverSliceOverABaseSlice_When_ThePointerEntersAndLeaves_Then_TheInsetsFollowTheVariant()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            reconciler.Reconcile(root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div("slice-[12] hover:slice-[4]") });
            var leaf = root.ElementAt(0);

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                leaf.SimulateEvent(over);
            }
            var hovered = Insets(leaf);
            using (var leave = PointerOutEvent.GetPooled())
            {
                leaf.SimulateEvent(leave);
            }

            // Assert
            Assert.That((hovered, Insets(leaf)), Is.EqualTo(("4,4,4,4", "12,12,12,12")));
        }

        private static string Insets(VisualElement element)
        {
            var style = element.style;
            return string.Join(",", Read(style.unitySliceTop), Read(style.unitySliceRight),
                Read(style.unitySliceBottom), Read(style.unitySliceLeft));
        }

        private static string Slots(VisualElement element)
        {
            var scale = element.style.unitySliceScale;
            return Insets(element) + " " + (scale.keyword == StyleKeyword.Null
                ? "null"
                : scale.value.ToString(CultureInfo.InvariantCulture));
        }

        private static string Read(StyleInt inset)
            => inset.keyword == StyleKeyword.Null ? "null" : inset.value.ToString(CultureInfo.InvariantCulture);
    }
}
