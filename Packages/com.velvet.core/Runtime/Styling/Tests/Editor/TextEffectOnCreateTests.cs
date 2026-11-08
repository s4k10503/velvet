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

        [Test]
        public void Given_ALabelWithNoEffectClass_When_Created_Then_ItShowsItsTextInThePreflightLineHeight()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var shown = ShownBy(scope, V.Label(className: "p-2", text: "hi"));

            // Assert
            Assert.AreEqual("<line-height=1.5em>hi</line-height>", shown);
        }

        [Test]
        public void Given_ALabelWithAnEffectClass_When_Created_Then_ItShowsTheEffect()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var shown = ShownBy(scope, V.Label(className: "uppercase", text: "hi"));

            // Assert
            Assert.AreEqual("<line-height=1.5em>HI</line-height>", shown);
        }

        [Test]
        public void Given_AnEffectFreeLabelBelowAContainerWithAnEffect_When_Created_Then_ItShowsTheEffect()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var shown = ShownBy(scope, V.Div("uppercase", V.Label(className: "p-2", text: "hi")));

            // Assert
            Assert.AreEqual("<line-height=1.5em>HI</line-height>", shown);
        }

        // GREEN_ON_BASE(characterization): on the base a created label under leading-[normal] has no repaint watch, as
        // here; it is the one label that shows its text without the preflight's tag.
        [Test]
        public void Given_ALabelUnderANormalLineHeight_When_Created_Then_ItIsNotWatched()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act — leading-[normal] is the only state in which a label shows its text without a tag.
            ShownBy(scope, V.Label(className: "leading-[normal]", text: "hi"));

            // Assert
            Assert.AreEqual(0, scope.Reconciler.Context.TextShownWatches.Count);
        }
    }
}
