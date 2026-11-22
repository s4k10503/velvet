using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the callbacks the styling layer registers for the first attach to a panel alone: a second attach
    /// event reaching the element does not run the callback again.
    /// </summary>
    [TestFixture]
    internal sealed class FireOnceAttachCallbackTests : PanelTestBase
    {
        // GREEN_ON_BASE(refactor): the base's handler unregisters itself before it runs, so a second event finds
        // nothing there either; registering it Once moves that off the handler.
        [Test]
        public void Given_AnAnimationDeferredUntilAttached_When_TwoAttachEventsReachTheElement_Then_ItRunsOnce()
        {
            // Arrange
            var element = new VisualElement();
            var pending = Activator.CreateInstance(
                typeof(StyleAnimationScheduler).GetNestedType("PendingAnimation", BindingFlags.NonPublic)!, true);
            var runs = 0;
            typeof(StyleAnimationScheduler)
                .GetMethod("DeferUntilAttached", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new[] { element, pending, (Action)(() => runs++) });

            // Act
            using var first = AttachToPanelEvent.GetPooled();
            element.SimulateEvent(first);
            using var second = AttachToPanelEvent.GetPooled();
            element.SimulateEvent(second);

            // Assert
            Assert.That(runs, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(refactor): see the deferred-animation case above.
        [Test]
        public void Given_AnAnimateLoopAttachedOffPanel_When_ASecondAttachEventFollowsTheRealOne_Then_TheTickIsNotScheduledAgain()
        {
            // Arrange
            var element = new VisualElement();
            var binding = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Hue, 4f), false);
            _window.rootVisualElement.Add(element);
            var scheduled = binding.Scheduled;

            // Act
            using var again = AttachToPanelEvent.GetPooled();
            element.SimulateEvent(again);

            // Assert — a tick the real attach never scheduled is null on both sides, which the first term refuses.
            Assert.That(binding.Scheduled, Is.Not.Null.And.SameAs(scheduled));
        }

        // GREEN_ON_BASE(refactor): see the deferred-animation case above.
        [Test]
        public void Given_AClassDeferredUntilAttached_When_ASecondAttachEventFollowsTheRealOne_Then_TheClassIsNotTakenUpAgain()
        {
            // Arrange — the deferred list is put back after the real attach drained it, so a handler that ran again
            // would drain it once more.
            var element = new VisualElement();
            StyleUserClassProperties.TryGet(element, "app-card", out _);
            _window.rootVisualElement.Add(element);
            var deferred = (ConditionalWeakTable<VisualElement, List<string>>)typeof(StyleUserClassProperties)
                .GetField("s_unattached", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            deferred.Add(element, new List<string> { "velvet-engine-owned" });

            // Act
            using var again = AttachToPanelEvent.GetPooled();
            element.SimulateEvent(again);

            // Assert
            Assert.That(deferred.TryGetValue(element, out _), Is.True);
        }

        [Test]
        public void Given_AClassDeferredOnAnElementThatWasCleanedUp_When_TheElementAttaches_Then_OnlyTheOtherElementsClassIsProbed()
        {
            // Arrange — two elements defer a class each; the first is torn down as a pool return would, the second
            // is the control that shows an attach does send a probe out.
            using var reconciler = new Reconciler();
            var pooled = new VisualElement();
            var kept = new VisualElement();
            StyleUserClassProperties.TryGet(pooled, "app-pooled", out _);
            StyleUserClassProperties.TryGet(kept, "app-kept", out _);
            new FiberElementCleaner(reconciler.Context).CleanupElement(pooled);

            // Act — the first element is reused and both attach.
            _window.rootVisualElement.Add(pooled);
            _window.rootVisualElement.Add(kept);

            // Assert
            var probes = _window.rootVisualElement.Query<VisualElement>(className: StyleUserClassProperties.MarkerClass).ToList();
            Assert.That(probes.Count, Is.EqualTo(1));
        }
    }
}
