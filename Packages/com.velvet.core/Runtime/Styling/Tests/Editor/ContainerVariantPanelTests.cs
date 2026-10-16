using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural coverage for the container query variants (<c>@sm:</c>…<c>@7xl:</c>, <c>@min-*:</c>,
    /// <c>@max-*:</c>, <c>@md/name:</c>): each reads the width of the container it asks for, the nearest ancestor
    /// marked <c>@container</c> (or <c>@container/name</c> for a named query), and matches nothing when there is
    /// none. The viewport variants (<c>sm:</c>…<c>2xl:</c>) never read a container. These run in a real
    /// <see cref="UnityEditor.EditorWindow"/> panel (via <see cref="PanelTestBase"/>) sized wider than every
    /// container, which are given fixed widths either side of the threshold under test, then deliver a
    /// <see cref="GeometryChangedEvent"/> on the containers so the manipulators re-read them. Each tuple carries
    /// the width a threshold was tested against beside its verdict, so a host that laid nothing out fails rather
    /// than passing unmeasured.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerVariantPanelTests : PanelTestBase
    {
        private const float PanelWidth = 1000f;
        private const float MdThreshold = 448f;      // @md
        private const float SmViewport = 640f;       // sm:

        protected override Rect WindowSize => new Rect(0, 0, PanelWidth, 600);

        [TearDown]
        public void ResetTheme() => VelvetTheme.IsDark = false;

        private VisualElement Find(string name) => _window.rootVisualElement.Q<VisualElement>(name);

        private static VNode Container(string name, string className, params VNode?[] children)
            => V.Div(name: name, className: className, children: children);

        private static VNode Leaf(string name, string className) => V.Label(name: name, className: className, text: "x");

        private bool On(string name) => Find(name).ClassListContains("bg-wide");

        private static readonly FieldInfo CallbackRegistry =
            typeof(CallbackEventHandler).GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Pins the viewport root as well as requesting the width, since the host window does not always get the
        // width it asks for, then lays out and fires the geometry event the manipulators re-read their source on.
        // A source nothing registered on is skipped, because SimulateEvent refuses it: an assertion then reads
        // the delivery that did not happen rather than an exception.
        private void Resolve(params VisualElement[] sources)
        {
            var panel = _window.rootVisualElement.panel;
            panel.visualTree.style.width = PanelWidth;
            ForcePanelUpdate(panel);
            foreach (var source in sources)
            {
                if (CallbackRegistry.GetValue(source) == null)
                {
                    continue;
                }
                using var evt = EventBase<GeometryChangedEvent>.GetPooled();
                source.SimulateEvent(evt);
            }
        }

        [Test]
        public void Given_ANarrowAndAWideContainer_When_TheyResolve_Then_OnlyTheWideOnesMdVariantIsOn()
        {
            // Arrange — 300px is below @md (448px) and 600px above it, in a panel wider than both.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                Container("narrow", "@container w-[300px]", Leaf("narrowLeaf", "@md:bg-wide")),
                Container("wide", "@container w-[600px]", Leaf("wideLeaf", "@md:bg-wide")),
            }));

            // Act
            Resolve(Find("narrow"), Find("wide"));

            // Assert
            Assert.That(
                (Find("narrow").resolvedStyle.width < MdThreshold, On("narrowLeaf"),
                    Find("wide").resolvedStyle.width >= MdThreshold, On("wideLeaf")),
                Is.EqualTo((true, false, true, true)));
        }

        [Test]
        public void Given_AnSmViewportVariantInsideANarrowContainer_When_ThePanelIsWide_Then_ItReadsThePanel()
        {
            // Arrange — the container is below sm: (640px) and the panel above it.
            _mounted = V.Mount(_window.rootVisualElement,
                Container("scope", "@container w-[300px]", Leaf("leaf", "sm:bg-wide")));

            // Act
            Resolve(_window.rootVisualElement.panel.visualTree);

            // Assert
            Assert.That(
                (_window.rootVisualElement.panel.visualTree.resolvedStyle.width >= SmViewport,
                    Find("scope").resolvedStyle.width < SmViewport, On("leaf")),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_ViewportAndContainerVariantsOnOneElement_When_OnlyTheContainerIsBelowItsThreshold_Then_OnlyTheContainerOneIsOff()
        {
            // Arrange — sm: is 640px of panel and @sm: 384px of container; 300px is below the latter, 600px above.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                Container("narrow", "@container w-[300px]", Leaf("narrowLeaf", "sm:bg-wide @sm:bg-container")),
                Container("wide", "@container w-[600px]", Leaf("wideLeaf", "sm:bg-wide @sm:bg-container")),
            }));

            // Act
            Resolve(_window.rootVisualElement.panel.visualTree, Find("narrow"), Find("wide"));

            // Assert
            Assert.That(
                (On("narrowLeaf"), Find("narrowLeaf").ClassListContains("bg-container"),
                    On("wideLeaf"), Find("wideLeaf").ClassListContains("bg-container")),
                Is.EqualTo((true, false, true, true)));
        }

        [Test]
        public void Given_NamedContainersOneInsideTheOther_When_TheyResolve_Then_ANamedVariantReadsItsOwnContainer()
        {
            // Arrange — outer 600px (above @md), inner 300px (below); the unnamed variant reads the nearest.
            _mounted = V.Mount(_window.rootVisualElement,
                Container("outer", "@container/outer w-[600px]",
                    Container("inner", "@container/inner w-[300px]",
                        Leaf("byOuter", "@md/outer:bg-wide"),
                        Leaf("byInner", "@md/inner:bg-wide"),
                        Leaf("byNearest", "@md:bg-wide"))));

            // Act
            Resolve(Find("outer"), Find("inner"));

            // Assert
            Assert.That(
                (Find("outer").resolvedStyle.width >= MdThreshold, Find("inner").resolvedStyle.width < MdThreshold,
                    On("byOuter"), On("byInner"), On("byNearest")),
                Is.EqualTo((true, true, true, false, false)));
        }

        [Test]
        public void Given_ANamedVariantAndNoContainerOfThatName_When_Resolved_Then_ItMatchesNothing()
        {
            // Arrange — the container above is wide enough, but it is not the one named.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                Container("wide", "@container/other w-[600px]",
                    Leaf("named", "@md/side:bg-wide"), Leaf("unnamed", "@md:bg-wide")),
            }));

            // Act
            Resolve(Find("wide"));

            // Assert
            Assert.That((Find("wide").resolvedStyle.width >= MdThreshold, On("named"), On("unnamed")),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_CeilingVariantsInAndOutOfContainers_When_Resolved_Then_OnlyANarrowContainersMatches()
        {
            // Arrange — @max-md reads 448px as a ceiling; with no container above there is nothing to be below.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                Container("narrow", "@container w-[300px]", Leaf("inNarrow", "@max-md:bg-wide")),
                Container("wide", "@container w-[600px]", Leaf("inWide", "@max-md:bg-wide")),
                Leaf("outside", "@max-md:bg-wide"),
            }));

            // Act
            Resolve(Find("narrow"), Find("wide"));

            // Assert
            Assert.That((On("inNarrow"), On("inWide"), On("outside")), Is.EqualTo((true, false, false)));
        }

        [Test]
        public void Given_ArbitraryAndNamedThresholds_When_AContainerIs520Wide_Then_EachMatchesByItsOwnThreshold()
        {
            // Arrange — floors at 500px, 448px (@min-md) and 640px, 768px (@3xl); ceilings at 500px and 640px.
            _mounted = V.Mount(_window.rootVisualElement,
                Container("scope", "@container w-[520px]",
                    Leaf("minPx", "@min-[500px]:bg-wide"),
                    Leaf("barePx", "@[500px]:bg-wide"),
                    Leaf("minRem", "@min-[40rem]:bg-wide"),
                    Leaf("maxPx", "@max-[500px]:bg-wide"),
                    Leaf("maxRem", "@max-[40rem]:bg-wide"),
                    Leaf("minMd", "@min-md:bg-wide"),
                    Leaf("threeXl", "@3xl:bg-wide")));

            // Act
            Resolve(Find("scope"));

            // Assert
            Assert.That(
                (On("minPx"), On("barePx"), On("minRem"), On("maxPx"), On("maxRem"), On("minMd"), On("threeXl")),
                Is.EqualTo((true, true, false, false, true, true, false)));
        }

        [Test]
        public void Given_ContainersAtAndJustBelowTheMdThreshold_When_Resolved_Then_AFloorIncludesItAndACeilingDoesNot()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                Container("at", "@container w-[448px]", Leaf("atFloor", "@md:bg-wide"), Leaf("atCeiling", "@max-md:bg-wide")),
                Container("below", "@container w-[447px]",
                    Leaf("belowFloor", "@md:bg-wide"), Leaf("belowCeiling", "@max-md:bg-wide")),
            }));

            // Act
            Resolve(Find("at"), Find("below"));

            // Assert
            Assert.That((On("atFloor"), On("atCeiling"), On("belowFloor"), On("belowCeiling")),
                Is.EqualTo((true, false, false, true)));
        }

        [Test]
        public void Given_AContainerResizedAcrossTheThreshold_When_ItResolvesAgain_Then_TheVariantFollowsIt()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                Container("scope", "@container w-[300px]", Leaf("leaf", "@md:bg-wide")));
            Resolve(Find("scope"));
            var narrow = On("leaf");

            // Act
            Find("scope").style.width = 600f;
            Resolve(Find("scope"));
            var wide = On("leaf");
            Find("scope").style.width = 300f;
            Resolve(Find("scope"));

            // Assert
            Assert.That((narrow, wide, On("leaf")), Is.EqualTo((false, true, false)));
        }

        [Test]
        public void Given_AContainerWithAnMdVariantOfItsOwn_When_Resolved_Then_ItAsksItsParentContainer()
        {
            // Arrange — the inner container is 300px, below @md, and the outer 600px, above it.
            _mounted = V.Mount(_window.rootVisualElement,
                Container("outer", "@container w-[600px]",
                    Container("inner", "@container w-[300px] @md:bg-wide")));

            // Act
            Resolve(Find("outer"));

            // Assert
            Assert.That((Find("inner").resolvedStyle.width < MdThreshold, On("inner")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_DarkAndAContainerVariantStackedEitherWayRound_When_ContainersResolve_Then_BothGateOnTheContainer()
        {
            // Arrange — dark is on, so the container is the only gate left to decide.
            VelvetTheme.IsDark = true;
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                Container("narrow", "@container w-[300px]",
                    Leaf("darkOuterNarrow", "dark:@md:bg-wide"), Leaf("containerOuterNarrow", "@md:dark:bg-wide")),
                Container("wide", "@container w-[600px]",
                    Leaf("darkOuterWide", "dark:@md:bg-wide"), Leaf("containerOuterWide", "@md:dark:bg-wide")),
            }));

            // Act
            Resolve(Find("narrow"), Find("wide"));

            // Assert
            Assert.That(
                (On("darkOuterNarrow"), On("containerOuterNarrow"), On("darkOuterWide"), On("containerOuterWide")),
                Is.EqualTo((false, false, true, true)));
        }

        [Test]
        public void Given_HoverAndAContainerVariantStackedEitherWayRound_When_TheLeavesAreHovered_Then_BothGateOnTheContainer()
        {
            // Arrange
            var leaves = new[] { "hoverNarrow", "containerNarrow", "hoverWide", "containerWide" };
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode?[]
            {
                Container("narrow", "@container w-[300px]",
                    Leaf(leaves[0], "hover:@md:bg-wide"), Leaf(leaves[1], "@md:hover:bg-wide")),
                Container("wide", "@container w-[600px]",
                    Leaf(leaves[2], "hover:@md:bg-wide"), Leaf(leaves[3], "@md:hover:bg-wide")),
            }));
            Resolve(Find("narrow"), Find("wide"));
            var before = (On(leaves[0]), On(leaves[1]), On(leaves[2]), On(leaves[3]));

            // Act
            foreach (var leaf in leaves)
            {
                if (CallbackRegistry.GetValue(Find(leaf)) == null)
                {
                    continue;
                }
                using var over = PointerOverEvent.GetPooled();
                Find(leaf).SimulateEvent(over);
            }

            // Assert — nothing is on before the pointer arrives, and only the wide container's leaves after.
            Assert.That(
                (before, (On(leaves[0]), On(leaves[1]), On(leaves[2]), On(leaves[3]))),
                Is.EqualTo(((false, false, false, false), (false, false, true, true))));
        }

        [Test]
        public void Given_ViewportAndContainerVariantsOfOneProperty_When_BothAreOn_Then_TheContainerOneWinsWhicheverIsWrittenLast()
        {
            // Arrange — the 1000px panel is above md: (768px) and the 700px container above @md (448px).
            _mounted = V.Mount(_window.rootVisualElement,
                Container("scope", "@container w-[700px]",
                    Leaf("viewportFirst", "md:w-[10px] @md:w-[20px]"),
                    Leaf("containerFirst", "@md:w-[20px] md:w-[10px]")));

            // Act
            Resolve(_window.rootVisualElement.panel.visualTree, Find("scope"));

            // Assert
            Assert.That(
                (Find("viewportFirst").style.width.value.value, Find("containerFirst").style.width.value.value),
                Is.EqualTo((20f, 20f)));
        }

        [Test]
        public void Given_TwoFloorsOfOneProperty_When_BothAreOn_Then_TheLargerThresholdWinsWhicheverIsWrittenLast()
        {
            // Arrange — a 600px container is above @md (448px) and @lg (512px).
            _mounted = V.Mount(_window.rootVisualElement,
                Container("scope", "@container w-[600px]",
                    Leaf("largerFirst", "@lg:w-[10px] @md:w-[20px]"),
                    Leaf("smallerFirst", "@md:w-[20px] @lg:w-[10px]")));

            // Act
            Resolve(Find("scope"));

            // Assert
            Assert.That(
                (Find("largerFirst").style.width.value.value, Find("smallerFirst").style.width.value.value),
                Is.EqualTo((10f, 10f)));
        }

        [Test]
        public void Given_TwoCeilingsOfOneProperty_When_BothAreOn_Then_TheSmallerThresholdWinsWhicheverIsWrittenLast()
        {
            // Arrange — a 480px container is below @max-lg (512px) and @max-xl (576px).
            _mounted = V.Mount(_window.rootVisualElement,
                Container("scope", "@container w-[480px]",
                    Leaf("smallerFirst", "@max-lg:w-[20px] @max-xl:w-[10px]"),
                    Leaf("largerFirst", "@max-xl:w-[10px] @max-lg:w-[20px]")));

            // Act
            Resolve(Find("scope"));

            // Assert
            Assert.That(
                (Find("smallerFirst").style.width.value.value, Find("largerFirst").style.width.value.value),
                Is.EqualTo((20f, 20f)));
        }

        [Test]
        public void Given_AFloorAndACeilingOfOneProperty_When_BothAreOn_Then_TheFloorWinsWhicheverIsWrittenLast()
        {
            // Arrange — a 480px container is above @md (448px) and below @max-lg (512px).
            _mounted = V.Mount(_window.rootVisualElement,
                Container("scope", "@container w-[480px]",
                    Leaf("ceilingFirst", "@max-lg:w-[10px] @md:w-[20px]"),
                    Leaf("floorFirst", "@md:w-[20px] @max-lg:w-[10px]")));

            // Act
            Resolve(Find("scope"));

            // Assert
            Assert.That(
                (Find("ceilingFirst").style.width.value.value, Find("floorFirst").style.width.value.value),
                Is.EqualTo((20f, 20f)));
        }
    }
}
