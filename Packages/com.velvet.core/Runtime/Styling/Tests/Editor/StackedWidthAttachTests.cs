using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A stacked width inner (<c>dark:md:</c>'s <c>md:</c>, <c>dark:@md:</c>'s <c>@md:</c>) is added to its element
    /// with the outer gate already open, and the element attaches to the panel afterwards: the inner reads its width
    /// source when the attach event arrives, not before. The reconciler opens the gate with the element attached,
    /// so these build the manipulator by hand.
    /// </summary>
    [TestFixture]
    internal sealed class StackedWidthAttachTests : PanelTestBase
    {
        private Label _leaf;
        private StyleStackedVariantManipulator _stacked;

        protected override Rect WindowSize => new Rect(0, 0, 1000f, 600);

        [TearDown]
        public override void TearDown()
        {
            _leaf?.RemoveManipulator(_stacked);
            base.TearDown();
        }

        // A detached leaf carrying a stacked inner of innerKind with its outer gate open, ahead of the attach.
        private ReconcilerContext Arrange(StyleVariantKind innerKind, string innerName, long innerRank)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "container", className: "@container w-[600px]"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var context = _mounted.Root.Reconciler.Context;
            _leaf = new Label("x");
            _stacked = new StyleStackedVariantManipulator(context, innerKind, innerName,
                new string[] { "bg-wide" }, StyleLayerPriority.Stack(StyleLayerPriority.Dark, innerRank), 0);
            _leaf.AddManipulator(_stacked);
            _stacked.SetOuterGate(true);
            return context;
        }

        [Test]
        public void Given_AStackedContainerInnerAddedBeforeItsElementAttaches_When_ItAttaches_Then_ItReadsTheContainer()
        {
            // Arrange — the 600px container is above @md (448px).
            Arrange(StyleVariantKind.Container, "@md", StyleLayerPriority.ContainerMin);
            var container = _window.rootVisualElement.Q<VisualElement>("container");

            // Act
            container.Add(_leaf);

            // Assert
            Assert.That((container.resolvedStyle.width >= 448f, _leaf.ClassListContains("bg-wide")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AStackedViewportInnerAddedBeforeItsElementAttaches_When_ItAttaches_Then_ItReadsThePanel()
        {
            // Arrange — the 1000px panel is above md (768px).
            Arrange(StyleVariantKind.Md, null, StyleLayerPriority.ResponsiveMd);
            var container = _window.rootVisualElement.Q<VisualElement>("container");
            var panelRoot = container.panel.visualTree;
            panelRoot.style.width = 1000f;
            ForcePanelUpdate(container.panel);

            // Act
            container.Add(_leaf);

            // Assert
            Assert.That((panelRoot.resolvedStyle.width >= 768f, _leaf.ClassListContains("bg-wide")),
                Is.EqualTo((true, true)));
        }
    }
}
