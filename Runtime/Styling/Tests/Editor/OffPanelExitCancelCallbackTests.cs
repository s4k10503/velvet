using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// When an exit animation starts while its element is off-panel, the scheduler defers scheduling until the
    /// element attaches by registering an <see cref="AttachToPanelEvent"/> callback. That callback unregisters
    /// itself only when it fires (on attach), so a cancel-before-attach (the key is re-added, or the subtree is
    /// disposed, while still detached) would leave it — and the closure pinning the animation's pooled state —
    /// dangling on the element, surviving even pool reuse. Cancelling the exit must remove it. The handler's
    /// presence is read via the element's bubble-up handler flag (the only registration the off-panel exit makes).
    /// GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class OffPanelExitCancelCallbackTests
    {
        [Test]
        public void Given_AnOffPanelExitRegisteredItsDeferredAttachCallback_When_TheExitIsCancelledBeforeAttach_Then_TheCallbackIsUnregistered()
        {
            // Arrange — an off-panel element whose exit deferred scheduling by registering an attach callback.
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            Assume.That(element.panel, Is.Null, "Precondition: the element is off-panel");
            scheduler.PlayExit(element, StyleTransition.Fade.With(durationSec: 0.1f), onComplete: null);
            Assume.That(HasBubbleUpHandlers(element), Is.True,
                "Precondition: the off-panel exit registered its deferred-attach callback");

            // Act — the exit is cancelled before the element ever attaches.
            scheduler.CancelExit(element);

            // Assert — the deferred-attach callback was removed (no dangling listener / pinned closure).
            Assert.That(HasBubbleUpHandlers(element), Is.False,
                "Cancelling an off-panel exit before attach unregisters its deferred-attach callback.");
        }

        /// <summary>
        /// Reads <c>CallbackEventHandler.HasBubbleUpHandlers()</c> (internal) by reflection. The off-panel exit's
        /// only registration is the bubble-up <see cref="AttachToPanelEvent"/> callback, so this flag tracks
        /// exactly its presence — the only way to observe the otherwise-encapsulated callback registry.
        /// </summary>
        private static bool HasBubbleUpHandlers(VisualElement element)
        {
            var method = typeof(CallbackEventHandler).GetMethod(
                "HasBubbleUpHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "Could not find CallbackEventHandler.HasBubbleUpHandlers(). The UI Toolkit internal layout may have changed.");
            return (bool)method.Invoke(element, null);
        }
    }
}
