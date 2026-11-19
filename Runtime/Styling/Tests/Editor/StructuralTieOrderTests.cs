using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where the structural and child-combinator variants tie against their neighbours on the same
    /// property, as Tailwind's generated CSS resolves it — specificity first, then emission order: among the
    /// pseudo-class variants group/peer come first, then first/last/only/odd/even, then after data the functional
    /// nth-*, and an arbitrary [&amp;:…]: selector after all of them; [&amp;>*]: carries one class on the child,
    /// so it beats the child's md:/dark: and loses to its pseudo-class variants.
    /// </summary>
    [TestFixture]
    internal sealed class StructuralTieOrderTests : PanelTestBase
    {
        private bool _darkBefore;

        public override void SetUp()
        {
            base.SetUp();
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        public override void TearDown()
        {
            base.TearDown();
            s_setFirstClass = default;
            VelvetTheme.IsDark = _darkBefore;
        }

        private static StateUpdater<string> s_setFirstClass;

        [Component]
        private static VNode RenderRows()
        {
            var (className, setClass) = Hooks.UseState("first:bg-cold");
            s_setFirstClass = setClass;
            return V.Div(children: new VNode?[]
            {
                V.Div(name: "first", className: className),
                V.Div(name: "second"),
            });
        }

        private (VisualElement Group, VisualElement Leaf) MountFirstChild(string leafClassName,
            IReadOnlyDictionary<string, string>? data = null)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "group", className: "group", children: new VNode?[]
                {
                    V.Div(name: "leaf", className: leafClassName, data: data),
                    V.Div(name: "sibling"),
                }));
            var root = _window.rootVisualElement;
            return (root.Q<VisualElement>("group"), root.Q<VisualElement>("leaf"));
        }

        private (VisualElement First, VisualElement Second) MountRows(string rowClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(children: new VNode?[]
                {
                    V.Div(name: "first", className: rowClassName),
                    V.Div(name: "second", className: rowClassName),
                }));
            var root = _window.rootVisualElement;
            return (root.Q<VisualElement>("first"), root.Q<VisualElement>("second"));
        }

        private (VisualElement Parent, VisualElement Leaf) MountUnderChildVariant(string parentClassName,
            string leafClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "parent", className: parentClassName, children: new VNode?[]
                {
                    V.Div(name: "leaf", className: leafClassName),
                }));
            var root = _window.rootVisualElement;
            return (root.Q<VisualElement>("parent"), root.Q<VisualElement>("leaf"));
        }

        [Test]
        public void Given_GroupHoverAndFirstWidths_When_TheGroupIsHovered_Then_TheFirstWidthWins()
        {
            // Arrange
            var (group, leaf) = MountFirstChild("group-hover:w-[10px] first:w-[20px]");

            // Act — hovered after the first width landed, so a shared layer would leave the group-hover width.
            using (var over = PointerOverEvent.GetPooled()) group.SimulateEvent(over);

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_AGroupHoverHoverStackBesidePlainHover_When_BothHold_Then_TheStacksClassAloneRemains()
        {
            // Arrange — the stack outranks plain hover: though the plain one is written later — its parts add a
            // class, and its variant set holds hover's bit and one more — and the projection takes the losing
            // class off the element.
            var (group, leaf) = MountFirstChild("group-hover:hover:opacity-75 hover:opacity-50");

            // Act
            using (var overGroup = PointerOverEvent.GetPooled()) group.SimulateEvent(overGroup);
            using (var overLeaf = PointerOverEvent.GetPooled()) leaf.SimulateEvent(overLeaf);

            // Assert
            Assert.That((leaf.ClassListContains("opacity-75"), leaf.ClassListContains("opacity-50")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_Nth1AndNth2Backgrounds_When_TwoRowsCarryBoth_Then_EachRowTakesItsOwn()
        {
            // Arrange / Act — on the first row nth-2: evaluates off after nth-1: turned on, on one rank.
            var rows = MountRows("nth-1:bg-[#ff0000] nth-2:bg-[#00ff00]");

            // Assert
            Assert.That((rows.First.style.backgroundColor.value, rows.Second.style.backgroundColor.value),
                Is.EqualTo((Color.red, Color.green)));
        }

        [Test]
        public void Given_OddAndEvenBackgrounds_When_TwoRowsCarryBoth_Then_EachRowTakesItsOwn()
        {
            // Arrange / Act
            var rows = MountRows("odd:bg-[#ff0000] even:bg-[#00ff00]");

            // Assert
            Assert.That((rows.First.style.backgroundColor.value, rows.Second.style.backgroundColor.value),
                Is.EqualTo((Color.red, Color.green)));
        }

        [Test]
        public void Given_DataAndNthWidths_When_BothHold_Then_TheNthWidthWins()
        {
            // Arrange / Act
            var (_, leaf) = MountFirstChild("data-[state=open]:w-[10px] nth-1:w-[20px]",
                data: new Dictionary<string, string> { ["state"] = "open" });

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_HoverAndArbitraryNthChildWidths_When_TheElementIsHovered_Then_TheArbitraryWidthWins()
        {
            // Arrange
            var (_, leaf) = MountFirstChild("[&:nth-child(1)]:w-[20px] hover:w-[10px]");

            // Act — hovered after the arbitrary width landed, so a shared layer would leave the hover width.
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_AChildDarkWidthUnderAParentChildVariant_When_DarkTurnsOn_Then_TheParentWidthWins()
        {
            // Arrange
            var (_, leaf) = MountUnderChildVariant("[&>*]:w-[20px]", "dark:w-[10px]");

            // Act — dark lands after the parent's payload, so a shared layer would leave the dark width.
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        // GREEN_ON_BASE(characterization): the child's own hover: already outranks the container's [&>*]: on the
        // base; the ladder rework keeps it, a pseudo-class outweighing `.x > *`.
        [Test]
        public void Given_AChildHoverWidthUnderAParentChildVariant_When_TheChildIsHovered_Then_TheChildWidthWins()
        {
            // Arrange
            var (_, leaf) = MountUnderChildVariant("[&>*]:w-[20px]", "hover:w-[10px]");

            // Act
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(10f));
        }

        [Test]
        public void Given_TwoNthLastPayloads_When_TheElementIsSecondFromTheEnd_Then_OnlyTheMatchingOneApplies()
        {
            // Arrange / Act — the leaf is the first of two, so nth-last-2 names it and nth-last-1 does not.
            var (_, leaf) = MountFirstChild("nth-last-2:w-[20px] nth-last-1:h-[20px]");

            // Assert
            Assert.That((leaf.style.width.value.value, leaf.style.height.keyword),
                Is.EqualTo((20f, StyleKeyword.Null)));
        }

        // GREEN_ON_BASE(characterization): a render swapping a first: payload takes the old one off, as the base
        // already does.
        [Test]
        public void Given_AFirstPayload_When_ARenderSwapsItsClass_Then_TheOldClassComesOff()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderRows));
            var first = _window.rootVisualElement.Q<VisualElement>("first");

            // Act
            s_setFirstClass.Invoke("first:bg-hot");
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert
            Assert.That((first.ClassListContains("bg-cold"), first.ClassListContains("bg-hot")), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnNthValueWithALeadingZero_When_TheFirstRowCarriesIt_Then_ItIsRefused()
        {
            // Act — nth-1: beside it shows the row is one an nth-1 rule matches.
            var (first, _) = MountRows("nth-1:bg-cold nth-01:bg-hot");

            // Assert
            Assert.That((first.ClassListContains("bg-cold"), first.ClassListContains("bg-hot")), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): a child nothing presses stays off under [&>*]:active:, as on the base.
        [Test]
        public void Given_AChildActiveVariant_When_TheChildIsMountedUnpressed_Then_OnlyTheUngatedChildVariantApplies()
        {
            // Act — [&>*]:bg-cold beside it shows the child variant reached the child at all.
            var (_, leaf) = MountUnderChildVariant("[&>*]:bg-cold [&>*]:active:bg-hot", "");

            // Assert
            Assert.That((leaf.ClassListContains("bg-cold"), leaf.ClassListContains("bg-hot")), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base keys every [&>*]: payload alike, so both classes stay
        // and the stylesheet puts h-8 last; ordering the two by candidate alone would let size-4 cover h-8.
        [Test]
        public void Given_ChildSizeAndHeightClasses_When_TheChildIsMounted_Then_BothStay()
        {
            // Act
            var (_, leaf) = MountUnderChildVariant("[&>*]:size-4 [&>*]:h-8", "");

            // Assert
            Assert.That((leaf.ClassListContains("size-4"), leaf.ClassListContains("h-8")), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): as the case above, with the className writing h-8 first.
        [Test]
        public void Given_ChildHeightAndSizeClassesWrittenHeightFirst_When_TheChildIsMounted_Then_BothStay()
        {
            // Act
            var (_, leaf) = MountUnderChildVariant("[&>*]:h-8 [&>*]:size-4", "");

            // Assert
            Assert.That((leaf.ClassListContains("size-4"), leaf.ClassListContains("h-8")), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base applies the child's payloads in written order at one key, so
        // the height written last wins here; ordering the two by candidate alone puts size-[20px] last.
        [Test]
        public void Given_ChildSizeAndHeightValues_When_TheChildIsMounted_Then_TheHeightValueWins()
        {
            // Act — size-[20px] is emitted before h-[10px], since width comes before height in property order.
            var (_, leaf) = MountUnderChildVariant("[&>*]:size-[20px] [&>*]:h-[10px]", "");

            // Assert
            Assert.That((leaf.style.width.value.value, leaf.style.height.value.value), Is.EqualTo((20f, 10f)));
        }

        [Test]
        public void Given_ChildHeightAndSizeValuesWrittenHeightFirst_When_TheChildIsMounted_Then_TheHeightValueWins()
        {
            // Act
            var (_, leaf) = MountUnderChildVariant("[&>*]:h-[10px] [&>*]:size-[20px]", "");

            // Assert
            Assert.That((leaf.style.width.value.value, leaf.style.height.value.value), Is.EqualTo((20f, 10f)));
        }
    }
}
