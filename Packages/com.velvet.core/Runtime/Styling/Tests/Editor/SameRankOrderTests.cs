using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies two variant rules of one family-level rank: each keeps its own slot, and while both hold they
    /// order as Tailwind emits them — by their variants' values, then by property order, then by the candidate —
    /// never by where the className writes them.
    /// </summary>
    [TestFixture]
    internal sealed class SameRankOrderTests : PanelTestBase
    {
        private VisualElement MountLeaf(string className, IReadOnlyDictionary<string, string>? data = null)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "outer", children: new VNode?[]
                {
                    V.Div(name: "leaf", className: className, data: data),
                    V.Div(name: "sibling"),
                }));
            return _window.rootVisualElement.Q<VisualElement>("leaf");
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

        [Test]
        public void Given_Nth1AndNth2NamingOneClass_When_TwoRowsCarryBoth_Then_BothRowsKeepTheClass()
        {
            // Arrange / Act — on the first row nth-2: evaluates off after nth-1: put the class on.
            var rows = MountRows("nth-1:opacity-50 nth-2:opacity-50");

            // Assert
            Assert.That((rows.First.ClassListContains("opacity-50"), rows.Second.ClassListContains("opacity-50")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_TwoDataRulesNamingOneClass_When_OnlyTheFirstMatches_Then_TheClassStays()
        {
            // Arrange / Act — the second rule evaluates off after the first put the class on.
            var leaf = MountLeaf("data-[selected=true]:bg-blue-500 data-[highlighted=true]:bg-blue-500",
                new Dictionary<string, string> { ["selected"] = "true" });

            // Assert
            Assert.That(leaf.ClassListContains("bg-blue-500"), Is.True);
        }

        [Test]
        public void Given_TwoDataValuesOnOneProperty_When_BothHold_Then_TheLaterValueWins()
        {
            // Arrange / Act — "side=left" sorts before "state=open", so Tailwind emits state=open later.
            var leaf = MountLeaf("data-[state=open]:w-[10px] data-[side=left]:w-[20px]",
                new Dictionary<string, string> { ["state"] = "open", ["side"] = "left" });

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(10f));
        }

        [Test]
        public void Given_TwoArbitrarySelectorsOnOneProperty_When_BothHold_Then_TheLaterSelectorWins()
        {
            // Arrange / Act — "&:first-child" sorts before "&:nth-child(1)".
            var leaf = MountLeaf("[&:nth-child(1)]:w-[10px] [&:first-child]:w-[20px]");

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(10f));
        }

        [Test]
        public void Given_TwoHoverWidthsWrittenLargestFirst_When_Hovered_Then_TheCandidateOrderDecides()
        {
            // Arrange — hover:w-[10px] sorts before hover:w-[20px], so the 20 px rule is emitted later.
            var leaf = MountLeaf("hover:w-[20px] hover:w-[10px]");

            // Act
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_TwoHoverBackgroundClasses_When_Hovered_Then_TheOneEmittedLaterAloneRemains()
        {
            // Arrange — hover:bg-blue-500 sorts before hover:bg-red-500.
            var leaf = MountLeaf("hover:bg-red-500 hover:bg-blue-500");

            // Act
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);

            // Assert
            Assert.That((leaf.ClassListContains("bg-red-500"), leaf.ClassListContains("bg-blue-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AShorthandAndALonghandHoverMargin_When_Hovered_Then_TheLonghandWinsItsSide()
        {
            // Arrange — margin sorts before margin-top in Tailwind's property order, so mt- is emitted later.
            var leaf = MountLeaf("hover:mt-[8px] hover:m-[4px]");

            // Act
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);

            // Assert
            Assert.That(leaf.style.marginTop.value.value, Is.EqualTo(8f));
        }

        [Test]
        public void Given_TwoRadiiSharingACorner_When_Hovered_Then_TheFirstPropertyTheyDifferOnDecides()
        {
            // Arrange — rounded-t writes top-left and top-right, rounded-r top-right and bottom-right: the first
            // corner they differ on is top-right against bottom-right, which Tailwind's property order puts
            // rounded-t first, so rounded-r wins the shared corner. The candidate alone would put rounded-r first.
            var leaf = MountLeaf("w-[100px] h-[100px] hover:rounded-r-[8px] hover:rounded-t-[4px]");

            // Act
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);
            ForcePanelUpdate(leaf.panel);

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopRightRadius, Is.EqualTo(8f));
        }

        [Test]
        public void Given_TwoDataKeysEndingInNumbers_When_BothHold_Then_TheirValuesCompareAsText()
        {
            // Arrange / Act — Tailwind compares the values as strings, so "a10" sorts before "a9"; the candidate
            // alone would read the digits as numbers and put a9 first.
            var leaf = MountLeaf("data-[a9]:w-[20px] data-[a10]:w-[10px]",
                new Dictionary<string, string> { ["a9"] = "", ["a10"] = "" });

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_TwoHasClassArgumentsEndingInNumbers_When_BothHold_Then_TheirArgumentsCompareAsText()
        {
            // Arrange / Act — ".row10" sorts before ".row9" as text.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "leaf", className: "has-[.row9]:w-[20px] has-[.row10]:w-[10px]", children: new VNode?[]
                {
                    V.Div(className: "row9 row10"),
                }));

            // Assert
            Assert.That(_window.rootVisualElement.Q<VisualElement>("leaf").style.width.value.value,
                Is.EqualTo(20f));
        }

        [Test]
        public void Given_TwoStacksOfNamedGroupAndPeer_When_AllHold_Then_TheirPeerNamesDecideFirst()
        {
            // Arrange — the peer variants rank above the group ones, so the peer names are compared first:
            // "z" after "y" puts the first rule later, whatever the group names say.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "groupB", className: "group/b", children: new VNode?[]
                {
                    V.Div(name: "groupA", className: "group/a", children: new VNode?[]
                    {
                        V.Div(name: "peerZ", className: "peer/z"),
                        V.Div(name: "peerY", className: "peer/y"),
                        V.Div(name: "leaf",
                            className: "group-hover/a:peer-hover/z:w-[10px] group-hover/b:peer-hover/y:w-[20px]"),
                    }),
                }));
            var root = _window.rootVisualElement;

            // Act — the groups open the outer gates first, then the peers light the inners.
            foreach (var name in new[] { "groupA", "groupB", "peerZ", "peerY" })
            {
                using var over = PointerOverEvent.GetPooled();
                root.Q<VisualElement>(name).SimulateEvent(over);
            }

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.width.value.value, Is.EqualTo(10f));
        }

        // GREEN_ON_BASE(characterization): on the base the inline font size shared the class's layer, and inline
        // style beat the class; the case pins that ordering by what Tailwind emits keeps that answer.
        [Test]
        public void Given_ANamedTextSizeAndAnArbitraryOne_When_Hovered_Then_TheArbitraryOneWins()
        {
            // Arrange — Tailwind's text-lg emits font-size and line-height, so it sorts before text-[20px].
            var leaf = MountLeaf("hover:text-[20px] hover:text-lg");

            // Act
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);

            // Assert
            Assert.That(leaf.style.fontSize.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_TwoChildVariantWidths_When_TheChildIsMounted_Then_TheCandidateOrderDecides()
        {
            // Arrange / Act — [&>*]:w-[10px] sorts before [&>*]:w-[20px], so the 20 px rule is emitted later.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "[&>*]:w-[20px] [&>*]:w-[10px]", children: new VNode?[]
                {
                    V.Div(name: "child"),
                }));

            // Assert
            Assert.That(_window.rootVisualElement.Q<VisualElement>("child").style.width.value.value,
                Is.EqualTo(20f));
        }

        [Test]
        public void Given_ANamedAndAnUnnamedGroupHover_When_BothGroupsAreHovered_Then_TheNamedOneWins()
        {
            // Arrange — a group's name sorts after no name.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "unnamed", className: "group", children: new VNode?[]
                {
                    V.Div(name: "named", className: "group/card", children: new VNode?[]
                    {
                        V.Div(name: "leaf", className: "group-hover/card:w-[10px] group-hover:w-[20px]"),
                    }),
                }));
            var root = _window.rootVisualElement;

            // Act
            using (var overUnnamed = PointerOverEvent.GetPooled()) root.Q<VisualElement>("unnamed").SimulateEvent(overUnnamed);
            using (var overNamed = PointerOverEvent.GetPooled()) root.Q<VisualElement>("named").SimulateEvent(overNamed);

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.width.value.value, Is.EqualTo(10f));
        }
    }
}
