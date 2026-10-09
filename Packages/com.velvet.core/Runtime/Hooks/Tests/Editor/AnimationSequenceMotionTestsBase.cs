using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Shared harness for the fixtures that watch a <c>Hooks.UseAnimationSequence</c> drive a <c>V.Motion</c>'s
    /// Bezier plays on <see cref="UseFrameFakeClockHost"/>'s fake clock: a sequence that lands on <c>hidden</c> at
    /// once and then plays a linear one-second fade to <c>visible</c>, either on the Motion handed the sequence's
    /// transition or on a child taking its label from it. Every frame is 16ms, and a frame's re-render is flushed
    /// inside it, so a label a frame commits starts its play there.
    /// </summary>
    internal abstract class AnimationSequenceMotionTestsBase
    {
        protected static readonly StyleTransitionConfig Instant = new() { Type = TransitionType.Bezier, DurationSec = 0f };

        protected static StyleTransitionConfig Linear(float durationSec) => new()
        {
            Type = TransitionType.Bezier, DurationSec = durationSec,
            BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
        };

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["half"] = "opacity-50",
            ["visible"] = "opacity-100",
        };

        // The child's poses name their own transitions, so its swaps play those rather than the sequence's.
        private static readonly Dictionary<string, MotionVariant> s_childFade = new()
        {
            ["hidden"] = new MotionVariant("opacity-0", Instant),
            ["visible"] = new MotionVariant("opacity-100", Linear(1f)),
        };

        protected static AnimationSequenceStep[] Steps;
        protected static AnimationSequenceState State;
        protected static AnimationSequenceControls Controls;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            Steps = new[] { AnimationSequenceStep.To("hidden", Instant), AnimationSequenceStep.To("visible", Linear(1f)) };
            OuterLabel = "hidden";
            ChildMounted = false;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Component]
        private static VNode CoordinatorHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(Steps, deps: Array.Empty<object>());
            State = state;
            Controls = controls;
            return V.Motion(key: "m", name: "m", variants: s_fade, animate: state.CurrentLabel,
                transition: state.CurrentTransition);
        }

        [Component]
        private static VNode InheritingHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(Steps, deps: Array.Empty<object>());
            State = state;
            Controls = controls;
            return V.Motion(key: "coordinator", name: "coordinator", animate: state.CurrentLabel,
                transition: state.CurrentTransition, children: new VNode[]
                {
                    V.Motion(key: "m", name: "m", variants: s_childFade),
                });
        }

        protected static string OuterLabel;

        // The child sits beside the sequence's coordinator under an outer Motion with no playback, taking the outer
        // label. Rendered again on every root render, so a test flips OuterLabel with RenderAgain.
        [Component(Compiler = false)]
        private static VNode SiblingHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(Steps, deps: Array.Empty<object>());
            State = state;
            Controls = controls;
            return V.Motion(key: "outer", name: "outer", animate: OuterLabel, children: new VNode[]
            {
                V.Motion(key: "coordinator", name: "coordinator", animate: state.CurrentLabel,
                    transition: state.CurrentTransition),
                V.Motion(key: "m", name: "m", variants: s_childFade),
            });
        }

        protected static bool ChildMounted;

        // The child mounts under the coordinator only once ChildMounted is set, taking its label and the
        // coordinator's initial label, so it plays a mount enter from hidden to the sequence's current label.
        [Component(Compiler = false)]
        private static VNode MidMountHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(Steps, deps: Array.Empty<object>());
            State = state;
            Controls = controls;
            return V.Motion(key: "coordinator", name: "coordinator", initial: "hidden", animate: state.CurrentLabel,
                transition: state.CurrentTransition, children: ChildMounted
                    ? new VNode[] { V.Motion(key: "m", name: "m", variants: s_childFade) }
                    : Array.Empty<VNode>());
        }

        // MidMountHost's child, keyed under an AnimatePresence, so it mounts through the presence's enter.
        [Component(Compiler = false)]
        private static VNode PresenceHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(Steps, deps: Array.Empty<object>());
            State = state;
            Controls = controls;
            return V.Motion(key: "coordinator", name: "coordinator", initial: "hidden", animate: state.CurrentLabel,
                transition: state.CurrentTransition, children: new VNode[]
                {
                    V.AnimatePresence(children: ChildMounted
                        ? new VNode[] { V.Motion(key: "m", name: "m", variants: s_childFade) }
                        : Array.Empty<VNode>()),
                });
        }

        protected void MountCoordinator() => Mount(CoordinatorHost);

        protected void MountInheriting() => Mount(InheritingHost);

        protected void MountSibling() => Mount(SiblingHost);

        protected void MountMidMount() => Mount(MidMountHost);

        protected void MountPresence() => Mount(PresenceHost);

        // The coordinator rests at hidden until the sequence's first label arrives, so a first step to visible
        // plays from hidden.
        [Component]
        private static VNode InitialCoordinatorHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(Steps, deps: Array.Empty<object>());
            State = state;
            Controls = controls;
            return V.Motion(key: "m", name: "m", variants: s_fade, initial: "hidden", animate: state.CurrentLabel,
                transition: state.CurrentTransition);
        }

        protected void MountInitialCoordinator() => Mount(InitialCoordinatorHost);

        // Set by ChildToggler's own render: mounting the child through it re-renders that component alone.
        protected static StateUpdater<bool> SetChildMounted;

        [Component]
        private static VNode ChildToggler()
        {
            var (mounted, setMounted) = Hooks.UseState(false);
            SetChildMounted = setMounted;
            return V.Div(name: "toggler", children: mounted
                ? new VNode[] { V.Motion(key: "m", name: "m", variants: s_childFade) }
                : Array.Empty<VNode>());
        }

        [Component]
        private static VNode PresenceToggler()
        {
            var (mounted, setMounted) = Hooks.UseState(false);
            SetChildMounted = setMounted;
            return V.AnimatePresence(children: mounted
                ? new VNode[] { V.Motion(key: "m", name: "m", variants: s_childFade) }
                : Array.Empty<VNode>());
        }

        [Component]
        private static VNode SelfMountHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(Steps, deps: Array.Empty<object>());
            State = state;
            Controls = controls;
            return V.Motion(key: "coordinator", name: "coordinator", initial: "hidden", animate: state.CurrentLabel,
                transition: state.CurrentTransition, children: new VNode[] { V.Component(ChildToggler, key: "t") });
        }

        [Component]
        private static VNode SelfPresenceHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(Steps, deps: Array.Empty<object>());
            State = state;
            Controls = controls;
            return V.Motion(key: "coordinator", name: "coordinator", initial: "hidden", animate: state.CurrentLabel,
                transition: state.CurrentTransition, children: new VNode[] { V.Component(PresenceToggler, key: "t") });
        }

        protected void MountSelfMount() => Mount(SelfMountHost);

        protected void MountSelfPresence() => Mount(SelfPresenceHost);

        protected void RenderAgain() => _mounted.Render(V.Component(_rendered, key: "root"));

        protected void Flush() => _mounted.FlushStateForTest();

        private Func<VNode> _rendered;

        private void Mount(Func<VNode> host)
        {
            _rendered = host;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(host, key: "root"));
            _mounted.FlushEffectsForTest();
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        protected void Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
                _mounted.FlushStateForTest();
            }
        }

        // Runs frames until the fade to visible has started, and then three more of it.
        protected void PlayIntoTheFade()
        {
            _mounted.FlushStateForTest();
            for (var i = 0; i < 5 && State.CurrentLabel != "visible"; i++)
            {
                Frames(1);
            }
            Frames(3);
        }

        protected StyleFloat Opacity() => _host.Root.Q<VisualElement>("m").style.opacity;
    }
}
