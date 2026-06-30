using System;
using NUnit.Framework;
using Velvet;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how reconciliation diffs an element's class list across re-renders.
    /// <list type="bullet">
    /// <item>Classes present in the new render but absent from the old are added to the element.</item>
    /// <item>Classes present in the old render but absent from the new are removed from the element.</item>
    /// <item>When the old and new class-name arrays are the same reference the diff is skipped, leaving the
    /// element's classes untouched.</item>
    /// <item>An unchanged class list produces no membership change.</item>
    /// <item>A completely different class list replaces every old class with the new ones.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ClassListDiffTests
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

        [Test]
        public void Given_NewClassesAdded_When_Reconciled_Then_ElementGainsExactlyThoseClasses()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div() };
            var newTree = new VNode[] { V.Div("btn btn--primary") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            CollectionAssert.AreEquivalent(
                new[] { "btn", "btn--primary" }, _root.ElementAt(0).GetClasses());
        }

        [Test]
        public void Given_ClassesRemoved_When_Reconciled_Then_ElementRetainsOnlySurvivingClasses()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div("btn btn--primary btn--large") };
            var newTree = new VNode[] { V.Div("btn") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            CollectionAssert.AreEquivalent(new[] { "btn" }, _root.ElementAt(0).GetClasses());
        }

        [Test]
        public void Given_SameClassNamesArrayReference_When_Reconciled_Then_ClassesAreLeftUntouched()
        {
            // Arrange — both renders share the same ClassNames reference, so the diff is skipped
            var classNames = new[] { "btn", "btn--primary" };
            var oldNode = new ElementNode
            {
                ElementType = typeof(VisualElement),
                ClassNames = classNames,
                Children = Array.Empty<VNode>(),
                Events = Array.Empty<FiberEventBinding>(),
            };
            var newNode = new ElementNode
            {
                ElementType = typeof(VisualElement),
                ClassNames = classNames,
                Children = Array.Empty<VNode>(),
                Events = Array.Empty<FiberEventBinding>(),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), new VNode[] { oldNode });
            var element = _root.ElementAt(0);
            Assume.That(element.GetClasses(), Is.EquivalentTo(new[] { "btn", "btn--primary" }),
                "Precondition: the first render applied both classes");

            // Act
            _reconciler.Reconcile(_root, new VNode[] { oldNode }, new VNode[] { newNode });

            // Assert
            CollectionAssert.AreEquivalent(new[] { "btn", "btn--primary" }, element.GetClasses());
        }

        [Test]
        public void Given_UnchangedClassList_When_Reconciled_Then_MembershipIsUnchanged()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div("btn btn--primary") };
            var newTree = new VNode[] { V.Div("btn btn--primary") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            CollectionAssert.AreEquivalent(
                new[] { "btn", "btn--primary" }, _root.ElementAt(0).GetClasses());
        }

        [Test]
        public void Given_CompletelyDifferentClassList_When_Reconciled_Then_OldClassesReplacedByNew()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div("old-a old-b") };
            var newTree = new VNode[] { V.Div("new-x new-y") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            CollectionAssert.AreEquivalent(
                new[] { "new-x", "new-y" }, _root.ElementAt(0).GetClasses());
        }
    }
}
