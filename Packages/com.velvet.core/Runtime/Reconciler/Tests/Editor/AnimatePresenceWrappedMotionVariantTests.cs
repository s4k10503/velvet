using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins variant enter/exit resolution for a Motion that sits UNDER a transparent wrapper inside an
    /// AnimatePresence keyed child — a z-managed Div (the animated top-most modal shape, since z-* is a
    /// documented no-op on a Motion itself) or a ContextProvider. The presence anchor walk already
    /// resolves the descendant Motion for timing and onEnterComplete; the named variants' enter/exit
    /// CLASSES must resolve against that same Motion's own element (where the resting variants[animate]
    /// classes live), not be silently dropped because the keyed child node is not the Motion itself.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceWrappedMotionVariantTests
    {
        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore() : base(new SetState("a")) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("a"));
        }

        private static SetStore s_store;
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("light");
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        private static VNode VariantMotion(string key)
            => V.Motion(name: "inner-" + key,
                variants: s_fade, initial: "hidden", animate: "visible", exit: "hidden",
                transition: new StyleTransitionConfig { DurationSec = 0.3f });

        [Component]
        private static VNode ZWrappedPresenceHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                var k = key.ToString();
                children.Add(V.Div(key: k, name: "wrapper-" + k, className: "absolute z-10",
                    children: new VNode[] { VariantMotion(k) }));
            }
            return V.Div(name: "host", className: "relative", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode ProviderWrappedPresenceHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                var k = key.ToString();
                children.Add(V.Provider(ThemeContext, "dark", key: k,
                    children: new VNode[] { VariantMotion(k) }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        // The variant enter's synchronous strip-to-initial is the observable EditMode evidence (the
        // scheduler never fires the swap back to the resting classes), mirroring
        // MotionPresenceEnterGateTests' oracle.

        [Test]
        public void Given_AZWrappedVariantMotion_When_Mounted_Then_TheInnerMotionStartsAtItsInitialVariant()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;

            // Act
            using var mounted = V.Mount(_root, V.Component(ZWrappedPresenceHost, key: "host"));

            // Assert — the variant enter played against the Motion's own element: it carries
            // variants[initial] instead of resting at variants[animate].
            Assert.That(_root.Q<VisualElement>("inner-a").ClassListContains("opacity-0"), Is.True,
                "The z-wrapped Motion's variant enter resolves against its own element");
        }

        [Test]
        public void Given_AProviderWrappedVariantMotion_When_Mounted_Then_TheInnerMotionStartsAtItsInitialVariant()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;

            // Act
            using var mounted = V.Mount(_root, V.Component(ProviderWrappedPresenceHost, key: "host"));

            // Assert
            Assert.That(_root.Q<VisualElement>("inner-a").ClassListContains("opacity-0"), Is.True,
                "The Provider-wrapped Motion's variant enter resolves against its own element");
        }

        // A VARIANT exit cancels the in-flight enter, applies its from-classes — the resting
        // variants[animate] — and sets transition-property: all inline on its target, all
        // synchronously (only the swap to variants[exit] is scheduler-driven). The INNER element
        // resting at variants[animate] with the initial classes gone AND carrying the variant swap's
        // transition-property is the observable evidence the exit resolved the declared variants and
        // targeted the Motion: the silent classic fallback leaves the inner element untouched — frozen
        // at the enter's variants[initial] pose (or, with no enter fix either, resting with no
        // transition-property at all).

        private static (bool op0, bool op100, bool tpAll) VariantExitEvidence(VisualElement inner)
        {
            var tp = inner.style.transitionProperty;
            var tpAll = tp.keyword != StyleKeyword.Null && tp.value.Count > 0 && tp.value[0].ToString() == "all";
            return (inner.ClassListContains("opacity-0"), inner.ClassListContains("opacity-100"), tpAll);
        }

        [Test]
        public void Given_AZWrappedVariantMotion_When_TheKeyIsRemoved_Then_TheVariantExitTargetsTheInnerMotion()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ZWrappedPresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;

            // Act — remove the key; the ghost's exit must start on the Motion's own element.
            store.Set("");
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.AreEqual((false, true, true), VariantExitEvidence(_root.Q<VisualElement>("inner-a")),
                "The z-wrapped Motion's variant exit resolves against its own element");
        }

        [Test]
        public void Given_AProviderWrappedVariantMotion_When_TheKeyIsRemoved_Then_TheVariantExitTargetsTheInnerMotion()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ProviderWrappedPresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;

            // Act
            store.Set("");
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.AreEqual((false, true, true), VariantExitEvidence(_root.Q<VisualElement>("inner-a")),
                "The Provider-wrapped Motion's variant exit resolves against its own element");
        }

        [Test]
        public void Given_AZWrappedVariantMotionMidExit_When_TheKeyIsReAdded_Then_TheInnerMotionRestsAtItsAnimateVariant()
        {
            // Arrange — the exit must be cancelled ON THE MOTION'S ELEMENT (a cancel aimed only at the
            // wrapper would leave the inner exit running to completion, whose ghost-drop then removes the
            // freshly re-entered child).
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ZWrappedPresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("");
            scheduler.DrainImmediateForTest();
            Assume.That(_root.Q<VisualElement>("inner-a"), Is.Not.Null,
                "Precondition: the exiting ghost is still mounted");

            // Act — cancel the exit by re-adding the key.
            store.Set("a");
            scheduler.DrainImmediateForTest();

            // Assert — the cancel reverses the inner element toward its resting variant (initial is not
            // replayed on the still-attached element) AND scrubs the exit's inline transition styles (an
            // off-panel cancel clears immediately) — a cancel that misses the inner element would leave
            // both the pending exit and its inline duration alive there.
            var inner = _root.Q<VisualElement>("inner-a");
            Assert.AreEqual((false, true, true),
                (inner.ClassListContains("opacity-0"), inner.ClassListContains("opacity-100"),
                    inner.style.transitionDuration.keyword == StyleKeyword.Null),
                "Cancelling a wrapped Motion's variant exit restores its resting variant and clears the exit");
        }
    }
}
