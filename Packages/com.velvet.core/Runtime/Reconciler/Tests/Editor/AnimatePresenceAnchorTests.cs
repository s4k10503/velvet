using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which Motion a keyed AnimatePresence child's enter and exit play on — its anchor, as the motion
    /// guide's <i>Exits</i> section states it — for a keyed child that is the Motion itself or puts a
    /// Provider, a component, a memo, a Suspense or an element around it:
    /// the presence's <c>initial: false</c> reaches an anchor only, the removal waits for an anchor's exit
    /// only, and a Motion declaring <c>exit</c> where it is no anchor says so at mount.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceAnchorTests
    {
        private const string InertExitWarning = "exit on this Motion is inert";

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static readonly ComponentContext<int> s_context = ComponentContext<int>.Create(0);

        private readonly record struct KeySetState(string Keys);

        private sealed class KeySetStore : Store<KeySetState>
        {
            public KeySetStore(string initial) : base(new KeySetState(initial)) { }
            public void Set(string keys) => SetState(_ => new KeySetState(keys));
            protected override void ResetCore() => SetState(_ => new KeySetState("a"));
        }

        private static KeySetStore s_keyStore;
        private static string s_wrapper;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_keyStore = null;
            s_wrapper = null;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        // Declares an enter and an exit of its own, so either one playing is readable off its element.
        private static VNode TimedMotion(string key) => V.Motion(name: "item", key: key, variants: s_fade,
            initial: "hidden", animate: "visible", exit: "hidden",
            transition: new StyleTransitionConfig { DurationSec = 0.3f });

        [Component]
        private static VNode MotionRender() => TimedMotion(null);

        // The keyed child s_wrapper names: the same TimedMotion, or a wrapper around it. "nested" is the one
        // wrapper that is an anchor itself, with the timed Motion inside it.
        private static VNode KeyedChild(string key) => s_wrapper switch
        {
            "direct" => TimedMotion(key),
            "provider" => V.Provider(s_context, 1, new[] { TimedMotion(null) }, key: key),
            "component" => V.Component(MotionRender, key: key),
            "memo" => V.MemoizedWithKey(key, () => TimedMotion(null)),
            "suspense" => V.Suspense(V.Label(text: "loading"), new[] { TimedMotion(null) }, key: key),
            "element" => V.Div(key: key, children: new[] { TimedMotion(null) }),
            "nested" => V.Motion(key: key, variants: s_fade, animate: "visible",
                children: new[] { TimedMotion(null) }),
            _ => throw new System.ArgumentOutOfRangeException(nameof(s_wrapper), s_wrapper, null),
        };

        [Component]
        private static VNode PresenceHost()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(KeyedChild(key.ToString()));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: children.ToArray()),
            });
        }

        // The inline transition-duration the scheduler wrote on this element, in milliseconds, or NaN when
        // nothing holds the slot — the same reading MotionVariantTransitionTests takes.
        private static float InlineDurationMs(VisualElement element)
        {
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0
                ? duration.value[0].value
                : float.NaN;
        }

        // An unexpected Warning fails no Unity test on its own, so the count is captured and asserted rather
        // than left to LogAssert.
        private int CountInertExitWarningsWhileMounting()
        {
            var warned = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains(InertExitWarning))
                {
                    warned++;
                }
            }
            Application.logMessageReceived += OnLog;
            try
            {
                using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            return warned;
        }

        [TestCase("component")]
        [TestCase("memo")]
        [TestCase("suspense")]
        [TestCase("element")]
        [TestCase("nested")]
        public void Given_AMotionDeclaringExitThatIsNoAnchor_When_ThePresenceChildMounts_Then_TheInertExitIsDiagnosed(
            string wrapper)
        {
            // Arrange
            s_wrapper = wrapper;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act
            var warned = CountInertExitWarningsWhileMounting();

            // Assert — once, for the timed Motion.
            Assert.That(warned, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): an anchor declaring exit mounted without this warning before it existed.
        [TestCase("direct")]
        [TestCase("provider")]
        public void Given_AnAnchorDeclaringExit_When_ThePresenceChildMounts_Then_NothingIsDiagnosed(string wrapper)
        {
            // Arrange
            s_wrapper = wrapper;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act
            var warned = CountInertExitWarningsWhileMounting();

            // Assert
            Assert.That(warned, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): only an anchor's removal waits for its exit, which this change documents.
        [TestCase("direct", 1)]
        [TestCase("provider", 1)]
        [TestCase("component", 0)]
        [TestCase("memo", 0)]
        [TestCase("suspense", 0)]
        [TestCase("element", 0)]
        public void Given_AKeyedChildWrappingATimedMotion_When_TheKeyIsRemoved_Then_OnlyAnAnchorIsHeldForItsExit(
            string wrapper, int heldChildren)
        {
            // Arrange — settled first, so the removal is from rest.
            s_wrapper = wrapper;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            for (var i = 0; i < 40; i++) _sim.FrameUpdateMs(16);

            // Act
            keys.Set(string.Empty);
            scheduler.DrainImmediateForTest();

            // Assert — an exiting ghost still holds the host's slot; a child with no anchor is gone.
            Assert.That(Root.Q<VisualElement>("host").childCount, Is.EqualTo(heldChildren));
        }

        // GREEN_ON_BASE(characterization): initial: false suppressed only an anchor's mount enter, as documented now.
        [TestCase("direct", float.NaN)]
        [TestCase("provider", float.NaN)]
        [TestCase("component", 300f)]
        [TestCase("memo", 300f)]
        [TestCase("suspense", 300f)]
        [TestCase("element", 300f)]
        public void Given_InitialFalseOnThePresence_When_AKeyedChildWrappingATimedMotionMounts_Then_OnlyAnAnchorsEnterIsSuppressed(
            string wrapper, float enterMs)
        {
            // Arrange
            s_wrapper = wrapper;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act — an enter that plays writes its inline timing inside this reconcile.
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));

            // Assert — the Motion's own 300ms where its standalone enter ran, nothing where it was suppressed.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("item")), Is.EqualTo(enterMs).Within(1e-3f));
        }
    }
}
