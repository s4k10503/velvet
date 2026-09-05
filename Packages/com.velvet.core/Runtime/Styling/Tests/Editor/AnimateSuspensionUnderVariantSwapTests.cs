using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What the panel paints when a Motion's variant swap and a running animate-* motion both want the inline
    /// transition-property. Each case asserts the PAINTED value, which is where either failure shows up as
    /// itself: a driver's frame the engine is still transitioning towards instead of landing, and a swap that
    /// arrives at its target outright instead of tweening there. The bundled stylesheet is attached so the
    /// transition utilities resolve, and the panel runs on a fake clock so every painted mid-animation value is
    /// load-independent. GWT, one assert each.
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
    }
}
