using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class FlatPathDuplicateKeyTests
    {
        private Reconciler _reconciler;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
        }

        private static int DuplicateWarningsDuring(Action act)
        {
            var count = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains("Duplicate key detected")) count++;
            }

            Application.logMessageReceived += OnLog;
            try
            {
                act();
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            return count;
        }

        [Test]
        public void Given_TwoSiblingsSharingAKey_When_Mounted_Then_TheRepeatIsReportedOnce()
        {
            // Arrange
            var children = new VNode[] { V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B") };

            // Act
            var warnings = DuplicateWarningsDuring(() => _reconciler.Reconcile(_root, Array.Empty<VNode>(), children));

            // Assert — both render beside the one warning
            Assert.That((_root.childCount, warnings), Is.EqualTo((2, 1)));
        }

        [Test]
        public void Given_TwoSiblingsSharingAKey_When_RenderedAgainInTheSameOrder_Then_TheRepeatIsReportedOnce()
        {
            // Arrange
            var oldChildren = new VNode[] { V.Label(key: "x", text: "A0"), V.Label(key: "x", text: "B0") };
            var newChildren = new VNode[] { V.Label(key: "x", text: "A1"), V.Label(key: "x", text: "B1") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            var warnings = DuplicateWarningsDuring(() => _reconciler.Reconcile(_root, oldChildren, newChildren));

            // Assert
            Assert.That(warnings, Is.EqualTo(1));
        }

        [Test]
        public void Given_TwoSiblingsSharingAKey_When_RenderedAgainTimeSliced_Then_TheRepeatIsReportedOnce()
        {
            // Arrange
            var oldChildren = new VNode[] { V.Label(key: "x", text: "A0"), V.Label(key: "x", text: "B0") };
            var newChildren = new VNode[] { V.Label(key: "x", text: "A1"), V.Label(key: "x", text: "B1") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            var warnings = DuplicateWarningsDuring(() =>
            {
                _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
                for (var i = 0; i < 100 && _reconciler.HasPendingWork; i++) _reconciler.ContinueReconcile(frameBudgetMs: 0.001);
            });

            // Assert — the render drained, beside the count
            Assert.That((_reconciler.HasPendingWork, warnings), Is.EqualTo((false, 1)));
        }

        // GREEN_ON_BASE(characterization): the base reported a new-side repeat once, from the map pass alone.
        // What this pins is that the warning every render now gives is not added to that one.
        [Test]
        public void Given_ARepeatTheMapPassResolves_When_Rendered_Then_TheRepeatIsReportedOnce()
        {
            // Arrange
            var oldChildren = new VNode[] { V.Label(key: "x", text: "X") };
            var newChildren = new VNode[]
            {
                V.Label(key: "y", text: "Y"), V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            var warnings = DuplicateWarningsDuring(() => _reconciler.Reconcile(_root, oldChildren, newChildren));

            // Assert — every declared row beside the one warning
            Assert.That((_root.childCount, warnings), Is.EqualTo((3, 1)));
        }

        [Test]
        public void Given_RepeatsOnlyInThePreviousRender_When_RenderedWithDistinctKeys_Then_NothingIsReported()
        {
            // Arrange
            var oldChildren = new VNode[]
            {
                V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B"), V.Label(key: "y", text: "Y"),
            };
            var newChildren = new VNode[] { V.Label(key: "y", text: "Y"), V.Label(key: "x", text: "A") };
            var previousWarnings = DuplicateWarningsDuring(() => _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren));

            // Act
            var warnings = DuplicateWarningsDuring(() => _reconciler.Reconcile(_root, oldChildren, newChildren));

            // Assert — the render committed, beside the count
            Assert.That((previousWarnings, _root.childCount, warnings), Is.EqualTo((1, 2, 0)));
        }

        // GREEN_ON_BASE(characterization): the base's suffix trim already declined both sides holding a repeat.
        // Let the trim take them (drop both uniqueness terms
        // from its gate) and each repeat keeps its own element: this reddens.
        [Test]
        public void Given_TwoSiblingsSharingAKeyAfterAnother_When_ThatOneIsDropped_Then_TheFirstTakesTheLastElementForTheKey()
        {
            // Arrange
            var oldChildren = new VNode[]
            {
                V.Label(key: "y", text: "Y"), V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B"),
            };
            var newChildren = new VNode[] { V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            var (first, second) = (_root.ElementAt(1), _root.ElementAt(2));

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert — the first repeat is on the second's element, and the second on neither.
            Assert.That(
                (_root.childCount, ReferenceEquals(_root.ElementAt(0), second),
                    ReferenceEquals(_root.ElementAt(1), first) || ReferenceEquals(_root.ElementAt(1), second)),
                Is.EqualTo((2, true, false)));
        }

        // GREEN_ON_BASE(characterization): the base's suffix trim already declined a new side holding a repeat.
        // Drop the new side's
        // uniqueness term from the trim's gate and the old element goes to the last: this reddens.
        [Test]
        public void Given_AKeyRepeatedByARenderAddingASiblingBefore_When_Rendered_Then_TheFirstRepeatTakesTheOldElement()
        {
            // Arrange
            var oldChildren = new VNode[] { V.Label(key: "x", text: "X") };
            var newChildren = new VNode[]
            {
                V.Label(key: "z", text: "Z"), V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            var old = _root.ElementAt(0);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert
            Assert.That((_root.childCount, ReferenceEquals(_root.ElementAt(1), old)), Is.EqualTo((3, true)));
        }

        // The mark-and-read-back InlineOwnerListPoolingTests gives its key-set case, on a render the linear
        // prefix finishes, where no pass after the report rents a key set.
        [Test]
        public void Given_AKeyedRender_When_ItEnds_Then_TheKeySetTheReportRentedIsBackInThePool()
        {
            // Arrange
            var pool = _reconciler.Context.BufferPool;
            var oldChildren = new VNode[] { V.Label(key: "a", text: "A0"), V.Label(key: "b", text: "B0") };
            var newChildren = new VNode[] { V.Label(key: "a", text: "A1"), V.Label(key: "b", text: "B1") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            var borrowed = pool.RentKeySet();
            pool.ReturnKeySet(borrowed);
            borrowed.Add(ChildKey.Explicit("mark"));
            var marked = borrowed.Count;

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);
            var left = borrowed.Count;

            // Assert
            Assert.That(
                (marked, ReferenceEquals(pool.RentKeySet(), borrowed), left),
                Is.EqualTo((1, true, 0)));
        }
    }
}
