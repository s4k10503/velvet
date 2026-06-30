using System;
using NUnit.Framework;
using Velvet;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how reconciliation applies the <see cref="StyleOverrides"/> inline-style prop across re-renders.
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
    /// <c>Reconciler.DiffStyles</c> is internal, so each case drives it through the public
    /// <c>Reconcile()</c> API and observes the resulting inline style on the element.
    /// </remarks>
    [TestFixture]
    internal sealed class StyleDiffTests
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
