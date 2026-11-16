using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a pass dropped after it ran leaves the fiber side as the pass found it: the journal a pass
    /// keeps, rolled back, puts every fiber and tenancy the pass wrote back, whichever reconcile shape wrote it. Each
    /// case mounts its tree, takes the snapshot, runs one shape of update under <see cref="PassDropHarness"/> and
    /// reads what differs from the snapshot once the journal is rolled back.
    /// </summary>
    [TestFixture]
    internal sealed class PassDropFiberSideTests
    {
        private VisualElement _root;
        private static Action<int> s_setRows;
        private static Action<int> s_setOrder;
        private static Action<int> s_setWhich;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setRows = null;
            s_setOrder = null;
            s_setWhich = null;
        }

        private static VNode Rows(string prefix, int count)
        {
            var rows = new VNode[count];
            for (var i = 0; i < count; i++) rows[i] = V.Label(name: prefix + i, key: prefix + i);
            return V.Fragment(children: rows);
        }

        [Component]
        private static VNode TailRender() => V.Label(name: "tail", key: "tail");

        #region A component's own update

        [Component]
        private static VNode GrowingRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setRows = setRows;
            return Rows("grow", rows);
        }

        [Component]
        private static VNode GrowingHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(GrowingRender, key: "grow"),
                V.Component(TailRender, key: "tail"),
            });

        [Test]
        public void Given_AComponentThatGrowsAheadOfAnother_When_ItsOwnUpdatePassIsDropped_Then_NoSlotDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GrowingHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_setRows.Invoke(3);
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region A keyed reorder

        [Component]
        private static VNode FirstRender() => V.Label(name: "first");

        [Component]
        private static VNode SecondRender() => V.Label(name: "second");

        [Component]
        private static VNode ReorderHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_setOrder = setOrder;
            var first = V.Component(FirstRender, key: "first");
            var second = V.Component(SecondRender, key: "second");
            return V.Div(name: "box", children: order == 0 ? new VNode[] { first, second } : new VNode[] { second, first });
        }

        [Test]
        public void Given_TwoKeyedComponentsOnOneContainer_When_ThePassThatSwapsThemIsDropped_Then_NoSlotDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReorderHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_setOrder.Invoke(1);
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region An insert and a remove

        [Component]
        private static VNode OneRowRender() => V.Label(name: "one");

        [Component]
        private static VNode ThreeRowsRender() => Rows("three", 3);

        [Component]
        private static VNode ReplacingHostRender()
        {
            var (which, setWhich) = Hooks.UseState(0);
            s_setWhich = setWhich;
            return V.Div(name: "box", children: new VNode[]
            {
                which == 0 ? V.Component(OneRowRender, key: "one") : V.Component(ThreeRowsRender, key: "three"),
                V.Component(TailRender, key: "tail"),
            });
        }

        [Test]
        public void Given_AComponentAheadOfAnotherReplacedByOneOfMoreRows_When_ThatPassIsDropped_Then_NoSlotDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReplacingHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_setWhich.Invoke(1);
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion
    }
}
