using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a driver's narrowing of an element's transitions holds: it is given back once the element's
    /// classes stop transitioning what the driver writes, and a driver painting the face contends with a declared
    /// colour transition.
    /// </summary>
    [TestFixture]
    internal sealed class MotionNativeTransitionGuardNarrowingTests
    {
        private static readonly MethodInfo s_contends = typeof(MotionNativeTransitionGuard).GetMethod("Contends",
            BindingFlags.Static | BindingFlags.NonPublic, null,
            new[] { typeof(MotionTransitionSlots), typeof(MotionTransitionSlots) }, null)!;

        // GREEN_ON_BASE(characterization): the base releases a narrowing whose transition class is removed.
        [Test]
        public void Given_ANarrowedElementWhoseTransitionClassIsRemoved_When_TheNarrowingIsSynced_Then_NoTransitionPropertyIsLeftInline()
        {
            // Arrange
            var element = new VisualElement();
            element.AddToClassList("transition-opacity");
            var owner = new object();
            MotionNativeTransitionGuard.SyncNarrowing(element, owner, MotionTransitionSlots.Opacity, StyleLonghandSet.Empty);
            element.RemoveFromClassList("transition-opacity");

            // Act
            MotionNativeTransitionGuard.SyncNarrowing(element, owner, MotionTransitionSlots.Opacity, StyleLonghandSet.Empty);

            // Assert
            Assert.That(element.style.transitionProperty.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base has a declared colour transition contend with a face driver.
        [Test]
        public void Given_ADeclaredColourTransition_When_AFaceDriverIsAskedAbout_Then_TheyContend()
        {
            // Act
            var contends = (bool)s_contends.Invoke(null,
                new object[] { MotionTransitionSlots.Color, MotionTransitionSlots.Face });

            // Assert
            Assert.That(contends, Is.True);
        }
    }
}
