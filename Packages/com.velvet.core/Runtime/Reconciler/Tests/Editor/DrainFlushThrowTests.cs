using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a drain leaves queued when an exception leaves the flush of a component it renders: a component queued
    /// behind that one, which the drain never rendered, still renders at the tier's next drain, with the update it
    /// received after the exception. Held on the immediate tier and on the Transition tier.
    /// </summary>
    /// <remarks>
    /// The exception is a <see cref="BoundaryCaughtSignal"/> naming no boundary, thrown by the first component's body.
    /// Rendering rethrows the signal for the walk of the boundary it names; naming none, this one leaves the drain. A
    /// render's own error would not serve: a boundary takes it, or the root, which unmounts the tree
    /// (<see cref="UncaughtErrorRootTests"/>). Each assertion folds in that the drain threw, so a change that stops the
    /// signal leaving the flush reddens the case rather than passing it with nothing measured.
    /// </remarks>
    [TestFixture]
    internal sealed class DrainFlushThrowTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_firstThrows = false;
            s_setFirst = default;
            s_setSecond = default;
            s_startTransition = default;
        }

        [TearDown]
        public void TearDown() => s_firstThrows = false;

        [Test]
        public void Given_AnImmediateDrainWhoseFlushOfTheFirstOfTwoQueuedComponentsThrew_When_TheSecondUpdatesAgain_Then_TheNextDrainRendersIt()
        {
            // Arrange — the drain renders the two in tree order, so the second is behind the first
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_firstThrows = true;
            s_setFirst.Invoke(1);
            s_setSecond.Invoke(1);
            var threw = DrainThrows(scheduler.RunImmediateCallbackForTest);
            s_firstThrows = false;

            // Act
            s_setSecond.Invoke(2);
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(threw ? Text("second") : null, Is.EqualTo("second:2"));
        }

        [Test]
        public void Given_ATransitionDrainWhoseFlushOfTheFirstOfTwoQueuedComponentsThrew_When_TheSecondUpdatesAgainInATransition_Then_TheNextTransitionDrainRendersIt()
        {
            // Arrange — one transition updates both
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_firstThrows = true;
            s_startTransition.Invoke(() =>
            {
                s_setFirst.Invoke(1);
                s_setSecond.Invoke(1);
            });
            var threw = DrainThrows(scheduler.DrainDelayedForTest);
            s_firstThrows = false;

            // Act — started by the first component: the Urgent update that shows a transition pending outranks the
            // second's Transition lane, so it would put the second back on a tier had the second started it
            s_startTransition.Invoke(() => s_setSecond.Invoke(2));
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That(threw ? Text("second") : null, Is.EqualTo("second:2"));
        }

        private string Text(string name) => _root.Q<Label>(name).text;

        private static bool DrainThrows(Action drain)
        {
            try
            {
                drain();
            }
            catch (BoundaryCaughtSignal)
            {
                return true;
            }
            return false;
        }

        private static bool s_firstThrows;
        private static StateUpdater<int> s_setFirst;
        private static StateUpdater<int> s_setSecond;
        private static TransitionStarter s_startTransition;

        private static VNode HostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(FirstRender, key: "first"),
                V.Component(SecondRender, key: "second"),
            });

        [Component(Compiler = false)]
        private static VNode FirstRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setFirst = setValue;
            var (_, start) = Hooks.UseTransition();
            s_startTransition = start;
            if (s_firstThrows && value > 0)
            {
                throw new BoundaryCaughtSignal(null, Array.Empty<VNode>(), null, new InvalidOperationException("first"),
                    new ErrorInfo(string.Empty));
            }
            return V.Label(name: "first", text: "first:" + value);
        }

        [Component(Compiler = false)]
        private static VNode SecondRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setSecond = setValue;
            return V.Label(name: "second", text: "second:" + value);
        }
    }
}
