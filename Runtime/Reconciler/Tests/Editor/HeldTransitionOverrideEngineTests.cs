using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins two UI Toolkit behaviours StyleAnimationScheduler.LandOnHeldTransition is built on when it lands one
    /// property of a running `all` tween: a zero-duration entry after `all` does not stop `all` timing that
    /// property, which is why a changed value gets a 1ms entry instead; and leaving the property out of the list
    /// rests it at the running tween's old target, which is how a value the pose repeats from that target lands.
    /// </summary>
    [TestFixture]
    internal sealed class HeldTransitionOverrideEngineTests : MotionSimulatedPanelTestsBase
    {
        public override void SetUp()
        {
            base.SetUp();
            VelvetStyleUtilities.AttachTo(Root);
        }

        // An element tweening opacity from 0 toward 1 under an inline `all`, a few frames in.
        private VisualElement TweeningBox()
        {
            var box = new VisualElement();
            box.AddToClassList("opacity-0");
            box.style.transitionProperty = new List<StylePropertyName> { new("all") };
            box.style.transitionDuration = new List<TimeValue> { new(300, TimeUnit.Millisecond) };
            Root.Add(box);
            Tick();
            box.RemoveFromClassList("opacity-0");
            box.AddToClassList("opacity-100");
            Tick();
            Tick();
            Tick();
            return box;
        }

        // GREEN_ON_BASE(characterization): UI Toolkit's own behaviour, which no production code runs in.
        [Test]
        public void Given_AnAllTweenRunning_When_AZeroDurationEntryForItsPropertyIsAppended_Then_ThePropertyKeepsTweening()
        {
            // Arrange
            var box = TweeningBox();
            var before = box.resolvedStyle.opacity;

            // Act
            box.style.transitionProperty = new List<StylePropertyName> { new("all"), new("opacity") };
            box.style.transitionDuration = new List<TimeValue>
            {
                new(300, TimeUnit.Millisecond), new(0, TimeUnit.Millisecond),
            };
            box.RemoveFromClassList("opacity-100");
            box.AddToClassList("opacity-50");
            Tick();

            // Assert — gated on the tween being mid-way, where tweening and landing read apart.
            var after = before > 0.05f && before < 0.45f ? box.resolvedStyle.opacity : float.NaN;
            Assert.That(after, Is.InRange(0.05f, 0.45f));
        }

        // GREEN_ON_BASE(characterization): UI Toolkit's own behaviour, which no production code runs in.
        [Test]
        public void Given_AnAllTweenRunning_When_ItsPropertyIsLeftOutOfTheList_Then_ThePropertyRestsAtTheOldTarget()
        {
            // Arrange
            var box = TweeningBox();
            var before = box.resolvedStyle.opacity;

            // Act
            box.style.transitionProperty = new List<StylePropertyName> { new("translate") };
            box.RemoveFromClassList("opacity-100");
            box.AddToClassList("opacity-50");
            Tick();
            Tick();
            Tick();

            // Assert
            var after = before > 0.05f && before < 0.45f ? box.resolvedStyle.opacity : float.NaN;
            Assert.That(after, Is.EqualTo(1f).Within(1e-3f));
        }
    }
}
