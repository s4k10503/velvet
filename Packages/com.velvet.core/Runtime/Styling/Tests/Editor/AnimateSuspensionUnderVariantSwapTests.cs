using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What the panel paints when a Motion's variant swap and a per-frame driver — a running animate-* motion,
    /// or a layoutId spring — both want the inline transition-property. Each case asserts the PAINTED value,
    /// which is where either failure shows up as itself: a driver's frame the engine is still transitioning
    /// towards instead of landing, and a swap that arrives at its target outright instead of tweening there.
    /// The bundled stylesheet is attached so the transition utilities resolve, and the panel runs on a fake
    /// clock so every painted mid-animation value is load-independent. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateSuspensionUnderVariantSwapTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";
        // Long enough that a tween is still visibly mid-flight one paint after the swap fires.
        private const float SwapDurationSec = 0.35f;

        private double _now;

        private static readonly Dictionary<string, MotionVariant> s_variants =
            new() { ["hidden"] = "opacity-0", ["visible"] = "opacity-100" };

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, () => _now);
        }

        private static VNode Card(string className, string label) => V.Motion(
            className: className, name: "card", variants: s_variants, animate: label,
            transition: new StyleTransitionConfig { DurationSec = SwapDurationSec });

        private VisualElement MountCard(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, Card(className, "hidden"));
            var element = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(element.panel);
            return element;
        }

        // Re-renders the card from the "hidden" variant to the "visible" one, changing its static classes at
        // the same time — the patch on which FiberNodePatcher runs the variant swap and the animate-* class
        // passes one after the other.
        private void FlipVariant(string fromClassName, string toClassName) => _mounted!.Root.Reconciler.Reconcile(
            _window.rootVisualElement,
            new VNode[] { Card(fromClassName, "hidden") },
            new VNode[] { Card(toClassName, "visible") });

        // A layoutId card placed after a spacer: widening the spacer moves the card without touching the card's
        // own styles, which a swap's transition-property would otherwise tween into the move before the
        // layoutId driver ever saw it.
        private static VNode LayoutIdRow(int spacerWidth, string className, string label, StyleTransitionConfig transition)
            => V.Div(className: "flex-row", children: new VNode[]
            {
                V.Div(className: $"w-[{spacerWidth}px] h-[40px]"),
                V.Motion(className: className, name: "card", variants: s_variants, animate: label, layoutId: "card",
                    transition: transition ?? new StyleTransitionConfig { DurationSec = SwapDurationSec }),
            });

        private VisualElement MountLayoutIdRow(string className, StyleTransitionConfig transition = null)
        {
            _mounted = V.Mount(_window.rootVisualElement, LayoutIdRow(0, className, "hidden", transition));
            var element = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(element.panel);
            return element;
        }

        private void PatchLayoutIdRow(string className, (int Spacer, string Label) from, (int Spacer, string Label) to,
            StyleTransitionConfig transition = null) => _mounted!.Root.Reconciler.Reconcile(
            _window.rootVisualElement,
            new VNode[] { LayoutIdRow(from.Spacer, className, from.Label, transition) },
            new VNode[] { LayoutIdRow(to.Spacer, className, to.Label, transition) });

        // A swap short enough to end while the spring it runs beside is still moving fast.
        private static readonly StyleTransitionConfig s_shortSwap = new() { DurationSec = 0.1f };

        private bool LayoutIdSpringRuns(VisualElement element)
            => _mounted!.Root.Reconciler.Context.LayoutIdTicks.ContainsKey(element);

        // Frame by frame rather than in one step, so the spring integrates the way a live panel would drive it.
        private void RunFrames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                RunScheduledWork(0.02);
                PaintAfter(0.0);
            }
        }

        // How far the painted translate trails the one the spring wrote, or NaN while no spring runs, so a case
        // whose spring never started cannot pass.
        private float SpringTrail(VisualElement element) => LayoutIdSpringRuns(element)
            ? element.resolvedStyle.translate.x - element.style.translate.value.x.value
            : float.NaN;

        private StyleAnimateBinding BindingOf(VisualElement element)
        {
            _mounted!.Root.Reconciler.Context.AnimationBindings.TryGetValue(element, out var binding);
            return binding;
        }

        // Steps the clock and pumps the panel's timer scheduler, which is what runs the scheduler's deferred
        // class swap and its completion timeout (the EditMode player loop never delivers either).
        private void RunScheduledWork(double seconds)
        {
            _now += seconds;
            EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
        }

        // A styles pass, then the clock stepped and the animation phase run — the pair a live panel performs
        // once per frame. What resolvedStyle reports afterwards is what would be painted.
        private void PaintAfter(double seconds)
        {
            ForcePanelUpdate(_window.rootVisualElement.panel);
            _now += seconds;
            EditorPanelTestHelpers.DriveAnimationsOnce(_window.rootVisualElement.panel);
        }

        // Drives the swap the way a live panel would: the from-state resolves, the deferred class swap fires,
        // and the panel paints one frame a short way into the tween.
        private void PlayOutTheSwap()
        {
            ForcePanelUpdate(_window.rootVisualElement.panel);
            RunScheduledWork(0.02);
            PaintAfter(0.05);
        }

        [Test]
        public void Given_APulseSuspendedByADuration_When_AVariantSwapHasRunItsCourse_Then_TheNextPulseFrameIsPaintedAsWritten()
        {
            // Arrange — duration-300 leaves UI Toolkit's initial transition-property of `all` standing, so the
            // opacity the pulse writes every frame is transitionable and the pulse suspends the element at
            // attach. The swap that follows holds the same inline slot for its own length and clears it on
            // completion, which is the moment the suspension has to survive.
            var element = MountCard("w-[40px] h-[40px] bg-red-500 duration-300 animate-pulse");
            var binding = BindingOf(element);
            FlipVariant("w-[40px] h-[40px] bg-red-500 duration-300 animate-pulse",
                "w-[40px] h-[40px] bg-red-500 duration-300 animate-pulse");
            RunScheduledWork(0.02);
            RunScheduledWork(0.5);
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);
            PaintAfter(1.0);

            // Act — one pulse frame at the trough, then the panel paints.
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            PaintAfter(0.05);

            // Assert — the frame's own value. Unsuspended, the paint would still be most of the way back at
            // full opacity, the engine transitioning towards the write over duration-300.
            Assert.That(element.resolvedStyle.opacity, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void Given_ASpinSuspendingTheElement_When_APatchStopsItWhileSwappingAVariant_Then_TheSwapStillTweens()
        {
            // Arrange — transition-transform names rotate, so the spin suspends the element; it names no
            // opacity, so once the suspension is handed back the swap has only the inline transition-property
            // it wrote itself to tween opacity with.
            MountCard("w-[40px] h-[40px] bg-red-500 transition-transform animate-spin");

            // Act — one patch both stops the spin and swaps the variant.
            FlipVariant("w-[40px] h-[40px] bg-red-500 transition-transform animate-spin",
                "w-[40px] h-[40px] bg-red-500 transition-transform");
            PlayOutTheSwap();

            // Assert — a tween is still short of opacity-100 one paint in; a swap whose transition-property was
            // taken off the element lands on the target outright.
            Assert.That(_window.rootVisualElement.Q<VisualElement>("card").resolvedStyle.opacity, Is.LessThan(0.9f));
        }

        [Test]
        public void Given_AnElementTransitioningTransform_When_APatchStartsASpinWhileSwappingAVariant_Then_TheSwapStillTweens()
        {
            // Arrange — the same pair of layers as the case above, in the other order: here the swap is already
            // holding the slot when the spin attaches and asks to suspend it.
            MountCard("w-[40px] h-[40px] bg-red-500 transition-transform");

            // Act — one patch both starts the spin and swaps the variant.
            FlipVariant("w-[40px] h-[40px] bg-red-500 transition-transform",
                "w-[40px] h-[40px] bg-red-500 transition-transform animate-spin");
            PlayOutTheSwap();

            // Assert — as above: short of the target while tweening, exactly on it if the swap was cancelled.
            Assert.That(_window.rootVisualElement.Q<VisualElement>("card").resolvedStyle.opacity, Is.LessThan(0.9f));
        }

        [Test]
        public void Given_ASpinSuspendingTheElement_When_APatchSwapsAVariantUnderIt_Then_TheNextSpinFrameIsPaintedAsWritten()
        {
            // Arrange — the spin already holds the suspension when the swap writes its own transition-property
            // over it, which names rotate like everything else.
            var element = MountCard("w-[40px] h-[40px] bg-red-500 transition-transform animate-spin");
            var binding = BindingOf(element);
            FlipVariant("w-[40px] h-[40px] bg-red-500 transition-transform animate-spin",
                "w-[40px] h-[40px] bg-red-500 transition-transform animate-spin");
            ForcePanelUpdate(_window.rootVisualElement.panel);
            RunScheduledWork(0.02);
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);
            PaintAfter(0.05);

            // Act — a quarter-turn frame while the swap is still tweening.
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);
            PaintAfter(0.05);

            // Assert — the frame's own angle; under the swap's list the paint is still on its way from 0.
            Assert.That(element.resolvedStyle.rotate.angle.ToDegrees(), Is.EqualTo(90f).Within(0.5f));
        }

        [Test]
        public void Given_ALayoutIdSpringStartingUnderAPendingSwap_When_TheSwapFires_Then_TheSwapStillTweens()
        {
            // Arrange — one patch both moves the card and swaps its variant; the spring starts on the layout
            // pass, before the swap's deferred class change fires.
            var element = MountLayoutIdRow("w-[40px] h-[40px] bg-red-500");
            PatchLayoutIdRow("w-[40px] h-[40px] bg-red-500", (0, "hidden"), (200, "visible"));

            // Act
            PlayOutTheSwap();

            // Assert — the spring ran, and the swap is still short of opacity-100 one paint in.
            Assert.That((LayoutIdSpringRuns(element), element.resolvedStyle.opacity < 0.9f), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base lands the spring's frame by writing none over the swap, and
        // the swap's list has to be narrowed rather than left for that to survive.
        [Test]
        public void Given_ALayoutIdSpringStartingUnderAPendingSwap_When_ThePanelPaints_Then_TheSpringsFrameIsPaintedAsWritten()
        {
            // Arrange
            var element = MountLayoutIdRow("w-[40px] h-[40px] bg-red-500");
            PatchLayoutIdRow("w-[40px] h-[40px] bg-red-500", (0, "hidden"), (200, "visible"));

            // Act
            PlayOutTheSwap();

            // Assert — painted where the spring put it; under the swap's list it would still be near 0.
            Assert.That(SpringTrail(element), Is.EqualTo(0f).Within(0.5f));
        }

        [Test]
        public void Given_ASwapMidTween_When_ALayoutIdSpringStartsUnderIt_Then_TheSwapKeepsTweening()
        {
            // Arrange — the swap is already painting its way to opacity-100 when a second patch moves the card.
            var element = MountLayoutIdRow("w-[40px] h-[40px] bg-red-500");
            PatchLayoutIdRow("w-[40px] h-[40px] bg-red-500", (0, "hidden"), (0, "visible"));
            PlayOutTheSwap();
            PatchLayoutIdRow("w-[40px] h-[40px] bg-red-500", (0, "visible"), (200, "visible"));

            // Act — the layout pass that starts the spring, and one paint.
            PaintAfter(0.02);

            // Assert — the spring ran, and the running tween was narrowed rather than landed.
            Assert.That((LayoutIdSpringRuns(element), element.resolvedStyle.opacity < 0.9f), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ASwapWithPerPropertyTransitions_When_ALayoutIdSpringStartsUnderIt_Then_BothLandAsTheirOwn()
        {
            // Arrange — translate is listed first on a near-instant duration, so an opacity left paired with
            // translate's timing after translate is taken out would land at once.
            var transition = new StyleTransitionConfig
            {
                DurationSec = SwapDurationSec,
                PropertyOverrides = new[]
                {
                    new StylePropertyTransition("translate", durationSec: 0.01f),
                    new StylePropertyTransition("opacity", durationSec: SwapDurationSec),
                },
            };
            var element = MountLayoutIdRow("w-[40px] h-[40px] bg-red-500", transition);
            PatchLayoutIdRow("w-[40px] h-[40px] bg-red-500", (0, "hidden"), (200, "visible"), transition);

            // Act
            PlayOutTheSwap();

            // Assert — the spring's frame is painted as written, and the swap is still short of opacity-100.
            Assert.That((System.Math.Abs(SpringTrail(element)) < 0.5f, element.resolvedStyle.opacity < 0.9f),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALayoutIdSpringOnATransitionColorsElement_When_ItStartsUnderAPendingSwap_Then_TheSpringsFrameIsPaintedAsWritten()
        {
            // Arrange — transition-colors names nothing the spring drives, so the spring takes no suspension
            // here; the swap's own list is what would transition its writes.
            var element = MountLayoutIdRow("w-[40px] h-[40px] bg-red-500 transition-colors");
            PatchLayoutIdRow("w-[40px] h-[40px] bg-red-500 transition-colors", (0, "hidden"), (200, "visible"));

            // Act
            PlayOutTheSwap();

            // Assert
            Assert.That(SpringTrail(element), Is.EqualTo(0f).Within(0.5f));
        }

        // GREEN_ON_BASE(characterization): the base keeps a spring on a transition-transform element suspended
        // past the swap beside it, which the owner decision under a held list must go on doing.
        [Test]
        public void Given_ALayoutIdSpringOnATransitionTransformElement_When_TheSwapBesideItEnds_Then_TheSpringsFrameIsStillPaintedAsWritten()
        {
            // Arrange — transition-transform names translate, so after the swap the spring still needs the
            // suspension.
            var element = MountLayoutIdRow("w-[40px] h-[40px] bg-red-500 transition-transform", s_shortSwap);
            PatchLayoutIdRow("w-[40px] h-[40px] bg-red-500 transition-transform", (0, "hidden"), (400, "visible"),
                s_shortSwap);
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act — past the swap's completion, with the spring still moving fast.
            RunFrames(10);

            // Assert
            Assert.That(SpringTrail(element), Is.EqualTo(0f).Within(0.5f));
        }
    }
}
