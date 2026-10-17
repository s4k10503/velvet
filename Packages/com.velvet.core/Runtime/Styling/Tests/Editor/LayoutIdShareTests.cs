using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class LayoutIdShareTests
    {
        [Test]
        public void Given_ALeadMovingSharedFromAHolder_When_ItStartsAgainTakingABoxFromAnotherHolder_Then_ItHoldsNoCrossfadeProgress()
        {
            // Arrange — Share is private and is reached by reflection.
            var projection = new LayoutIdProjection(new VisualElement(), default, default, Vector3.zero, Vector3.one)
            {
                Progress = LayoutIdTiming.From(new StyleTransitionConfig { DurationSec = 1f }).Start(),
                Shared = true,
                Moving = true,
            };
            var share = typeof(MotionLayoutIdDriver).GetMethod("Share", BindingFlags.NonPublic | BindingFlags.Static);

            // Act
            share.Invoke(null, new object[] { projection, true, true, true, null });

            // Assert
            Assert.That(float.IsNaN(projection.FadeAt), Is.True);
        }
    }
}
