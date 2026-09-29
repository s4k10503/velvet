using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where the structural variants tie against their neighbours on the same property, in the order
    /// Tailwind emits them: group/peer, then first/last/only/odd/even, then after data the functional nth-*, and
    /// every arbitrary [&amp;:…]: selector after all the named variants.
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
            VelvetTheme.IsDark = _darkBefore;
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
        public void Given_DataAndNthWidths_When_BothHold_Then_TheNthWidthWins()
        {
            // Arrange / Act
            var (_, leaf) = MountFirstChild("data-[state=open]:w-[10px] nth-1:w-[20px]",
                data: new Dictionary<string, string> { ["state"] = "open" });

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_DarkAndArbitraryNthChildWidths_When_DarkTurnsOn_Then_TheArbitraryWidthWins()
        {
            // Arrange
            var (_, leaf) = MountFirstChild("dark:w-[10px] [&:nth-child(1)]:w-[20px]");

            // Act — dark lands after the arbitrary width, so a shared layer would leave the dark width.
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
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
    }
}
