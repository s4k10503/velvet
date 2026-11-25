using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a transition-behavior hold leaves no callback of its own on its root once given back, as a pooled portal
    /// row or a disposed mount's target goes on to be used elsewhere.
    /// </summary>
    internal sealed class StyleTransitionHoldTests
    {
        // GREEN_ON_BASE(characterization): the base's hold registers its run callback alone and gives it back.
        // Leaving `_root.UnregisterCallback(s_onEnd);` out of StyleTransitionBehavior's Release reddens it.
        [Test]
        public void Given_AHeldRoot_When_ItsHoldIsGivenBack_Then_ItKeepsNoCallbackTheHoldRegistered()
        {
            // Arrange
            var root = new VisualElement();
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(root);
            var hold = StyleTransitionBehavior.Hold(root);
            var held = CallbackRegistryProbe.BubbleUpCallbackCount(root);

            // Act
            hold.Dispose();

            // Assert — -1 where the hold registered nothing to give back.
            Assert.That(held > before ? CallbackRegistryProbe.BubbleUpCallbackCount(root) : -1, Is.EqualTo(before));
        }
    }
}
