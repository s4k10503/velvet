using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins interruption continuity for a cancelled AnimatePresence exit. Cancelling used to revert
    /// to the resting classes and null the transition styles in the SAME synchronous call, so the
    /// next style resolve saw the resting target with no active transition and popped straight to it
    /// instead of blending back from the mid-tween value; with an <c>initial</c> variant declared,
    /// the cancel additionally replayed the full initial→animate enter from the declared initial
    /// pose. The oracle starts every retarget — including an exit cancelled by re-adding the key —
    /// from the value the element currently shows: the reversal must keep the transition alive
    /// (deferring the style clear) and must not replay <c>initial</c> on a still-mounted element.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceExitCancelBlendTests
    {
        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore() : base(new SetState("a")) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("a"));
        }

        private static SetStore s_store;
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };
        private static bool s_withInitial;

        private VisualElement _root;
        private EditorWindow _window;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_withInitial = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.Close();
                Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        [Component]
        private static VNode PresenceHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_fade,
                    animate: "visible",
                    initial: s_withInitial ? "hidden" : null,
                    exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: children.ToArray()),
            });
        }

        [Test]
        public void Given_AVariantExitWasCancelled_When_TheElementReverts_Then_TheTransitionStaysAliveForTheReversal()
        {
            // Arrange — a real panel (the reversal is panel-interpolated; off-panel cancels clear
            // immediately), a settled child whose exit has started, then the key re-added mid-exit.
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            _window = ScriptableObject.CreateInstance<EditorWindow>();
            _window.Show();
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_window.rootVisualElement, V.Component(PresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("");
            scheduler.DrainImmediateForTest();
            var item = _window.rootVisualElement.Q<VisualElement>("item-a");
            Assume.That(item, Is.Not.Null, "Precondition: the exiting ghost is still mounted");

            // Act — cancel the exit by re-adding the key.
            store.Set("a");
            scheduler.DrainImmediateForTest();

            // Assert — the transition styles survive the cancel, so the panel interpolates from the
            // currently-resolved value back to the resting classes instead of snapping.
            Assert.That(_window.rootVisualElement.Q<VisualElement>("item-a").style.transitionDuration.keyword,
                Is.Not.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AVariantMotionWithInitial_When_ItsExitIsCancelled_Then_TheInitialPoseIsNotReplayed()
        {
            // Arrange — the full initial+animate+exit pattern; exit starts, then the key returns.
            s_withInitial = true;
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("");
            scheduler.DrainImmediateForTest();
            Assume.That(_root.Q<VisualElement>("item-a"), Is.Not.Null,
                "Precondition: the exiting ghost is still mounted");

            // Act — cancel the exit; the same still-attached element is reproduced (not a remount).
            store.Set("a");
            scheduler.DrainImmediateForTest();

            // Assert — initial applies only on a genuine first mount: the cancel leaves the element
            // at its resting variant instead of re-seeding the declared initial pose.
            var item = _root.Q<VisualElement>("item-a");
            Assert.That((item.ClassListContains("opacity-0"), item.ClassListContains("opacity-100")),
                Is.EqualTo((false, true)));
        }
    }
}
