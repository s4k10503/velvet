using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins bottom-up effect-cleanup order when a subtree of directly-nested inline components is
    /// orphaned in one reconcile (a conditional unmount of a component that itself returns another
    /// component, with no host element in between — the shape of a link wrapper or a functional
    /// error boundary). The old-side expansion appended the parent fiber before recursing into its
    /// body output, so the orphan sweep's forward walk ran the PARENT's cleanups while the child was
    /// still fully mounted, inverting unmount order: a descendant's cleanups must complete before an
    /// ancestor's, exactly as the commit-phase deletion path already guarantees.
    /// </summary>
    [TestFixture]
    internal sealed class OrphanCleanupOrderTests
    {
        private readonly record struct ToggleState(bool Show);

        private sealed class ToggleStore : Store<ToggleState>
        {
            public ToggleStore() : base(new ToggleState(true)) { }
            public void Set(bool show) => SetState(_ => new ToggleState(show));
            protected override void ResetCore() => SetState(_ => new ToggleState(true));
        }

        private static ToggleStore s_store;
        private static readonly List<string> s_log = new();

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_log.Clear();
        }

        [Component]
        private static VNode OrderProbeInner()
        {
            Hooks.UseLayoutEffect(() => () => s_log.Add("cleanup:Inner"), Array.Empty<object>());
            return V.Div(name: "inner");
        }

        // Directly returns another component — no host element in between, so both fibers are
        // collected by the same old-side expansion walk when the subtree is orphaned.
        [Component]
        private static VNode OrderProbeOuter()
        {
            Hooks.UseLayoutEffect(() => () => s_log.Add("cleanup:Outer"), Array.Empty<object>());
            return V.Component(OrderProbeInner, key: "inner");
        }

        [Component]
        private static VNode OrderProbeRoot()
        {
            var visible = Hooks.UseStore(s_store, s => s.Show);
            return V.Div(name: "root", children: visible
                ? new VNode[] { V.Component(OrderProbeOuter, key: "outer") }
                : Array.Empty<VNode>());
        }

        [Test]
        public void Given_NestedInlineComponentsAreOrphanedTogether_When_CleanupsRun_Then_TheChildCleansUpBeforeTheParent()
        {
            // Arrange — a mounted parent→child inline chain with one layout-effect cleanup each.
            using var store = new ToggleStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(OrderProbeRoot, key: "probe-root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            mounted.FlushEffectsForTest();
            Assume.That(s_log, Is.Empty, "Precondition: no cleanup has run while mounted");

            // Act — orphan the whole chain in one reconcile.
            store.Set(false);
            scheduler.DrainImmediateForTest();

            // Assert — bottom-up: the child's cleanup completes before the parent's.
            Assert.That(s_log, Is.EqualTo(new[] { "cleanup:Inner", "cleanup:Outer" }));
        }
    }
}
