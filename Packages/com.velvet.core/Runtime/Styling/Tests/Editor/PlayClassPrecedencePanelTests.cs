using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A tween play's classes against the element's own styling on a real panel: while the play shows them they
    /// outrank everything but an important utility, as a CSS animation's declarations do, and once it ends they
    /// rank as the element's own or are gone.
    /// </summary>
    internal sealed class PlayClassPrecedencePanelTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        private static StateUpdater<bool> s_setLit;

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        // A Motion named "b" carrying className, entering under an AnimatePresence on a 0.2 s tween that shows from
        // and then to.
        private static VNode ClassicEnter(string className, string from, string to)
            => V.AnimatePresence(children: new VNode[]
            {
                V.Motion(key: "b", name: "b", className: "w-[20px] h-[20px] " + className,
                    transition: new StyleTransitionConfig { DurationSec = 0.2f, EnterFromClass = from, EnterToClass = to }),
            });

        private static StyleTransitionConfig Tween() => new() { DurationSec = 0.2f };

        // Mounts node on a panel whose scheduler runs on the fake clock, and returns the element named "b".
        private VisualElement MountOnFakeClock(VNode node)
        {
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_window.rootVisualElement, node);
            var element = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(element.panel);
            return element;
        }

        // Runs the play's next scheduled step, a second later on the fake clock: its class swap, then its completion.
        private static void Step(VisualElement element)
        {
            UseFrameFakeClockHost.Ms += 1000;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            ForcePanelUpdate(element.panel);
        }

        [Test]
        public void Given_ABaseBracketOpacity_When_AnEnterShowsAnOpacityClass_Then_TheClassWinsUntilItEnds()
        {
            // Arrange
            var element = MountOnFakeClock(ClassicEnter("opacity-[0.7]", "opacity-0", "opacity-100"));
            var during = element.style.opacity.keyword;

            // Act
            Step(element);
            Step(element);

            // Assert
            Assert.That((during, element.style.opacity.value), Is.EqualTo((StyleKeyword.Null, 0.7f)));
        }

        [Test]
        public void Given_ABaseOpacityClassTheStylesheetDeclaresLater_When_AnEnterShowsAnOpacityClass_Then_TheEntersClassWins()
        {
            // Arrange / Act — opacity-50 sits after opacity-0 in the bundled stylesheet.
            var element = MountOnFakeClock(ClassicEnter("opacity-50", "opacity-0", "opacity-100"));

            // Assert — the enter's class rides along, since one that never landed would leave opacity-50 alone.
            Assert.That((element.ClassListContains("opacity-0"), element.ClassListContains("opacity-50")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnImportantBaseOpacity_When_AnEnterShowsAnOpacityClass_Then_TheImportantOneWins()
        {
            // Arrange / Act — opacity-50 sits after opacity-0 in the bundled stylesheet.
            var element = MountOnFakeClock(ClassicEnter("!opacity-0", "opacity-50", "opacity-100"));

            // Assert — the important class rides along, since one that never landed would leave opacity-50 alone.
            Assert.That((element.ClassListContains("opacity-0"), element.ClassListContains("opacity-50")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnEnterShowingAnOpacityClass_When_TheReconcilerTakesTheClassOff_Then_ItsRankGoesWithIt()
        {
            // Arrange
            var element = MountOnFakeClock(ClassicEnter("opacity-[0.7]", "opacity-0", "opacity-100"));
            var during = element.style.opacity.keyword;

            // Act
            StyleClassProjection.Remove(element, "opacity-0", StyleLayerPriority.Base);

            // Assert
            Assert.That((during, element.style.opacity.value), Is.EqualTo((StyleKeyword.Null, 0.7f)));
        }

        [Test]
        public void Given_AVariantEnterOverABaseBracketOpacity_When_ItEnds_Then_TheBracketComesBack()
        {
            // Arrange
            var poses = new Dictionary<string, MotionVariant> { ["hidden"] = "opacity-0", ["visible"] = "opacity-100" };
            var element = MountOnFakeClock(V.Motion(name: "b", className: "w-[20px] h-[20px] opacity-[0.7]",
                variants: poses, initial: "hidden", animate: "visible", transition: Tween()));
            var during = element.style.opacity.keyword;

            // Act
            Step(element);
            Step(element);

            // Assert — the resting class stays on, and an inline value outranks it as the element's own.
            Assert.That((during, element.ClassListContains("opacity-100"), element.style.opacity.value),
                Is.EqualTo((StyleKeyword.Null, true, 0.7f)));
        }

        [Test]
        public void Given_ADividedChild_When_AnEnterShowsABorderColorClass_Then_TheClassWinsOverTheDivideColorUntilItEnds()
        {
            // Arrange
            var element = MountOnFakeClock(V.Div(children: new VNode[]
            {
                V.Div(className: "flex flex-row divide-x divide-gray-200", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    ClassicEnter("", "border-red-500", "border-blue-500"),
                    V.Div(className: "w-[20px] h-[20px]"),
                }),
            }));
            var during = element.style.borderRightColor.keyword;

            // Act
            Step(element);
            Step(element);

            // Assert — e5e7eb is gray-200, the divide color.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            Assert.That((during, element.style.borderRightColor.value), Is.EqualTo((StyleKeyword.Null, gray200)));
        }

        private static string s_rowClass;
        private static string s_swappedClass;

        // A row of s_rowClass whose middle child is a Motion that swaps to s_swappedClass on a 0.2 s tween when
        // s_setLit says so.
        [Component]
        private static VNode RowSwap()
        {
            var (lit, setLit) = Hooks.UseState(false);
            s_setLit = setLit;
            var poses = new Dictionary<string, MotionVariant>
            {
                ["a"] = "opacity-100", ["b"] = "opacity-100 " + s_swappedClass,
            };
            return V.Div(className: "flex flex-row " + s_rowClass, children: new VNode[]
            {
                V.Div(className: "w-[20px] h-[20px]"),
                V.Motion(name: "b", className: "w-[20px] h-[20px]", variants: poses, animate: lit ? "b" : "a",
                    transition: Tween()),
                V.Div(className: "w-[20px] h-[20px]"),
            });
        }

        // What slot reads on the middle child of a row of rowClass before and after a variant swap shows
        // swappedClass on it.
        private (StyleKeyword Before, StyleKeyword After) SwapInRow(string rowClass, string swappedClass,
            System.Func<IStyle, StyleKeyword> slot)
        {
            s_rowClass = rowClass;
            s_swappedClass = swappedClass;
            var element = MountOnFakeClock(V.Component(RowSwap));
            var before = slot(element.style);
            s_setLit.Invoke(true);
            _mounted.FlushStateForTest();
            Step(element);
            return (before, slot(element.style));
        }

        [Test]
        public void Given_ADividedChild_When_AVariantSwapShowsABorderColorClass_Then_TheDivideColorGivesWay()
        {
            // Arrange / Act
            var read = SwapInRow("divide-x divide-gray-200", "border-red-500", style => style.borderRightColor.keyword);

            // Assert — the divide color held before rides along, since a row holding nothing gives way to nothing.
            Assert.That(read, Is.EqualTo((StyleKeyword.Undefined, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ASpacedChild_When_AVariantSwapShowsAMarginClass_Then_TheSpaceMarginGivesWay()
        {
            // Arrange / Act
            var read = SwapInRow("space-x-4", "mr-2", style => style.marginRight.keyword);

            // Assert — the space margin held before rides along, since a row holding nothing gives way to nothing.
            Assert.That(read, Is.EqualTo((StyleKeyword.Undefined, StyleKeyword.Null)));
        }

        // A Motion that swaps from opacity-100 to opacity-0 on a 0.2 s tween when s_setLit says so, at half opacity
        // while hovered.
        [Component]
        private static VNode HoveredSwap()
        {
            var (lit, setLit) = Hooks.UseState(false);
            s_setLit = setLit;
            var poses = new Dictionary<string, MotionVariant> { ["a"] = "opacity-100", ["b"] = "opacity-0" };
            return V.Motion(name: "b", className: "w-[20px] h-[20px]", variants: poses, animate: lit ? "b" : "a",
                transition: Tween(), whileHoverClass: "opacity-50");
        }

        [Test]
        public void Given_AHoveredMotion_When_AVariantSwapShowsAnOpacityClass_Then_ThePlaysClassWinsUntilItEnds()
        {
            // Arrange — opacity-50 sits after opacity-0 in the bundled stylesheet.
            var element = MountOnFakeClock(V.Component(HoveredSwap));
            using (var over = PointerOverEvent.GetPooled())
            {
                element.SimulateEvent(over);
            }
            s_setLit.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            Step(element);
            var swapped = element.ClassListContains("opacity-50");
            Step(element);

            // Assert — the hover class is taken off while the play's class shows, and put back once it rests.
            Assert.That((swapped, element.ClassListContains("opacity-50")), Is.EqualTo((false, true)));
        }
    }
}
