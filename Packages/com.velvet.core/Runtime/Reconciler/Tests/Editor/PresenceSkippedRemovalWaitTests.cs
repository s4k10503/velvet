using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a presence keeps of a key still exiting through a render an abort stopped before its removals:
    /// the exit wait the key still held, which the next panel frame settles when a descendant's teardown has asked it
    /// to, so that the key's exit still completes. A presence disposed with its tree while an exit plays completes
    /// that exit once.
    /// </summary>
    [TestFixture]
    internal sealed class PresenceSkippedRemovalWaitTests
    {
        private static readonly System.Collections.Generic.Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static Action<int> s_setHostTick;
        private static Action<bool> s_setInnerShown;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_setHostTick = null;
            s_setInnerShown = null;
            s_setShown = null;
            s_exitCompletions = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        // GREEN_ON_BASE(characterization): the merge base removed the key at once in the aborted render and put its wait
        // back. The ghost that render re-emits now keeps the wait without being removed.
        [Test]
        public void Given_AKeyWhoseOnlyExitWasATornDownDescendants_When_AnAbortedRenderReEmitsItsGhost_Then_ThePresenceKeepsItsExitWait()
        {
            // Arrange — the key's exit is under way, and its descendant is torn down with the settle left to the next frame
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(WaitHostRender, key: "host"), CaughtErrors.Unlogged);
            s_setHostTick.Invoke(1);
            mounted.FlushStateForTest();
            s_setInnerShown.Invoke(false);
            mounted.FlushStateForTest();

            // Act — a render before that frame re-emits the key's ghost, and aborts
            s_setHostTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(mounted.Root.Reconciler.Context.PresenceStates.Values.Single().ExitWaits.ContainsKey("a"), Is.True);
        }

        // Framer lets the exit animation of a presence unmounted with its tree run out and then calls onExitComplete;
        // the disposal cancels the animation, so it completes the exit itself, and the frames after it add nothing.
        [Test]
        public void Given_APresenceOnAPanelWithAnExitPlaying_When_TheTreeIsDisposed_Then_OnExitCompleteRunsOnce()
        {
            // Arrange — the exit is a second long, and no frame passes before the disposal
            var mounted = V.Mount(_sim.rootVisualElement, V.Component(PlayingExitHostRender, key: "host"));
            s_setShown.Invoke(false);
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();
            for (var i = 0; i < 3; i++) _sim.FrameUpdateMs(16);

            // Assert
            Assert.That(s_exitCompletions, Is.EqualTo(1));
        }

        private static Action<bool> s_setShown;
        private static int s_exitCompletions;

        [Component(Compiler = false)]
        private static VNode PlayingExitHostRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.AnimatePresence(key: "p", onExitComplete: () => s_exitCompletions++, children: shown
                ? new VNode[]
                {
                    V.Motion(key: "a", variants: s_fade, animate: "visible", exit: "hidden",
                        transition: new StyleTransitionConfig { DurationSec = 1f }),
                }
                : Array.Empty<VNode>());
        }

        [Component(Compiler = false)]
        private static VNode WaitRefChildRender(bool drop)
            => V.Label(text: "child",
                refCallback: drop ? null : _ => () => throw new InvalidOperationException("Ref cleanup throw"));

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode WaitBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "o-fallback"));
            return V.Component(WaitRefChildRender, tick == 2, key: "child");
        }

        [Component(Compiler = false)]
        private static VNode WaitInnerRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setInnerShown = setShown;
            return shown
                ? V.Motion(name: "descendant", variants: s_fade, initial: "hidden", animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f })
                : null;
        }

        [Component(Compiler = false)]
        private static VNode WaitHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setHostTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(WaitBoundaryRender, tick, key: "o"),
                V.AnimatePresence(key: "p", children: tick == 0
                    ? new VNode[] { V.Div(key: "a", children: new VNode[] { V.Component(WaitInnerRender, key: "inner") }) }
                    : Array.Empty<VNode>()),
                V.Label(text: "tail:" + tick),
            });
        }
    }
}
