using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetPreviewSmokeTestTests
    {
        private const string Group = nameof(VelvetPreviewSmokeTestTests);

        // Every story in this assembly mounts through these setups, so they throw only while a case asks them to.
        private static bool s_setupThrows;
        private static bool s_teardownThrows;

        // The transition's render runs more than once; throwing on the first keeps its log a single entry.
        private static bool s_transitionThrew;

        [VelvetPreviewSetup]
        private static void ThrowingSetup()
        {
            if (s_setupThrows) throw new InvalidOperationException("setup boom");
        }

        [VelvetPreviewSetup]
        private static Action ThrowingTeardownSetup() =>
            s_teardownThrows ? () => throw new InvalidOperationException("teardown boom") : null;

        [TearDown]
        public void TearDown()
        {
            s_setupThrows = false;
            s_teardownThrows = false;
            s_transitionThrew = false;
        }

        private static VNode Plain() => V.Div();

        private static VNode StoryThrows() => throw new InvalidOperationException("story boom");

        private static VNode RenderThrows() => V.Component(ThrowingRender, key: "child");

        private static VNode EffectThrows() => V.Component(ThrowingEffectRender, key: "child");

        private static VNode UpdateThrows() => V.Component(ThrowAfterEffectUpdateRender, key: "child");

        private static VNode BoundaryCatches() => V.Component(BoundaryRender, key: "boundary");

        private static VNode SecondCommitEffectThrows() => V.Component(ThrowOnReadyEffectRender, key: "child");

        private static VNode TransitionThrows() => V.Component(ThrowAfterTransitionRender, key: "child");

        private static VNode ReadsItsPanel() => V.Component(PanelReadingRender, key: "child");

        [Component]
        private static VNode ThrowOnReadyEffectRender()
        {
            var (ready, setReady) = Hooks.UseState(false);
            Hooks.UseEffect(() =>
            {
                setReady.Invoke(true);
                return (Action)null;
            }, Array.Empty<object>());
            Hooks.UseEffect(() =>
            {
                if (ready) throw new InvalidOperationException("ready boom");
                return (Action)null;
            }, new object[] { ready });
            return V.Div();
        }

        [Component]
        private static VNode ThrowAfterTransitionRender()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            var (_, startTransition) = Hooks.UseTransition();
            Hooks.UseEffect(() =>
            {
                startTransition.Invoke(() => setMoved.Invoke(true));
                return (Action)null;
            }, Array.Empty<object>());
            if (moved && !s_transitionThrew)
            {
                s_transitionThrew = true;
                throw new InvalidOperationException("transition boom");
            }

            return V.Div();
        }

        [Component]
        private static VNode PanelReadingRender()
        {
            var target = Hooks.UseRef<VisualElement>();
            Hooks.UseLayoutEffect(() =>
            {
                _ = target.Current.panel.visualTree;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div(refCallback: target.SetElement);
        }

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
        public void Given_AnErrorABoundaryCatchesThenASetupTeardownThatThrows_When_SmokeTested_Then_ItFailsWithTheTeardowns()
        {
            // Arrange
            s_teardownThrows = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: render boom");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: teardown boom");

            // Act
            var failure = FailureOf(nameof(BoundaryCatches));

            // Assert
            Assert.That(failure, Is.EqualTo("InvalidOperationException: teardown boom"));
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
        public void Given_TheProjectsStories_When_SmokeTested_Then_EachExampleMountsWithoutAnError()
        {
            // Act
            var results = VelvetPreviewSmokeTest.Run();

            // Assert — the example stories Velvet.Editor declares are this project's whole set.
            Assert.That(
                string.Join(" | ", results.Select(r => r.Story.Id + ": " + (r.Failure ?? "passed"))),
                Is.EqualTo("Examples/Card: passed | Examples/Responsive: passed | Examples/Tall List: passed"));
        }

        [Test]
        public void Given_AnEffectThatThrowsOnlyOnTheSecondCommit_When_SmokeTested_Then_ItFails()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: ready boom");

            // Act
            var failure = FailureOf(nameof(SecondCommitEffectThrows));

            // Assert
            Assert.That(failure, Is.EqualTo("InvalidOperationException: ready boom"));
        }

        [Test]
        public void Given_ARenderThatThrowsOnlyAfterATransition_When_SmokeTested_Then_ItFails()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: transition boom");

            // Act
            var failure = FailureOf(nameof(TransitionThrows));

            // Assert
            Assert.That(failure, Is.EqualTo("InvalidOperationException: transition boom"));
        }

        [Test]
        public void Given_ALayoutEffectReadingItsElementsPanel_When_SmokeTested_Then_ItPasses()
        {
            // Act
            var failure = FailureOf(nameof(ReadsItsPanel));

            // Assert
            Assert.That(failure, Is.Null);
        }

        [Test]
        public void Given_ASmokeRun_When_ItFinishes_Then_ItLeavesNoPanelObjectBehind()
        {
            // Arrange
            var before = PanelObjectCounts();

            // Act
            FailureOf(nameof(Plain));

            // Assert
            Assert.That(PanelObjectCounts(), Is.EqualTo(before));
        }

        private static string PanelObjectCounts() =>
            string.Join(",",
                Resources.FindObjectsOfTypeAll<UIDocument>().Length,
                Resources.FindObjectsOfTypeAll<PanelSettings>().Length,
                Resources.FindObjectsOfTypeAll<ThemeStyleSheet>().Length,
                Resources.FindObjectsOfTypeAll<RenderTexture>().Length);
    }
}
