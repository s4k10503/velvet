using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class MotionOpacityOwnershipTests : MotionSimulatedPanelTestsBase
    {
        private static readonly LayoutIdFade Half = new(0f, 1f, 0.5f);

        private VisualElement Drawn()
        {
            var element = new VisualElement();
            Root.Add(element);
            element.style.opacity = 0.8f;
            Tick();
            MotionOpacity.Draw(element, Half, 0f);
            return element;
        }

        // Plays the tween whose one-second linear timing Carrying holds inline.
        private StyleAnimationScheduler _scheduler;

        // A drawn element under a tween holding a one-second opacity transition inline, its own opacity carried
        // towards 0.2; own is a transition duration the element carries of its own, if any.
        private VisualElement Carrying(List<TimeValue> own = null)
        {
            var element = Drawn();
            if (own != null) element.style.transitionDuration = own;
            element.style.transitionProperty = new List<StylePropertyName> { new("opacity") };
            _scheduler = new StyleAnimationScheduler();
            _scheduler.PlayEnter(element, new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.Linear });
            MotionOpacity.Draw(element, Half, 0f);
            MotionOpacity.WriteTransitioned(element, 0.2f);
            return element;
        }

        // Ends the tween, and releases transition-property as a variant swap's end does.
        private void EndSwap(VisualElement element)
        {
            _scheduler.CancelEnter(element);
            element.style.transitionProperty = StyleKeyword.Null;
        }

        private static object Drawing(VisualElement element)
        {
            var table = typeof(MotionOpacity).GetField("s_drawing", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var args = new object[] { element, null };
            table.GetType().GetMethod("TryGetValue").Invoke(table, args);
            return args[1];
        }

        private static void Tail(VisualElement element, float seconds) =>
            typeof(MotionOpacity).GetMethod("StepTail", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { element, Drawing(element), (object)seconds });

        private static void Cancel(VisualElement element, string property, double elapsed)
        {
            using var evt = (TransitionCancelEvent)typeof(TransitionCancelEvent).BaseType
                .GetMethod("GetPooled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(StylePropertyName), typeof(double) }, null)
                .Invoke(null, new object[] { new StylePropertyName(property), elapsed });
            typeof(EventBase).GetProperty("currentTarget").SetValue(evt, element);
            typeof(MotionOpacity).GetMethod("OnTransitionCancel", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { evt });
        }

        [Test]
        public void Given_AChangingOwnOpacity_When_AFrameDriverWrites_Then_ItLandsImmediately()
        {
            // Arrange
            var element = Carrying();
            var faded = element.style.opacity.value != MotionOpacity.Own(element);
            // Act
            MotionOpacity.Write(element, 0.4f);
            // Assert
            Assert.That((faded, MotionOpacity.Own(element)), Is.EqualTo((true, 0.4f)));
        }

        [Test]
        public void Given_ADrawnOpacity_When_AFrameDriverWrites_Then_TheSlotUpdatesImmediately()
        {
            // Arrange
            var element = Drawn();
            // Act
            MotionOpacity.Write(element, 0.4f);
            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.2f));
        }

        [Test]
        public void Given_ACarryingOpacity_When_TheCrossfadeEnds_Then_TheSlotDropsTheFadeImmediately()
        {
            // Arrange
            var element = Carrying();
            var own = MotionOpacity.Own(element);
            var faded = element.style.opacity.value != own;
            // Act
            MotionOpacity.End(element);
            // Assert
            Assert.That((faded, element.style.opacity.value), Is.EqualTo((true, own)));
        }

        [Test]
        public void Given_ACarryingOpacity_When_TheTailSteps_Then_TheValueAdvances()
        {
            // Arrange
            var element = Carrying();
            MotionOpacity.End(element);
            // Act
            Tail(element, 0.5f);
            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Given_ACarryingOpacityOnAHeldClock_When_TheCrossfadeEndsAndThePanelTicks_Then_TheTailMovesOnlyAsFarAsTheClock()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = Carrying();
            MotionOpacity.End(element, clock);

            // Act
            for (var i = 0; i < 10; i++) Tick();
            var held = element.style.opacity.value;
            clock.Now += 0.25;
            Tick();

            // Assert — a quarter of the second-long carry from 0.8 to 0.2.
            Assert.That(new[] { held, element.style.opacity.value }, Is.EqualTo(new[] { 0.8f, 0.65f }).Within(1e-3f));
        }

        [Test]
        public void Given_ACarryingOpacityWhoseCrossfadeEndsTwice_When_TheClockSteps_Then_ItsTailStepsOnce()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = Carrying();
            MotionOpacity.End(element, clock);
            MotionOpacity.End(element, clock);

            // Act
            Tick();
            clock.Now += 0.25;
            Tick();

            // Assert — a quarter of the carry, where two tails would have stepped it half way.
            Assert.That(element.style.opacity.value, Is.EqualTo(0.65f).Within(1e-3f));
        }

        [Test]
        public void Given_ACarryingOpacity_When_TheTailLands_Then_TheDrawingIsReleased()
        {
            // Arrange
            var element = Carrying();
            MotionOpacity.Draw(element, Half, 0.5f);
            var carrying = Mathf.Abs(MotionOpacity.Own(element) - 0.5f) < 1e-3f;
            MotionOpacity.End(element);
            // Act
            Tail(element, 1f);
            element.style.opacity = 0.6f;
            // Assert
            Assert.That((carrying, MotionOpacity.Own(element)), Is.EqualTo((true, 0.6f)));
        }

        [Test]
        public void Given_ACarryingOpacity_When_ElapsedEqualsDuration_Then_EndReleasesTheDrawing()
        {
            // Arrange
            var element = Carrying();
            MotionOpacity.Draw(element, Half, 0.5f);
            var carrying = Mathf.Abs(MotionOpacity.Own(element) - 0.5f) < 1e-3f;
            MotionOpacity.Draw(element, Half, 0.5f);
            // Act
            MotionOpacity.End(element);
            element.style.opacity = 0.6f;
            // Assert
            Assert.That((carrying, MotionOpacity.Own(element)), Is.EqualTo((true, 0.6f)));
        }

        [Test]
        public void Given_ADrawnOpacity_When_ItIsForgotten_Then_TheCancellationCallbackIsRemoved()
        {
            // Arrange
            var element = Drawn();
            var registered = element.HasBubbleUpHandlers();
            // Act
            MotionOpacity.Forget(element);
            // Assert
            Assert.That((registered, element.HasBubbleUpHandlers()), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADrawnOpacity_When_ItFinishes_Then_TheCancellationCallbackIsRemoved()
        {
            // Arrange
            var element = Drawn();
            var registered = element.HasBubbleUpHandlers();
            // Act
            MotionOpacity.End(element);
            // Assert
            Assert.That((registered, element.HasBubbleUpHandlers()), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ACarryingOpacity_When_AnotherPropertyCancels_Then_ItsDurationStaysIntact()
        {
            // Arrange
            var element = Carrying();
            // Act
            Cancel(element, "width", 0.5);
            MotionOpacity.Draw(element, Half, 0.6f);
            // Assert
            Assert.That(MotionOpacity.Own(element), Is.EqualTo(0.44f).Within(0.001f));
        }

        [Test]
        public void Given_ACarryingOpacity_When_ItsNativeTransitionCancels_Then_TheCallbackIsRemoved()
        {
            // Arrange
            var element = Carrying();
            var registered = element.HasBubbleUpHandlers();
            // Act
            Cancel(element, "opacity", 0.5);
            // Assert
            Assert.That((registered, element.HasBubbleUpHandlers()), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADrawnOpacityWithoutACascade_When_AWriterClearsItsOwnSlot_Then_TheKnownOwnValueSurvives()
        {
            // Arrange
            var element = new VisualElement();
            element.style.opacity = 0.8f;
            MotionOpacity.Draw(element, Half, 0f);
            MotionOpacity.Write(element, 0.8f);
            // Act
            MotionOpacity.Write(element, StyleKeyword.Null);
            // Assert
            Assert.That(MotionOpacity.Own(element), Is.EqualTo(0.8f));
        }

        [Test]
        public void Given_ADrawnOpacityWithoutATransition_When_ItSteps_Then_NoSuspensionIsWritten()
        {
            // Arrange
            var element = Drawn();
            // Act
            MotionOpacity.Draw(element, Half, 0f);
            // Assert
            Assert.That(element.style.transitionProperty.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ADrawnOpacityWithADeclaredZeroDurationTransition_When_ItSteps_Then_TheNativeSlotIsSuspended()
        {
            // Arrange
            var element = Drawn();
            element.AddToClassList("transition-opacity");
            // Act
            MotionOpacity.Draw(element, Half, 0f);
            // Assert
            Assert.That(element.style.transitionProperty.value[0], Is.EqualTo(new StylePropertyName("none")));
        }

        [Test]
        public void Given_ADurationHeldInlineWithoutAClass_When_AMotionPatchSynchronizes_Then_ItDoesNotClaimTheTransition()
        {
            // Arrange
            var element = new VisualElement();
            element.style.transitionDuration = new List<TimeValue> { new(1f) };
            var held = element.style.transitionDuration.keyword == StyleKeyword.Undefined;
            // Act
            MotionNativeTransitionGuard.SyncSuspension(element, new object(), MotionTransitionSlots.Opacity);
            // Assert
            Assert.That((held, element.style.transitionProperty.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        // GREEN_ON_BASE(characterization): the base clears the inline timing at a tween's end, which drops what it held.
        // Restoring the element's own timing instead must drop the held timing all the same.
        [Test]
        public void Given_AHeldOpacityTransition_When_TheSwapEnds_Then_TheHeldTimingIsDropped()
        {
            // Arrange
            var element = Carrying();
            EndSwap(element);
            // Act
            MotionOpacity.Draw(element, Half, 0f);
            MotionOpacity.WriteTransitioned(element, 0.6f);
            MotionOpacity.Draw(element, Half, 0.5f);
            // Assert
            Assert.That(MotionOpacity.Own(element), Is.EqualTo(0.6f).Within(0.001f));
        }

        // GREEN_ON_BASE(characterization): the base clears the element's own duration at a tween's end, so no list is
        // left to read. Once that list is restored, the tween's end must still drop the held timing.
        [Test]
        public void Given_AHeldOpacityTransitionOnAnElementWithItsOwnDuration_When_TheSwapEnds_Then_TheHeldTimingIsDropped()
        {
            // Arrange — the swap's end puts the element's own 400ms list back in the slot.
            var element = Carrying(new List<TimeValue> { new(0.4f) });
            EndSwap(element);
            // Act
            MotionOpacity.Draw(element, Half, 0f);
            MotionOpacity.WriteTransitioned(element, 0.6f);
            MotionOpacity.Draw(element, Half, 0.5f);
            // Assert
            Assert.That(MotionOpacity.Own(element), Is.EqualTo(0.6f).Within(0.001f));
        }

        [Test]
        public void Given_AHeldOpacityTransition_When_AClassWriteStartsCarrying_Then_ItsDurationIsUsed()
        {
            // Arrange
            var element = Carrying();
            // Act
            MotionOpacity.Draw(element, Half, 0.5f);
            // Assert
            Assert.That(MotionOpacity.Own(element), Is.EqualTo(0.5f).Within(0.001f));
        }
        [Test]
        public void Given_ADrawnOpacity_When_ItsClassesChangeBeforeAClassWriterClearsInline_Then_TheNewCascadeIsReadImmediately()
        {
            // Arrange
            VelvetStyleUtilities.AttachTo(Root);
            var element = Drawn();
            element.AddToClassList("opacity-0");
            Tick();
            // Act
            MotionOpacity.WriteTransitioned(element, StyleKeyword.Null);
            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Given_ADrawnOpacityWithInlineTiming_When_AClassWriterStartsCarrying_Then_TheInlineDelayIsRead()
        {
            // Arrange
            VelvetStyleUtilities.AttachTo(Root);
            var element = Drawn();
            element.AddToClassList("transition-opacity");
            element.style.transitionDuration = new List<TimeValue> { new(1f) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
            element.style.transitionDelay = new List<TimeValue> { new(0.2f) };
            Tick();
            // Act
            MotionOpacity.WriteTransitioned(element, 0.2f);
            MotionOpacity.Draw(element, Half, 0.1f);
            // Assert
            Assert.That(MotionOpacity.Own(element), Is.EqualTo(0.8f).Within(0.001f));
        }

        [Test]
        public void Given_TwoIgnoredSubtrees_When_OneIsReleased_Then_TheOtherKeepsItsPickingHold()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var torn = new VisualElement();
            var kept = new VisualElement();
            var child = new VisualElement();
            Root.Add(torn);
            Root.Add(kept);
            kept.Add(child);
            var projection = new LayoutIdProjection(Root, StyleKeyword.Null, StyleKeyword.Null, Vector3.zero, Vector3.one);
            ctx.LayoutIdProjections.Add(kept, projection);
            LayoutIdPicking.Ignore(torn, projection);
            LayoutIdPicking.Ignore(kept, projection);
            // Act
            LayoutIdPicking.Release(torn, ctx);
            // Assert
            Assert.That((torn.pickingMode, kept.pickingMode, child.pickingMode),
                Is.EqualTo((PickingMode.Position, PickingMode.Ignore, PickingMode.Ignore)));
        }

        [Test]
        public void Given_ASettledHolderAtItsOwnBox_When_ThePatchStartsItsLayoutMove_Then_NoProjectionIsCreated()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var element = new VisualElement();
            element.style.width = 100f;
            element.style.height = 100f;
            Root.Add(element);
            Tick();
            ctx.ElementToLayoutId[element] = "card";
            ctx.LayoutIdMembers["card"] = new List<VisualElement> { element };
            ctx.LayoutIdRegistry["card"] = (element, null);
            var box = new LayoutIdBox(Root, element.layout, element.worldBound.center,
                element.worldBound.size, Vector2.one, null);
            var pending = new LayoutIdPendingSettle(box, element.layout, LayoutIdTiming.From(null)) { ReadOffItself = true };
            // Act
            typeof(MotionLayoutIdDriver).GetMethod("Start", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { element, pending, ctx });
            // Assert
            Assert.That(ctx.LayoutIdProjections.ContainsKey(element), Is.False);
        }

        private static void EndProjectionFade(VisualElement element, LayoutIdProjection projection) =>
            typeof(MotionLayoutIdDriver).GetMethod("WriteOpacity", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { element, projection, null });

        [Test]
        public void Given_AnEndedProjectionFade_When_AnInlineWritePrecedesItsNextPass_Then_TheTailAdoptsTheWrite()
        {
            // Arrange
            var element = Carrying();
            var projection = new LayoutIdProjection(Root, StyleKeyword.Null, StyleKeyword.Null, Vector3.zero, Vector3.one)
            {
                WritesOpacity = true,
            };
            EndProjectionFade(element, projection);
            element.style.opacity = 0.6f;
            // Act
            EndProjectionFade(element, projection);
            Tail(element, 0.1f);
            // Assert
            Assert.That(MotionOpacity.Own(element), Is.EqualTo(0.78f).Within(0.001f));
        }

    }
}
