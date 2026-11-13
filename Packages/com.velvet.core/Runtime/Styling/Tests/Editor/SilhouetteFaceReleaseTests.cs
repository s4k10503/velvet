using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a face stash hands back to an element it suppresses when the element unmounts: a colour written
    /// over the suppression through a ref, an inline border colour the element declares, after its background was
    /// captured again, and no inline border colour where the element declares none.
    /// </summary>
    internal sealed class SilhouetteFaceReleaseTests
    {
        [TearDown]
        public void TearDown() => VelvetTheme.IsDark = false;

        private static VisualElement Mount(ReconcilerScope scope, VNode[] tree)
        {
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            return scope.Root.Q<VisualElement>("card");
        }

        [Test]
        public void Given_AShadowedElementWhoseBackgroundARefWrote_When_ItUnmounts_Then_ItKeepsThatBackground()
        {
            // Arrange — the write stands for app code reaching the element through a ref.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "w-[50px] h-[50px] bg-[#0000ff] shadow-lg", name: "card") };
            var card = Mount(scope, tree);
            var suppressed = SilhouetteFace.IsSentinel(card.style.backgroundColor.value);
            card.style.backgroundColor = Color.red;

            // Act
            scope.Reconciler.Reconcile(scope.Root, tree, Array.Empty<VNode>());

            // Assert — null where the shadow never suppressed the background.
            Assert.That(suppressed ? card.style.backgroundColor.value : (Color?)null, Is.EqualTo(Color.red));
        }

        // GREEN_ON_BASE(characterization): the base leaves a border colour the element never set inline unset; writing `HasStash` alone for the flag a masked side starts from in SilhouetteFaceStash.Capture reddens it.
        [Test]
        public void Given_AShadowedElementWhoseBackgroundARefWrote_When_ItUnmounts_Then_ItsBorderColourIsLeftUnset()
        {
            // Arrange — no class colours the border inline.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "w-[50px] h-[50px] bg-[#0000ff] shadow-lg", name: "card") };
            var card = Mount(scope, tree);
            var suppressed = SilhouetteFace.IsSentinel(card.style.borderLeftColor.value);
            card.style.backgroundColor = Color.red;

            // Act
            scope.Reconciler.Reconcile(scope.Root, tree, Array.Empty<VNode>());

            // Assert — null where the shadow never suppressed the border colour.
            Assert.That(suppressed ? card.style.borderLeftColor.keyword : (StyleKeyword?)null, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ADashedElementWhoseBorderColourARefWrote_When_ItUnmounts_Then_ItKeepsThatBorderColour()
        {
            // Arrange — the write stands for app code reaching the element through a ref.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "w-[50px] h-[50px] border-dashed border-[#0000ff]", name: "card") };
            var card = Mount(scope, tree);
            var suppressed = SilhouetteFace.IsSentinel(card.style.borderLeftColor.value);
            card.style.borderLeftColor = Color.red;

            // Act
            scope.Reconciler.Reconcile(scope.Root, tree, Array.Empty<VNode>());

            // Assert — null where the dashed border never suppressed the border colour.
            Assert.That(suppressed ? card.style.borderLeftColor.value : (Color?)null, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AShadowedElementWithAnInlineBorderColourWhoseBackgroundAVariantChanged_When_ItUnmounts_Then_ItKeepsItsBorderColour()
        {
            // Arrange — the dark: payload writes the background over the suppression, and the face captures it again.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "w-[50px] h-[50px] bg-[#0000ff] border-[#00ff00] shadow-lg dark:bg-[#ff0000]",
                    name: "card"),
            };
            var card = Mount(scope, tree);
            var suppressed = SilhouetteFace.IsSentinel(card.style.borderLeftColor.value);
            VelvetTheme.IsDark = true;

            // Act
            scope.Reconciler.Reconcile(scope.Root, tree, Array.Empty<VNode>());

            // Assert — null where the shadow never suppressed the border colour.
            Assert.That(suppressed ? card.style.borderLeftColor.value : (Color?)null, Is.EqualTo(Color.green));
        }
    }
}
