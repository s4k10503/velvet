using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that each removal below takes the rows it removes off the panel first to last, as React commits one
    /// parent's deletions and one Portal's rows, so their <c>DetachFromPanelEvent</c> callbacks run in that order: the
    /// general walk's, of the rows one of its deletions takes, where the new side leaves each row's slot empty; the
    /// indexed diff's, of an unkeyed list; the keyed diff's tail removal, its trimmed middle and its map pass, with the
    /// tail removal and the map pass time-sliced as well; and that of a deletion that waits for the pass to apply its
    /// writes, of the rows of a Portal inside a deleted row. One case for each.
    /// </summary>
    /// <remarks>
    /// The rows sit on a real panel, which is what runs a detach callback.
    /// </remarks>
    [TestFixture]
    internal sealed class DeletedSiblingDetachOrderTests
    {
        private const double TinyBudgetMs = 0.001;

        private HeadlessEditorPanelHost _host = null!;
        private VisualElement _container = null!;
        private Reconciler _reconciler = null!;
        private readonly List<string> _left = new();

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _container = new VisualElement();
            _host.Root.Add(_container);
            _reconciler = new Reconciler();
            _left.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            _host.Dispose();
        }

        private static VNode[] Rows(bool keyed, params string[] names)
        {
            var rows = new VNode[names.Length];
            for (var i = 0; i < names.Length; i++) rows[i] = V.Div(name: names[i], key: keyed ? names[i] : null);
            return rows;
        }

        private void LogDetachOf(VisualElement root, params string[] names)
        {
            foreach (var name in names)
            {
                root.Q<VisualElement>(name).RegisterCallback<DetachFromPanelEvent>(_ => _left.Add(name));
            }
        }

        private string Left() => string.Join(" ", _left);

        private void ResumeUntilDone()
        {
            for (var slice = 0; slice < 100 && _reconciler.HasPendingWork; slice++)
            {
                _reconciler.ContinueReconcile(frameBudgetMs: TinyBudgetMs);
            }
        }

        [Component(Compiler = false)]
        private static VNode RowsWhileShown()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(name: "rows", children: new VNode?[]
            {
                shown ? V.Div(name: "a") : null,
                shown ? V.Div(name: "b") : null,
                shown ? V.Div(name: "c") : null,
            });
        }

        [Test]
        public void Given_ThreeRowsOfAnElementOnAPanel_When_ARenderLeavesEachOfTheirSlotsEmpty_Then_TheyDetachFirstToLast()
        {
            // Arrange — a slot the new side leaves empty sends the element's children to the general walk.
            var log = new EventLog();
            using var mounted = V.Mount(_container, log.Provide(V.Component(RowsWhileShown)));
            LogDetachOf(_container, "a", "b", "c");

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(Left(), Is.EqualTo("a b c"));
        }

        [Test]
        public void Given_ThreeUnkeyedRowsBehindOneThatStays_When_AReconcileRemovesThem_Then_TheyDetachFirstToLast()
        {
            // Arrange — k is patched where it stands, so it stays on the panel.
            var before = Rows(keyed: false, "k", "a", "b", "c");
            _reconciler.Reconcile(_container, Array.Empty<VNode>(), before);
            LogDetachOf(_container, "k", "a", "b", "c");

            // Act
            _reconciler.Reconcile(_container, before, Rows(keyed: false, "k"));

            // Assert
            Assert.That(Left(), Is.EqualTo("a b c"));
        }

        [Test]
        public void Given_ThreeKeyedRowsOnAPanel_When_AReconcileRemovesThemAll_Then_TheyDetachFirstToLast()
        {
            // Arrange — the linear scan matches nothing and the new side ends there, which leaves a tail removal.
            var before = Rows(keyed: true, "a", "b", "c");
            _reconciler.Reconcile(_container, Array.Empty<VNode>(), before);
            LogDetachOf(_container, "a", "b", "c");

            // Act
            _reconciler.Reconcile(_container, before, Array.Empty<VNode>());

            // Assert
            Assert.That(Left(), Is.EqualTo("a b c"));
        }

        [Test]
        public void Given_ThreeKeyedRowsBetweenTwoThatStay_When_AReconcileRemovesThem_Then_TheyDetachFirstToLast()
        {
            // Arrange — x matches at the head and y at the tail, which leaves the middle to the suffix trim. Both are
            // patched where they stand, so they stay on the panel.
            var before = Rows(keyed: true, "x", "a", "b", "c", "y");
            _reconciler.Reconcile(_container, Array.Empty<VNode>(), before);
            LogDetachOf(_container, "x", "a", "b", "c", "y");

            // Act
            _reconciler.Reconcile(_container, before, Rows(keyed: true, "x", "y"));

            // Assert
            Assert.That(Left(), Is.EqualTo("a b c"));
        }

        [Test]
        public void Given_ThreeKeyedRowsAheadOfOneThatStays_When_AReconcileRemovesThemBesideANewRow_Then_TheyDetachFirstToLast()
        {
            // Arrange — neither end matches, which leaves the rows to the map pass.
            var before = Rows(keyed: true, "a", "b", "c", "x");
            _reconciler.Reconcile(_container, Array.Empty<VNode>(), before);
            LogDetachOf(_container, "a", "b", "c");

            // Act
            _reconciler.Reconcile(_container, before, Rows(keyed: true, "x", "z"));

            // Assert
            Assert.That(Left(), Is.EqualTo("a b c"));
        }

        [Test]
        public void Given_ThreeKeyedRowsOnAPanel_When_ATimeSlicedReconcileRemovesThemAll_Then_TheyDetachFirstToLast()
        {
            // Arrange — the time-sliced counterpart of the tail removal above.
            var before = Rows(keyed: true, "a", "b", "c");
            _reconciler.Reconcile(_container, Array.Empty<VNode>(), before);
            LogDetachOf(_container, "a", "b", "c");

            // Act
            _reconciler.Reconcile(_container, before, Array.Empty<VNode>(), frameBudgetMs: TinyBudgetMs);
            ResumeUntilDone();

            // Assert
            Assert.That(Left(), Is.EqualTo("a b c"));
        }

        [Test]
        public void Given_ThreeKeyedRowsAheadOfOneThatStays_When_ATimeSlicedReconcileRemovesThemBesideANewRow_Then_TheyDetachFirstToLast()
        {
            // Arrange — the time-sliced counterpart of the map pass above.
            var before = Rows(keyed: true, "a", "b", "c", "x");
            _reconciler.Reconcile(_container, Array.Empty<VNode>(), before);
            LogDetachOf(_container, "a", "b", "c");

            // Act
            _reconciler.Reconcile(_container, before, Rows(keyed: true, "x", "z"), frameBudgetMs: TinyBudgetMs);
            ResumeUntilDone();

            // Assert
            Assert.That(Left(), Is.EqualTo("a b c"));
        }

        [Test]
        public void Given_ARowHoldingAPortalOfThreeRows_When_AReconcileLeavesItsSlotEmpty_Then_ThePortalsRowsDetachFirstToLast()
        {
            // Arrange — the empty slot sends the row to the general walk, which defers its deletion to where the pass
            // applies its writes (DeletionCommit.Settle).
            var target = new VisualElement();
            _host.Root.Add(target);
            var before = new VNode[]
            {
                V.Div(name: "holder", children: new VNode?[]
                {
                    V.Portal(target, children: Rows(keyed: false, "a", "b", "c")),
                }),
            };
            _reconciler.Reconcile(_container, Array.Empty<VNode>(), before);
            LogDetachOf(target, "a", "b", "c");

            // Act
            _reconciler.Reconcile(_container, before, new VNode?[] { null });

            // Assert
            Assert.That(Left(), Is.EqualTo("a b c"));
        }
    }
}
