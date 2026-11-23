using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that releasing an element's deferred user-class reads takes its attach callback off it.
    /// </summary>
    internal sealed class StyleUserClassPropertiesReleaseTests
    {
        // GREEN_ON_BASE(characterization): the base already unregisters the attach callback when the deferral is released.
        [Test]
        public void Given_AnOffPanelElementThatDeferredAUserClass_When_TheDeferralIsReleased_Then_NoAttachCallbackRemains()
        {
            // Arrange
            var element = new VisualElement();
            StyleUserClassProperties.TryGet(element, "app-own-class", out _);
            var registered = HasBubbleUpHandlers(element);

            // Act
            StyleUserClassProperties.ReleaseDeferred(element);

            // Assert — true where the deferral registered nothing, which no expected value equals.
            Assert.That(registered ? HasBubbleUpHandlers(element) : true, Is.False);
        }

        private static bool HasBubbleUpHandlers(VisualElement element)
        {
            var method = typeof(CallbackEventHandler).GetMethod(
                "HasBubbleUpHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("CallbackEventHandler.HasBubbleUpHandlers() was not found.");
            return (bool)method.Invoke(element, null);
        }
    }
}
