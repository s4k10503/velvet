using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a face stash hands back to an element it suppresses when it lets the face go: a colour written over
    /// the suppression, by a ref or by the render that removes the stash's class; an inline border colour the element
    /// declares, after its background was captured again; no inline border colour where the element declares none;
    /// and, where a shadow took the face from a dashed border, the dashed border's inline colour, unless the render
    /// that added the shadow removed it.
    /// </summary>
    internal sealed class SilhouetteFaceReleaseTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp() => VelvetTheme.IsDark = false;

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            VelvetTheme.IsDark = false;
        }

        private static VisualElement Mount(ReconcilerScope scope, VNode[] tree)
        {
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            return scope.Root.Q<VisualElement>("card");
        }

        private static VNode Card(string className) => V.Div(name: "card", key: "card", className: className);

        // On a panel, with a style pass after each render, so a capture reads a masked slot's resolved value as the
        // sentinel that masks it.
        private VisualElement MountOnPanel(string className)
        {
            _host = new HeadlessEditorPanelHost();
            _mounted = V.Mount(_host.Root, Card(className));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            return _host.Root.Q<VisualElement>("card");
        }

        private void Render(string className)
        {
            _mounted.Render(Card(className));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
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

        [Test]
        public void Given_AShadowedElement_When_OneRenderChangesItsBackgroundAndRemovesItsShadow_Then_ItShowsTheNewBackground()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "w-[50px] h-[50px] bg-[#0000ff] shadow-lg", name: "card") };
            var card = Mount(scope, before);
            var suppressed = SilhouetteFace.IsSentinel(card.style.backgroundColor.value);

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "w-[50px] h-[50px] bg-[#ff0000]", name: "card") });

            // Assert — null where the shadow never suppressed the background.
            Assert.That(suppressed ? card.style.backgroundColor.value : (Color?)null, Is.EqualTo(Color.red));
        }

        // GREEN_ON_BASE(characterization): the base leaves a border colour the element never set inline unset; writing `HasStash` alone for the flag a masked side starts from in SilhouetteFaceStash.Capture reddens it.
        [Test]
        public void Given_AShadowedElementWithNoInlineBorderColour_When_ItUnmounts_Then_ItsBorderColourIsLeftUnset()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "w-[50px] h-[50px] bg-[#0000ff] shadow-lg", name: "card") };
            var card = Mount(scope, tree);
            var suppressed = SilhouetteFace.IsSentinel(card.style.borderLeftColor.value);

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

        // GREEN_ON_BASE(characterization): the base hands a shadow the dashed border's inline colour and back; deleting the copy of the side's inline flag from SilhouetteFaceStash.AdoptBorderFace reddens it.
        // The case after it holds the opposite verdict for a colour the same render removed, which a hand-off that
        // copied nothing would satisfy alone.
        [Test]
        public void Given_ADashedBorderWithAnInlineColourARenderAddsAShadowTo_When_ALaterRenderRemovesTheShadow_Then_TheDashedFaceHoldsThatColourInline()
        {
            // Arrange
            var card = MountOnPanel("border-dashed border-[#00ff00]");
            Render("border-dashed border-[#00ff00] shadow-lg");

            // Act
            Render("border-dashed border-[#00ff00]");

            // Assert — null where no dashed face suppresses the left side.
            Assert.That(SilhouetteFaceStash.TryReadFace(card, DivideEdge.Left, out _, out var fromInline) ? fromInline : (bool?)null,
                Is.True);
        }

        [Test]
        public void Given_ADashedBorderWhoseColourTheRenderAddingAShadowRemoved_When_ALaterRenderRemovesTheShadow_Then_TheDashedFaceHoldsNoInlineColour()
        {
            // Arrange
            var card = MountOnPanel("border-dashed border-[#00ff00]");
            Render("border-dashed shadow-lg");

            // Act
            Render("border-dashed");

            // Assert — null where no dashed face suppresses the left side.
            Assert.That(SilhouetteFaceStash.TryReadFace(card, DivideEdge.Left, out _, out var fromInline) ? fromInline : (bool?)null,
                Is.False);
        }

        // GREEN_ON_BASE(characterization): the base's next re-capture clears a flag the shadow adopted; dropping the skip of a side the new owner read from SilhouetteFaceStash.AdoptBorderFace reddens it.
        [Test]
        public void Given_ADashedBorderWhoseColourTheRenderAddingAShadowRemoved_When_TheBackgroundChangesAndThenTheShadowGoes_Then_TheDashedFaceHoldsNoInlineColour()
        {
            // Arrange — the background the third render adds is a write over the shadow's suppression, which its face
            // captures again.
            var card = MountOnPanel("border-dashed border-[#00ff00]");
            Render("border-dashed shadow-lg");
            Render("border-dashed shadow-lg bg-[#ffffff]");

            // Act
            Render("border-dashed bg-[#ffffff]");

            // Assert — null where no dashed face suppresses the left side.
            Assert.That(SilhouetteFaceStash.TryReadFace(card, DivideEdge.Left, out _, out var fromInline) ? fromInline : (bool?)null,
                Is.False);
        }
    }
}
