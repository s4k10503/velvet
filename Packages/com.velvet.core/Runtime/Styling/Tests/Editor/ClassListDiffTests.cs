using System;
using NUnit.Framework;
using Velvet;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how reconciliation diffs an element's class list across re-renders:
    /// <list type="bullet">
    /// <item>Classes present in the new render but absent from the old are added to the element.</item>
    /// <item>Classes present in the old render but absent from the new are removed from the element.</item>
    /// <item>When the old and new class-name arrays are the same reference the diff is skipped, leaving the
    /// element's classes untouched.</item>
    /// <item>An unchanged class list produces no membership change.</item>
    /// <item>A completely different class list replaces every old class with the new ones.</item>
    /// </list>
    /// and how it applies the <see cref="StyleOverrides"/> inline-style prop across re-renders:
    /// <list type="bullet">
    /// <item>When an override value changes between renders the element's inline style is updated to the new value.</item>
    /// <item>An override appearing (no override to a value) applies the inline style.</item>
    /// <item>An override disappearing (a value to no override) clears the inline style back to the USS default
    /// (<see cref="StyleKeyword.Null"/>).</item>
    /// <item>Re-rendering with no override on either side, or with the same override value, leaves the element
    /// unchanged and performs no update.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <c>Reconciler.DiffStyles</c> is internal, so each style-override case drives it through the public
    /// <c>Reconcile()</c> API and observes the resulting inline style on the element.
    /// </remarks>
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

        [Test]
        public void Given_OverrideValueChanged_When_Reconciled_Then_InlineStyleUpdatedToNewValue()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Div(styles: new StyleOverrides { BackgroundColor = Color.red }),
            };
            var newTree = new VNode[]
            {
                V.Div(styles: new StyleOverrides { BackgroundColor = Color.blue }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(_root.ElementAt(0).style.backgroundColor.value, Is.EqualTo(new Color(0, 0, 1, 1)));
        }

        [Test]
        public void Given_NoOverrideThenValue_When_Reconciled_Then_InlineStyleApplied()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div() };
            var newTree = new VNode[]
            {
                V.Div(styles: new StyleOverrides { BackgroundColor = Color.green }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(_root.ElementAt(0).style.backgroundColor.value, Is.EqualTo(Color.green));
        }

        [Test]
        public void Given_ValueThenNoOverride_When_Reconciled_Then_InlineStyleClearedToNull()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Div(styles: new StyleOverrides { BackgroundColor = Color.green }),
            };
            var newTree = new VNode[] { V.Div() };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(_root.ElementAt(0).style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_NoOverrideOnEitherSide_When_Reconciled_Then_DoesNotThrow()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div() };
            var newTree = new VNode[] { V.Div() };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act + Assert
            Assert.DoesNotThrow(() => _reconciler.Reconcile(_root, oldTree, newTree));
        }

        [Test]
        public void Given_SameOverrideValue_When_Reconciled_Then_DoesNotThrow()
        {
            // Arrange
            var styles = new StyleOverrides { BackgroundColor = Color.red };
            var oldTree = new VNode[] { V.Div(styles: styles) };
            var newTree = new VNode[] { V.Div(styles: styles) };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act + Assert
            Assert.DoesNotThrow(() => _reconciler.Reconcile(_root, oldTree, newTree));
        }
    }
}
