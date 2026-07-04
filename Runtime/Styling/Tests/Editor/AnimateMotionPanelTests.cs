using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Applied-frame coverage for the animate-* motions on a real panel. The pan modes read the element's
    /// resolved box, so they need a laid-out <see cref="UnityEditor.EditorWindow"/> panel; Hue is geometry-free.
    /// The scheduler never ticks in EditMode, so each frame is driven explicitly via
    /// <see cref="StyleAnimateDriver.ApplyFrame"/> at a chosen phase — the same pure path the runtime tick
    /// calls. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateMotionPanelTests : PanelTestBase
    {
        // Mounts a single Div and returns (element, its animation binding or null). animate-* is wrapper-less,
        // so the element is the mounted root's first child.
        private (VisualElement element, StyleAnimateBinding binding) Mount(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var element = _window.rootVisualElement[0];
            _mounted.Root.Reconciler.Context.AnimationBindings.TryGetValue(element, out var binding);
            return (element, binding);
        }

        [Test]
        public void Given_GradientPanAtMidLoop_When_FrameApplied_Then_BackgroundPannedByBoxWidth()
        {
            // bg-gradient-to-r flows horizontally → the 100px box pans X; the triangle peak at t=0.5 offsets
            // by one full box width (negative = leftward).
            var (element, binding) = Mount("w-[100px] h-[40px] bg-gradient-to-r from-red-500 to-blue-500 animate-gradient");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            Assert.That(element.style.backgroundPositionX.value.offset.value, Is.EqualTo(-100f));
        }

        [Test]
        public void Given_GradientPan_When_Attached_Then_BackgroundIsOversizedOnPanAxis()
        {
            // The Gradient pan oversizes the pan axis to 200% so the box window never reveals a transparent edge.
            var (element, _) = Mount("w-[100px] h-[40px] bg-gradient-to-r from-red-500 to-blue-500 animate-gradient");

            Assert.That(element.style.backgroundSize.value.x.value, Is.EqualTo(200f));
        }

        [Test]
        public void Given_VerticalGradientPanAtMidLoop_When_FrameApplied_Then_PansYByBoxHeight()
        {
            // bg-gradient-to-b flows vertically → the pan axis is Y; t=0.5 offsets by the 40px box height.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-gradient-to-b from-red-500 to-blue-500 animate-gradient");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            Assert.That(element.style.backgroundPositionY.value.offset.value, Is.EqualTo(-40f));
        }

        [Test]
        public void Given_Hue_When_FrameAppliedAtMidLoop_Then_HueRotateFilterAtHalfTurn()
        {
            // animate-hue cycles the hue-rotate filter; t=0.5 is a 180deg rotation.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-hue");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            var f = element.style.filter.value[0];

            Assert.That((f.type, f.GetParameter(0).floatValue), Is.EqualTo((FilterFunctionType.HueRotate, 180f)));
        }

        [Test]
        public void Given_HueSecondFrame_When_FrameApplied_Then_AFreshFilterReferenceIsWritten()
        {
            // UI Toolkit dirties an element's filter for repaint only when the backing list REFERENCE changes
            // (it ref-compares, not content-compares). So a continuous hue MUST write a fresh list each frame;
            // reusing one mutated list would repaint frame 1 then freeze. Asserting the second frame's list is a
            // distinct reference pins that (RED if the driver reuses one list, GREEN with a fresh list per frame).
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-hue");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);
            var first = element.style.filter.value;
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            var second = element.style.filter.value;

            Assert.That(ReferenceEquals(first, second), Is.False);
        }

        [Test]
        public void Given_ShimmerAtLoopStart_When_FrameApplied_Then_BandSitsOffTheLeadingEdge()
        {
            // Shimmer sweeps one-way; t=0 the transparent-ended band sits one box width off the leading edge.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-slate-700 bg-gradient-to-r from-transparent via-white to-transparent animate-shimmer");
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            Assert.That(element.style.backgroundPositionX.value.offset.value, Is.EqualTo(-100f));
        }

        [Test]
        public void Given_StaticFilterAndHue_When_FrameApplied_Then_HueOwnsTheFilterSlot()
        {
            // Documented limitation: animate-hue OWNS style.filter while active — it does not compose with a
            // static filter-* on the same element. After a hue frame the slot holds only the hue rotation.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 grayscale-[.5] animate-hue");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            Assert.That(element.style.filter.value.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_HueWithNoGradient_When_Mounted_Then_BindingStillAttaches()
        {
            // Hue is not a pan, so it does not require a gradient — it attaches on any element.
            var (_, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-hue");

            Assert.That(binding, Is.Not.Null);
        }

        [Test]
        public void Given_PulseAtMidLoop_When_FrameApplied_Then_OpacityAtHalf()
        {
            // animate-pulse oscillates opacity; the trough at t=0.5 is the half-opacity minimum.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-pulse");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Given_PulseSecondFrame_When_FrameApplied_Then_OpacityReflectsNewPhase()
        {
            // The applied path must re-derive opacity from the phase each frame (the value-compare analog of the
            // Hue fresh-reference test). Driving t=0.5 then t=0 must land at full opacity — a frozen / hardcoded
            // value in the Pulse arm of ApplyFrame would not track the second phase. RED if it ignores t.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-pulse");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            Assert.That(element.style.opacity.value, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_RunningPulse_When_Detached_Then_InlineOpacityCleared()
        {
            // Detach drops the per-frame inline opacity so the element returns to its class-driven value.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-pulse");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            StyleAnimateDriver.Detach(element, binding);

            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_PanModeWithNoGradient_When_Mounted_Then_NoBinding()
        {
            // animate-gradient with nothing to pan is inert (parity with a lone gradient stop).
            var (_, binding) = Mount("w-[100px] h-[40px] animate-gradient");

            Assert.That(binding, Is.Null);
        }

        [Test]
        public void Given_RunningPan_When_Detached_Then_StretchToFillRestored()
        {
            // Detach restores the gradient's 100% stretch-to-fill (the gradient itself may still be bound).
            var (element, binding) = Mount("w-[100px] h-[40px] bg-gradient-to-r from-red-500 to-blue-500 animate-gradient");
            StyleAnimateDriver.Detach(element, binding);

            Assert.That(element.style.backgroundSize.value.x.value, Is.EqualTo(100f));
        }
    }
}
