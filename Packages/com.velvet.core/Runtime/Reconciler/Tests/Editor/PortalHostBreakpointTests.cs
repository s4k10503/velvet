using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the children of a <c>V.Portal(layer:)</c> or a <c>V.WorldSpace</c> take their unscoped
    /// <c>sm:</c>…<c>2xl:</c> breakpoints from the panel the portal was declared on, the way a DOM page's
    /// portals answer the page's one viewport. Each case reads the element a portal child's breakpoints
    /// resolve their width from.
    /// </summary>
    internal sealed class PortalHostBreakpointTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Test]
        public void Given_ALayerPortalChild_When_ItsBreakpointWidthResolves_Then_ItIsTheDeclaringPanels()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "portal-child") }),
            }));
            var child = HostedElement("portal-child");

            // Act
            var widthSource = StyleResponsiveScope.ResolveWidthSource(child, child.panel.visualTree);

            // Assert
            Assert.That(widthSource, Is.SameAs(_host.Root));
        }

        [Test]
        public void Given_AWorldSpaceChild_When_ItsBreakpointWidthResolves_Then_ItIsTheDeclaringPanels()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(children: new VNode[]
            {
                V.WorldSpace(Vector3.zero, children: new VNode[] { V.Div(name: "world-child") }),
            }));
            var child = HostedElement("world-child");

            // Act
            var widthSource = StyleResponsiveScope.ResolveWidthSource(child, child.panel.visualTree);

            // Assert
            Assert.That(widthSource, Is.SameAs(_host.Root));
        }

        [Test]
        public void Given_ALayerPortalDeclaredInsideAnotherLayer_When_ItsChildsBreakpointWidthResolves_Then_ItIsTheOutermostPanels()
        {
            // Arrange — the inner portal is declared on the outer layer's host panel.
            _mounted = V.Mount(_host.Root, V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[]
                {
                    V.Portal(UILayer.Topmost, children: new VNode[] { V.Div(name: "inner-child") }),
                }),
            }));
            var child = HostedElement("inner-child");

            // Act
            var widthSource = StyleResponsiveScope.ResolveWidthSource(child, child.panel.visualTree);

            // Assert
            Assert.That(widthSource, Is.SameAs(_host.Root));
        }

        // GREEN_ON_BASE(characterization): the base mounts a layer portal declared on no panel as well.
        // This pins that recording the declaring panel passes over a portal that has none.
        [Test]
        public void Given_ALayerPortalDeclaredOnNoPanel_When_ItMounts_Then_ItsChildReachesTheHost()
        {
            // Arrange
            var reconciler = new Reconciler();
            var tree = new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "panelless-child") }),
            };

            // Act
            reconciler.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            var reached = HostedElementOrNull("panelless-child") != null;
            reconciler.Dispose();

            // Assert
            Assert.That(reached, Is.True);
        }

        private static VisualElement HostedElement(string name)
            => HostedElementOrNull(name)
                ?? throw new InvalidOperationException($"No host document holds \"{name}\".");

        private static VisualElement HostedElementOrNull(string name)
        {
            foreach (var document in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                var found = document.rootVisualElement?.Q<VisualElement>(name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
