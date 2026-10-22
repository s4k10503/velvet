using System;
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
        public void Given_ABaseBracketOpacity_When_AnEnterShowsAnOpacityClass_Then_TheClassWinsWhileItShows()
        {
            // Arrange / Act
            var element = MountOnFakeClock(ClassicEnter("opacity-[0.7]", "opacity-0", "opacity-100"));

            // Assert — read as null unless the bracket's layer stands and the enter's class is on, so an element
            // that never carried the bracket, or an enter that never showed its class, cannot pass.
            var arranged = StyleArbitraryValueResolver.HasLayer(element, ArbitraryProperty.Opacity)
                && element.ClassListContains("opacity-0");
            Assert.That(arranged ? element.style.opacity.keyword : (StyleKeyword?)null, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ABaseBracketOpacity_When_AnEnterEnds_Then_TheBracketComesBack()
        {
            // Arrange
            var element = MountOnFakeClock(ClassicEnter("opacity-[0.7]", "opacity-0", "opacity-100"));
            // Whether the class stands over the bracket while the enter shows it.
            var shown = element.style.opacity.keyword == StyleKeyword.Null;

            // Act
            Step(element);
            Step(element);

            // Assert — read as NaN or null where the arrangement did not hold, which the expected value is not.
            Assert.That(shown ? element.style.opacity.value : float.NaN, Is.EqualTo(0.7f));
        }

        [Test]
        public void Given_ABaseOpacityClassTheStylesheetDeclaresLater_When_AnEnterShowsAnOpacityClass_Then_TheEntersClassWins()
        {
            // Arrange / Act — opacity-50 sits after opacity-0 in the bundled stylesheet.
            var element = MountOnFakeClock(ClassicEnter("opacity-50", "opacity-0", "opacity-100"));

            // Assert — the base class is off the list, which an enter that never landed would leave on.
            Assert.That(element.ClassListContains("opacity-50"), Is.False);
        }

        [Test]
        public void Given_AnImportantBaseOpacity_When_AnEnterShowsAnOpacityClass_Then_TheImportantOneWins()
        {
            // Arrange / Act — opacity-50 sits after opacity-0 in the bundled stylesheet.
            var element = MountOnFakeClock(ClassicEnter("!opacity-0", "opacity-50", "opacity-100"));

            // Assert — the enter's class is the element's own and held off its list, which an enter that never
            // showed it leaves false.
            Assert.That(StyleClassProjection.SuppressesOwnClass(element, "opacity-50"), Is.True);
        }

        [Test]
        public void Given_AnEnterShowingAnOpacityClass_When_TheReconcilerTakesTheClassOff_Then_ItsRankGoesWithIt()
        {
            // Arrange
            var element = MountOnFakeClock(ClassicEnter("opacity-[0.7]", "opacity-0", "opacity-100"));
            // Whether the class stands over the bracket while the enter shows it.
            var shown = element.style.opacity.keyword == StyleKeyword.Null;

            // Act
            StyleClassProjection.Remove(element, "opacity-0", StyleLayerPriority.Base);

            // Assert — read as NaN or null where the arrangement did not hold, which the expected value is not.
            Assert.That(shown ? element.style.opacity.value : float.NaN, Is.EqualTo(0.7f));
        }

        // A variant enter from opacity-0 to opacity-100 over a base opacity-[0.7].
        private VisualElement MountVariantEnterOverBracketOpacity()
        {
            var poses = new Dictionary<string, MotionVariant> { ["hidden"] = "opacity-0", ["visible"] = "opacity-100" };
            return MountOnFakeClock(V.Motion(name: "b", className: "w-[20px] h-[20px] opacity-[0.7]",
                variants: poses, initial: "hidden", animate: "visible", transition: Tween()));
        }

        [Test]
        public void Given_AVariantEnterOverABaseBracketOpacity_When_ItEnds_Then_TheBracketComesBack()
        {
            // Arrange
            var element = MountVariantEnterOverBracketOpacity();
            // Whether the class stands over the bracket while the enter shows it.
            var shown = element.style.opacity.keyword == StyleKeyword.Null;

            // Act
            Step(element);
            Step(element);

            // Assert — an inline value outranks the resting class as the element's own.
            Assert.That(shown ? element.style.opacity.value : float.NaN, Is.EqualTo(0.7f));
        }

        [Test]
        public void Given_AVariantEnterOverABaseBracketOpacity_When_ItEnds_Then_TheRestingClassStaysOn()
        {
            // Arrange
            var element = MountVariantEnterOverBracketOpacity();

            // Act
            Step(element);
            Step(element);

            // Assert
            Assert.That(element.ClassListContains("opacity-100"), Is.True);
        }

        // A driven play lands its to-classes at once and writes the values between from and to inline, so what its
        // rank decides is which of the element's own classes stay on the list while it runs.
        // A bezier enter from opacity-0 to opacity-100 over a base opacity-50.
        private VisualElement MountBezierEnterOverOpacityClass()
        {
            var poses = new Dictionary<string, MotionVariant> { ["hidden"] = "opacity-0", ["visible"] = "opacity-100" };
            return MountOnFakeClock(V.Motion(name: "b", className: "w-[20px] h-[20px] opacity-50",
                variants: poses, initial: "hidden", animate: "visible",
                transition: new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0.2f }));
        }

        [Test]
        public void Given_ABaseOpacityClass_When_ABezierEnterShowsAnOpacityClass_Then_TheBaseClassIsOffWhileItPlays()
        {
            // Arrange / Act
            var element = MountBezierEnterOverOpacityClass();

            // Assert
            Assert.That(element.ClassListContains("opacity-50"), Is.False);
        }

        [Test]
        public void Given_ABaseOpacityClass_When_ABezierEnterEnds_Then_TheBaseClassComesBack()
        {
            // Arrange
            var element = MountBezierEnterOverOpacityClass();
            // Whether the base class is off the list while the enter plays.
            var shown = !element.ClassListContains("opacity-50");

            // Act
            Step(element);
            Step(element);
            Step(element);

            // Assert — read as NaN or null where the arrangement did not hold, which the expected value is not.
            Assert.That(shown ? element.ClassListContains("opacity-50") : (bool?)null, Is.True);
        }

        // A divided row whose middle child enters from border-red-500 to border-blue-500.
        private VisualElement MountDividedChildEnter()
            => MountOnFakeClock(V.Div(children: new VNode[]
            {
                V.Div(className: "flex flex-row divide-x divide-gray-200", children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    ClassicEnter("", "border-red-500", "border-blue-500"),
                    V.Div(className: "w-[20px] h-[20px]"),
                }),
            }));

        [Test]
        public void Given_ADividedChild_When_AnEnterShowsABorderColorClass_Then_TheClassWinsOverTheDivideColorWhileItShows()
        {
            // Arrange / Act
            var element = MountDividedChildEnter();

            // Assert
            Assert.That(element.style.borderRightColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ADividedChild_When_AnEnterEnds_Then_TheDivideColorComesBack()
        {
            // Arrange
            var element = MountDividedChildEnter();
            // Whether the class stands over the divide color while the enter shows it.
            var shown = element.style.borderRightColor.keyword == StyleKeyword.Null;

            // Act
            Step(element);
            Step(element);

            // Assert — e5e7eb is gray-200, the divide color.
            ColorUtility.TryParseHtmlString("#e5e7eb", out var gray200);
            Assert.That(shown ? element.style.borderRightColor.value : (Color?)null, Is.EqualTo(gray200));
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

        // What slot reads on the middle child of a row of rowClass after a variant swap shows swappedClass on it, or
        // null where the row held nothing there before, which gives way to nothing.
        private StyleKeyword? SwapInRow(string rowClass, string swappedClass, System.Func<IStyle, StyleKeyword> slot)
        {
            s_rowClass = rowClass;
            s_swappedClass = swappedClass;
            var element = MountOnFakeClock(V.Component(RowSwap));
            var held = slot(element.style) == StyleKeyword.Undefined;
            s_setLit.Invoke(true);
            _mounted.FlushStateForTest();
            Step(element);
            return held ? slot(element.style) : null;
        }

        [Test]
        public void Given_ADividedChild_When_AVariantSwapShowsABorderColorClass_Then_TheDivideColorGivesWay()
        {
            // Arrange / Act
            var read = SwapInRow("divide-x divide-gray-200", "border-red-500", style => style.borderRightColor.keyword);

            // Assert
            Assert.That(read, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ASpacedChild_When_AVariantSwapShowsAMarginClass_Then_TheSpaceMarginGivesWay()
        {
            // Arrange / Act
            var read = SwapInRow("space-x-4", "mr-2", style => style.marginRight.keyword);

            // Assert
            Assert.That(read, Is.EqualTo(StyleKeyword.Null));
        }

        private static StyleTransitionConfig s_swapTransition = new() { DurationSec = 0.2f };

        // A Motion scaled up while hovered that, once s_setLit says so, exits from scale-100 to scale-95 on a 0.2 s
        // tween.
        [Component]
        private static VNode HoveredExit()
        {
            var (lit, setLit) = Hooks.UseState(false);
            s_setLit = setLit;
            return V.AnimatePresence(children: new VNode[]
            {
                lit ? null : V.Motion(key: "b", name: "b", className: "w-[20px] h-[20px]", whileHoverClass: "scale-105",
                    transition: new StyleTransitionConfig
                    {
                        DurationSec = 0.2f, ExitFromClass = "scale-100", ExitToClass = "scale-95",
                    }),
            });
        }

        // GREEN_ON_BASE(characterization): the base ranks a gesture class with the element's own, beneath a play's.
        // Showing an exit's classes in the play band beneath the gesture band (StyleLayerPriority.Animation) reddens
        // this case.
        [Test]
        public void Given_AHoveredMotionScaledUp_When_ItExitsToAScaleClass_Then_ItReachesTheExitsScale()
        {
            // Arrange
            var element = MountOnFakeClock(V.Component(HoveredExit));
            Hover(element);
            var hovered = element.ClassListContains("scale-105");

            // Act — past the exit's swap and its 0.2 s, short of the completion that removes it.
            s_setLit.Invoke(true);
            _mounted.FlushStateForTest();
            Step(element);
            UseFrameFakeClockHost.Ms += 500;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);

            // Assert — NaN where the hover never put its class on.
            Assert.That(hovered ? element.resolvedStyle.scale.value.x : float.NaN, Is.EqualTo(0.95f).Within(1e-4f));
        }

        // A Motion that swaps from opacity-100 to opacity-0 on s_swapTransition when s_setLit says so, at half opacity
        // while hovered.
        [Component]
        private static VNode HoveredSwap()
        {
            var (lit, setLit) = Hooks.UseState(false);
            s_setLit = setLit;
            var poses = new Dictionary<string, MotionVariant> { ["a"] = "opacity-100", ["b"] = "opacity-0" };
            return V.Motion(name: "b", className: "w-[20px] h-[20px]", variants: poses, animate: lit ? "b" : "a",
                transition: s_swapTransition, whileHoverClass: "opacity-50");
        }

        // A hovered HoveredSwap on transition one step into its variant swap.
        private VisualElement MountHoveredSwapAfterTheSwap(StyleTransitionConfig transition)
        {
            s_swapTransition = transition;
            var element = MountOnFakeClock(V.Component(HoveredSwap));
            using (var over = PointerOverEvent.GetPooled())
            {
                element.SimulateEvent(over);
            }
            s_setLit.Invoke(true);
            _mounted.FlushStateForTest();
            Step(element);
            return element;
        }

        // Framer Motion protects a value a gesture state sets from the element's animate and variants, so the hover
        // class keeps the opacity the swap would move, on a tween, whose swap lands its class beneath the hover
        // class, and on a spring or a bezier, whose plan leaves the value out.
        [TestCase(TransitionType.Tween)]
        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AHoveredMotion_When_AVariantSwapPlaysAnOpacityTheHoverClassSets_Then_TheHoverClassKeepsIt(
            TransitionType type)
        {
            // Arrange / Act
            var element = MountHoveredSwapAfterTheSwap(new StyleTransitionConfig { Type = type, DurationSec = 0.2f });

            // Assert — read as NaN unless the swap's class is the element's own and held off its list, which a swap
            // that never ran, or a hover that never came on, leaves false.
            Assert.That(StyleClassProjection.SuppressesOwnClass(element, "opacity-0") ? element.resolvedStyle.opacity
                : float.NaN, Is.EqualTo(0.5f).Within(1e-4f));
        }

        private static readonly StyleTransitionConfig s_spring =
            new() { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f };

        // Moves the fake clock on by ms and runs what the panel's scheduler then runs, a play's tick among it.
        private static void Advance(VisualElement element, int ms)
        {
            UseFrameFakeClockHost.Ms += ms;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            ForcePanelUpdate(element.panel);
        }

        // An unhovered HoveredSwap fifty milliseconds into a spring swap.
        private VisualElement MountSpringSwapPartWay()
        {
            s_swapTransition = s_spring;
            var element = MountOnFakeClock(V.Component(HoveredSwap));
            s_setLit.Invoke(true);
            _mounted.FlushStateForTest();
            Advance(element, 50);
            return element;
        }

        [Test]
        public void Given_ASpringSwapOfTheOpacity_When_TheMotionIsHoveredPartWay_Then_TheHoverClassTakesTheOpacity()
        {
            // Arrange
            var element = MountSpringSwapPartWay();
            // Whether the spring writes the opacity, which a swap that never started leaves to the classes.
            var driving = element.style.opacity.keyword == StyleKeyword.Undefined;

            // Act
            Hover(element);
            Advance(element, 50);

            // Assert — NaN where the spring never wrote the opacity.
            Assert.That(driving ? element.resolvedStyle.opacity : float.NaN, Is.EqualTo(0.5f).Within(1e-4f));
        }

        // Framer Motion animates a value a gesture state lets go of back to its animate target from where it is shown,
        // on the value's transition, so the opacity leaves 0.5 on the spring rather than jumping to where the spring
        // would have reached.
        [Test]
        public void Given_AHoverHoldingASpringSwapsOpacity_When_ThePointerLeavesPartWay_Then_TheOpacityLeavesFromTheHoverValue()
        {
            // Arrange — hovered for a quarter of a second, over which the swap's spring has run on below 0.5.
            var element = MountSpringSwapPartWay();
            Hover(element);
            Advance(element, 250);
            var hovered = element.resolvedStyle.opacity;

            // Act
            using (var leave = PointerOutEvent.GetPooled())
            {
                element.SimulateEvent(leave);
            }
            Advance(element, 16);

            // Assert — NaN where the hover never held the opacity at 0.5.
            Assert.That(Mathf.Abs(hovered - 0.5f) < 1e-3f ? element.resolvedStyle.opacity : float.NaN,
                Is.GreaterThan(0.4f));
        }

        private ReconcilerContext Context => _mounted.Root.Reconciler.Context;

        [Test]
        public void Given_AnEnterShowingAShadowClass_When_ItPlays_Then_TheShadowPaintsWhileItShows()
        {
            // Arrange / Act
            var element = MountOnFakeClock(ClassicEnter("", "shadow-lg", "opacity-100"));

            // Assert
            Assert.That(Context.ShadowBindings.ContainsKey(element), Is.True);
        }

        [Test]
        public void Given_AnEnterShowingAShadowClass_When_ItEnds_Then_TheShadowGoes()
        {
            // Arrange
            var element = MountOnFakeClock(ClassicEnter("", "shadow-lg", "opacity-100"));
            // Whether the shadow paints while the enter shows it.
            var shown = Context.ShadowBindings.ContainsKey(element);

            // Act
            Step(element);
            Step(element);

            // Assert — read as NaN or null where the arrangement did not hold, which the expected value is not.
            Assert.That(shown ? Context.ShadowBindings.ContainsKey(element) : (bool?)null, Is.False);
        }

        // A row Motion entering from gap-4, with two children.
        private VisualElement MountGapEnter()
            => MountOnFakeClock(V.AnimatePresence(children: new VNode[]
            {
                V.Motion(key: "b", name: "b", className: "flex flex-row w-[60px] h-[20px]",
                    transition: new StyleTransitionConfig
                    {
                        DurationSec = 0.2f, EnterFromClass = "gap-4", EnterToClass = "opacity-100",
                    },
                    children: new VNode[] { V.Div("w-[10px] h-[10px]"), V.Div("w-[10px] h-[10px]") }),
            }));

        [Test]
        public void Given_AnEnterShowingAGapClass_When_ItPlays_Then_TheGapAppliesWhileItShows()
        {
            // Arrange / Act
            var element = MountGapEnter();

            // Assert
            Assert.That(Context.GapManipulators.ContainsKey(element), Is.True);
        }

        [Test]
        public void Given_AnEnterShowingAGapClass_When_ItEnds_Then_TheGapGoes()
        {
            // Arrange
            var element = MountGapEnter();
            // Whether the gap applies while the enter shows it.
            var shown = Context.GapManipulators.ContainsKey(element);

            // Act
            Step(element);
            Step(element);

            // Assert — read as NaN or null where the arrangement did not hold, which the expected value is not.
            Assert.That(shown ? Context.GapManipulators.ContainsKey(element) : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AnEnterShowingABracketWidth_When_ItPlays_Then_TheWidthAppliesWhileItShows()
        {
            // Arrange / Act
            var element = MountOnFakeClock(ClassicEnter("", "w-[40px]", "opacity-100"));

            // Assert
            Assert.That(element.style.width.value.value, Is.EqualTo(40f));
        }

        [Test]
        public void Given_AnEnterShowingABracketWidth_When_ItsSwapLands_Then_TheElementsOwnWidthHolds()
        {
            // Arrange
            var element = MountOnFakeClock(ClassicEnter("", "w-[40px]", "opacity-100"));
            // Whether the play's width applies while it shows.
            var shown = element.style.width.value.value == 40f;

            // Act
            Step(element);

            // Assert — ClassicEnter's own w-[20px] holds the width once the play's class is gone.
            Assert.That(shown ? element.style.width.value.value : float.NaN, Is.EqualTo(20f));
        }

        // A variant enter into a pose carrying shadow-lg, from one that does not.
        private VisualElement MountEnterIntoShadowedPose()
        {
            var poses = new Dictionary<string, MotionVariant>
            {
                ["hidden"] = "opacity-0", ["visible"] = "opacity-100 shadow-lg",
            };
            return MountOnFakeClock(V.Motion(name: "b", className: "w-[20px] h-[20px]",
                variants: poses, initial: "hidden", animate: "visible", transition: Tween()));
        }

        [Test]
        public void Given_AVariantEnterIntoAShadowedPose_When_ItPlays_Then_NoShadowPaintsBeforeThePoseIsShown()
        {
            // Arrange / Act — the resting pose carries the shadow and the pose entered from does not, so the
            // shadow waits for the swap, as the pose's class does.
            var element = MountEnterIntoShadowedPose();

            // Assert
            Assert.That(Context.ShadowBindings.ContainsKey(element), Is.False);
        }

        [Test]
        public void Given_AVariantEnterIntoAShadowedPose_When_ItsSwapLands_Then_TheShadowPaints()
        {
            // Arrange
            var element = MountEnterIntoShadowedPose();
            // Whether the shadow waits for the swap.
            var shown = !Context.ShadowBindings.ContainsKey(element);

            // Act
            Step(element);
            Step(element);

            // Assert — read as NaN or null where the arrangement did not hold, which the expected value is not.
            Assert.That(shown ? Context.ShadowBindings.ContainsKey(element) : (bool?)null, Is.True);
        }

        // A Motion named "b" entering under an AnimatePresence on a 0.2 s tween from `from` to opacity-100, beside a
        // reference named "ref" carrying reference.
        private static VNode EnterBeside(string className, string from, string reference, Type? elementType = null)
            => V.Div(children: new VNode[]
            {
                V.AnimatePresence(children: new VNode[]
                {
                    V.Motion(key: "b", name: "b", className: "w-[20px] h-[20px] " + className, elementType: elementType,
                        transition: new StyleTransitionConfig
                        {
                            DurationSec = 0.2f, EnterFromClass = from, EnterToClass = "opacity-100",
                        }),
                }),
                V.Div(name: "ref", className: "w-[20px] h-[20px] " + reference),
            });

        private static void Hover(VisualElement element)
        {
            using (var over = PointerOverEvent.GetPooled())
            {
                element.SimulateEvent(over);
            }
            ForcePanelUpdate(element.panel);
        }

        // A hovered element with a hover background of its own, entering from a bracket background.
        private VisualElement MountHoveredEnterOverHoverBackground()
        {
            var element = MountOnFakeClock(EnterBeside("bg-[#0000ff] hover:bg-[#00ff00]", "bg-[#ff0000]", ""));
            Hover(element);
            return element;
        }

        [Test]
        public void Given_AHoveredElementWithAHoverBackground_When_AnEnterShowsABracketBackground_Then_ThePlaysWinsWhileItShows()
        {
            // Arrange / Act — the play's layer outranks the hover payload's, as an animation's declaration outranks a
            // :hover rule.
            var element = MountHoveredEnterOverHoverBackground();

            // Assert
            Assert.That(element.style.backgroundColor.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AHoveredElementWithAHoverBackground_When_TheEntersSwapLands_Then_TheHoverPayloadComesBack()
        {
            // Arrange
            var element = MountHoveredEnterOverHoverBackground();
            // Whether the play's color wins while it shows.
            var shown = element.style.backgroundColor.value == Color.red;

            // Act
            Step(element);

            // Assert — read as NaN or null where the arrangement did not hold, which the expected value is not.
            Assert.That(shown ? element.style.backgroundColor.value : (Color?)null, Is.EqualTo(Color.green));
        }

        // A hovered element with a hover padding class of its own, entering from a bracket padding.
        private VisualElement MountHoveredEnterOverHoverPadding()
        {
            var element = MountOnFakeClock(EnterBeside("hover:p-8", "p-[6px]", "p-8"));
            Hover(element);
            return element;
        }

        [Test]
        public void Given_AHoveredElementWithAHoverPaddingClass_When_AnEnterShowsABracketPadding_Then_ThePlaysWinsWhileItShows()
        {
            // Arrange / Act — a class of a lower rank than the play's layer claiming the same longhand is held off the
            // element while the layer stands (StyleClassProjection's floors).
            var element = MountHoveredEnterOverHoverPadding();

            // Assert
            Assert.That(element.resolvedStyle.paddingTop, Is.EqualTo(6f));
        }

        [Test]
        public void Given_AHoveredElementWithAHoverPaddingClass_When_TheEntersSwapLands_Then_TheHoverClassComesBack()
        {
            // Arrange
            var element = MountHoveredEnterOverHoverPadding();
            // Whether the play's padding wins while it shows.
            var shown = element.resolvedStyle.paddingTop == 6f;

            // Act
            Step(element);

            // Assert — read as NaN or null where the arrangement did not hold, which the expected value is not.
            Assert.That(shown ? element.resolvedStyle.paddingTop : float.NaN,
                Is.EqualTo(_window.rootVisualElement.Q("ref").resolvedStyle.paddingTop));
        }

        [Component]
        private static VNode GapRowWithEnteringChild()
            => V.Div(className: "flex flex-row gap-[10px]", children: new VNode[]
            {
                V.Div(className: "w-[20px] h-[20px]"),
                V.AnimatePresence(children: new VNode[]
                {
                    V.Motion(key: "b", name: "b", className: "w-[20px] h-[20px]",
                        transition: new StyleTransitionConfig
                        {
                            DurationSec = 0.2f, EnterFromClass = "ml-[3px]", EnterToClass = "opacity-100",
                        }),
                }),
            });

        // GREEN_ON_BASE(characterization): a gap's held child margin outranks a play's bracket margin as it does a child's own.
        // Make the gap manipulator yield its held margin to the element's layers and this reddens.
        [Test]
        public void Given_AGapRowChild_When_AnEnterShowsABracketMargin_Then_TheGapsHeldMarginStands()
        {
            // Arrange
            var element = MountOnFakeClock(V.Component(GapRowWithEnteringChild));
            var during = element.style.marginLeft.value.value;

            // Act
            Step(element);
            Step(element);

            // Assert — the margin the play's ml-[3px] would have replaced is the one the gap holds once it is gone.
            Assert.That(during, Is.EqualTo(element.style.marginLeft.value.value));
        }

        [Test]
        public void Given_AMotionTextField_When_AnEnterShowsABracketBackground_Then_ItsInputBoxTakesIt()
        {
            // Arrange / Act — a field control's surface utility lands on the box its value is shown in, as a
            // payload's does.
            var element = MountOnFakeClock(EnterBeside("", "bg-[#ff0000]", "", typeof(TextField)));
            var box = element.Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass);

            // Assert
            Assert.That(box.style.backgroundColor.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AMotionTextField_When_AnEnterShowsTruncate_Then_TheTextItsInputBoxDrawsTakesIt()
        {
            // Arrange / Act — as a reconciled class does
            // (Given_TruncateOnATextField_When_Reconciled_Then_TheTextTheInputBoxDrawsTakesIt).
            var element = MountOnFakeClock(EnterBeside("", "truncate", "", typeof(TextField)));
            var box = element.Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass);

            // Assert
            Assert.That(box.Q<TextElement>().ClassListContains("truncate"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base's play never touches the input box's clip. Keeping the clip only
        // for a class the element keeps that is the clip itself (StyleAnimationClassUtils.ClipStays) reddens this case.
        [Test]
        public void Given_ATruncatingMotionTextField_When_AnEntersTextOverflowUtilityLeaves_Then_ItsInputBoxStillClips()
        {
            // Arrange
            var element = MountOnFakeClock(EnterBeside("truncate", "text-ellipsis", "", typeof(TextField)));
            var box = element.Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass);

            // Act — the swap, which takes the enter's class off.
            Step(element);

            // Assert
            Assert.That(box.ClassListContains("overflow-hidden"), Is.True);
        }

        [Test]
        public void Given_AMotionTextField_When_AnEnterShowsATextOverflowUtility_Then_ItsInputBoxClips()
        {
            // Arrange / Act
            var element = MountOnFakeClock(EnterBeside("", "text-ellipsis", "", typeof(TextField)));
            var box = element.Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass);

            // Assert
            Assert.That(box.ClassListContains("overflow-hidden"), Is.True);
        }
    }
}
