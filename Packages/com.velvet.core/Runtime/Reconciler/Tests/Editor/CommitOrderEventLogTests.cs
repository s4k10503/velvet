using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins whole commit sequences — insertion, layout and passive effects with their cleanups, and ref attach and
    /// detach — as one <see cref="EventLog"/> string per case, each one the sequence React 19's
    /// <c>ReactFiberCommitWork</c> produces for the same tree.
    /// </summary>
    [TestFixture]
    internal sealed class CommitOrderEventLogTests
    {
        private sealed record LeafProps(string Name, int Dep);

        [Component]
        private static VNode Leaf(LeafProps props)
        {
            EventLog.UseRender(props.Name);
            // The name is a dependency so that a sibling patched with another's props, rather than moved with its
            // own, re-runs its effects.
            EventLog.UseEffects(props.Name, new object[] { props.Name, props.Dep });
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode Parent()
        {
            var (dep, _) = EventLog.UseState("P", 0);
            EventLog.UseRender("P");
            EventLog.UseEffects("P", new object[] { dep });
            return V.Div(refCallback: EventLog.UseRefCallback("P"), children: new VNode[]
            {
                V.Component(Leaf, new LeafProps("C1", dep), key: "c1"),
                V.Component(Leaf, new LeafProps("C2", dep), key: "c2"),
            });
        }

        // Without the compiler a and b are fresh elements each render, so both children render on the reorder; the
        // compiler would cache them, and a child handed back its own element bails.
        [Component(Compiler = false)]
        private static VNode Reorderer()
        {
            var (reversed, _) = EventLog.UseState("Order", false);
            EventLog.UseRender("Order");
            var a = V.Component(Leaf, new LeafProps("A", 0), key: "a");
            var b = V.Component(Leaf, new LeafProps("B", 0), key: "b");
            return V.Div(children: reversed ? new VNode[] { b, a } : new VNode[] { a, b });
        }

        private sealed record Named(string Name);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode Thrower() => throw new InvalidOperationException("thrown by the probe");

        [Component]
        private static VNode ThrowerParent()
        {
            EventLog.UseEffects("M", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("M"), children: new VNode[] { V.Component(Thrower) });
        }

        [Component]
        private static VNode BoundaryHost()
            => V.ErrorBoundary(
                fallback: _ => V.Component(Probed, new Named("F")),
                children: new VNode[]
                {
                    V.Component(Probed, new Named("S"), key: "s"),
                    V.Component(ThrowerParent, key: "m"),
                });

        // GREEN_ON_BASE(characterization): a mount hands no child its own element back, so the same-element bail
        // leaves the mount sequence a render/commit split must keep as the base commits it.
        [Test]
        public void Given_AParentWithTwoChildrenEachWithEffectsAndARef_When_Mounted_Then_RendersRunTopDownAndEachEffectPhaseRunsPostOrder()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(Parent)));
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:P render:C1 render:C2 ins:C1 ins:C2 ins:P ref:C1 lay:C1 ref:C2 lay:C2 ref:P lay:P eff:C1 eff:C2 eff:P"));
        }

        // GREEN_ON_BASE(characterization): each child's props are built from the changed dep, so none is handed its
        // own element back and the update sequence a render/commit split must keep is the base's.
        [Test]
        public void Given_AMountedParentWithTwoChildren_When_EveryDepChanges_Then_InsertionAndLayoutCleanupsRunPerFiberPostOrderBeforeAnyLayoutSetupAndPassiveCleanupsPrecedePassiveSetups()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(Parent)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("P").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:P render:C1 render:C2"
                + " ins-x:C1 ins:C1 lay-x:C1 ins-x:C2 ins:C2 lay-x:C2 ins-x:P ins:P lay-x:P"
                + " lay:C1 lay:C2 lay:P"
                + " eff-x:C1 eff-x:C2 eff-x:P eff:C1 eff:C2 eff:P"));
        }

        // GREEN_ON_BASE(characterization): built without the compiler, the reordered children are fresh elements, so
        // neither bails and the keyed-reorder sequence a render/commit split must keep is the base's.
        [Test]
        public void Given_KeyedChildrenWithEffectsAndRefs_When_Reordered_Then_OnlyRendersAreLogged()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(Reorderer)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Order").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Order render:B render:A"));
        }

        // GREEN_ON_BASE(characterization): a mount hands no child its own element back, so a discarded pass commits
        // none of its effects as on the base, which a split must keep.
        [Test]
        public void Given_AnErrorBoundaryAroundAThrowingGrandchild_When_Mounted_Then_OnlyTheFallbacksEffectsAndRefAreCommitted()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(BoundaryHost)),
                new MountOptions { OnCaughtError = (_, _) => { } });
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:F ref:F lay:F eff:F"));
        }
    }
}
