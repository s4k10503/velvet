using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What a text leaf shows when it is created: the entry that skips the effect pass for a leaf carrying no effect
    /// class must leave the leaf showing what the full pass would have shown. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextEffectOnCreateTests
    {
        private static string ShownBy(ReconcilerScope scope, VNode node)
        {
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { node });
            var label = scope.Root.Q<Label>();
            return StyleTextEffectResolver.DisplayedText(scope.Reconciler.Context, label);
        }

        // GREEN_ON_BASE(characterization): the base runs the full pass for every created leaf; this holds the skipped one to its result.
        [Test]
        public void Given_ALabelWithNoEffectClass_When_Created_Then_ItShowsItsText()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var shown = ShownBy(scope, V.Label(className: "p-2", text: "hi"));

            // Assert
            Assert.AreEqual("hi", shown);
        }

        // GREEN_ON_BASE(characterization): the base runs the full pass for every created leaf; this holds the leaf that must not skip it.
        [Test]
        public void Given_ALabelWithAnEffectClass_When_Created_Then_ItShowsTheEffect()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var shown = ShownBy(scope, V.Label(className: "uppercase", text: "hi"));

            // Assert
            Assert.AreEqual("HI", shown);
        }

        // GREEN_ON_BASE(characterization): the base resolves the ancestor's effect onto the leaf as well; this holds the skipped leaf to it.
        [Test]
        public void Given_AnEffectFreeLabelBelowAContainerWithAnEffect_When_Created_Then_ItShowsTheEffect()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var shown = ShownBy(scope, V.Div("uppercase", V.Label(className: "p-2", text: "hi")));

            // Assert
            Assert.AreEqual("HI", shown);
        }

        // GREEN_ON_BASE(characterization): the base runs the full pass and the leaf is left without a repaint watch either way.
        [Test]
        public void Given_ALabelWithNoEffectClass_When_Created_Then_ItIsNotWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            ShownBy(scope, V.Label(className: "p-2", text: "hi"));

            // Assert
            Assert.AreEqual(0, scope.Reconciler.Context.TextShownWatches.Count);
        }
    }
}
