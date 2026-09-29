using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetPreviewSmokeTestTests
    {
        private const string Group = nameof(VelvetPreviewSmokeTestTests);

        // Every story in this assembly mounts through this setup, so it throws only while a case asks it to.
        private static bool s_setupThrows;

        [VelvetPreviewSetup]
        private static void ThrowingSetup()
        {
            if (s_setupThrows) throw new InvalidOperationException("setup boom");
        }

        [TearDown]
        public void TearDown() => s_setupThrows = false;

        private static VNode Plain() => V.Div();

        private static VNode StoryThrows() => throw new InvalidOperationException("story boom");

        private static VNode RenderThrows() => V.Component(ThrowingRender, key: "child");

        private static VNode EffectThrows() => V.Component(ThrowingEffectRender, key: "child");

        private static VNode UpdateThrows() => V.Component(ThrowAfterEffectUpdateRender, key: "child");

        private static VNode BoundaryCatches() => V.Component(BoundaryRender, key: "boundary");

        [Component]
        private static VNode ThrowingRender() => throw new InvalidOperationException("render boom");

        [Component]
        private static VNode ThrowingEffectRender()
        {
            Hooks.UseEffect(ThrowingEffect, Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode ThrowAfterEffectUpdateRender()
        {
            var (updated, setUpdated) = Hooks.UseState(false);
            Hooks.UseEffect(() =>
            {
                setUpdated.Invoke(true);
                return (Action)null;
            }, Array.Empty<object>());
            if (updated) throw new InvalidOperationException("update boom");
            return V.Div();
        }

        private static Action ThrowingEffect() => throw new InvalidOperationException("effect boom");

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryRender()
        {
            Hooks.UseFallback((_, _) => V.Label(text: "fallback"));
            return V.Component(ThrowingRender, key: "child");
        }

        private static VelvetPreviewStory Story(string method) =>
            PreviewStoryTestFactory.Build(
                typeof(VelvetPreviewSmokeTestTests).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic),
                new VelvetPreviewAttribute { Name = method, Group = Group });

        private static string FailureOf(string method) =>
            VelvetPreviewSmokeTest.Run(new[] { Story(method) }).Single().Failure;

        [Test]
        public void Given_AStoryMethodThatThrows_When_SmokeTested_Then_ItFailsWithThatException()
        {
            // Act
            var failure = FailureOf(nameof(StoryThrows));

            // Assert
            Assert.That(failure, Is.EqualTo("InvalidOperationException: story boom"));
        }

        [Test]
        public void Given_AComponentThatThrowsWhileRenderingWithNoBoundary_When_SmokeTested_Then_ItFails()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: render boom");

            // Act
            var failure = FailureOf(nameof(RenderThrows));

            // Assert
            Assert.That(failure, Is.EqualTo("InvalidOperationException: render boom"));
        }

        [Test]
        public void Given_AnEffectThatThrowsWithNoBoundary_When_SmokeTested_Then_ItFails()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: effect boom");

            // Act
            var failure = FailureOf(nameof(EffectThrows));

            // Assert
            Assert.That(failure, Is.EqualTo("InvalidOperationException: effect boom"));
        }

        [Test]
        public void Given_AnEffectWhoseUpdateRendersAThrow_When_SmokeTested_Then_ItFails()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: update boom");

            // Act
            var failure = FailureOf(nameof(UpdateThrows));

            // Assert
            Assert.That(failure, Is.EqualTo("InvalidOperationException: update boom"));
        }

        [Test]
        public void Given_AnErrorABoundaryInTheStoryCatches_When_SmokeTested_Then_ItPasses()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: render boom");

            // Act
            var failure = FailureOf(nameof(BoundaryCatches));

            // Assert
            Assert.That(failure, Is.Null);
        }

        [Test]
        public void Given_ASetupThatThrows_When_ItsStoryIsSmokeTested_Then_ItFails()
        {
            // Arrange
            s_setupThrows = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ThrowingSetup"));
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: setup boom");

            // Act
            var failure = FailureOf(nameof(Plain));

            // Assert
            Assert.That(failure, Is.EqualTo("InvalidOperationException: setup boom"));
        }

        [Test]
        public void Given_TheProjectsStories_When_SmokeTested_Then_EachMountsWithoutAnError()
        {
            // Arrange
            var stories = VelvetPreviewRegistry.DiscoverStories();

            // Act
            var results = VelvetPreviewSmokeTest.Run();

            // Assert
            Assert.That(
                (results.Count, string.Join(" | ", results.Where(r => r.Failure != null)
                    .Select(r => r.Story.Id + ": " + r.Failure))),
                Is.EqualTo((stories.Count, string.Empty)));
        }
    }
}
