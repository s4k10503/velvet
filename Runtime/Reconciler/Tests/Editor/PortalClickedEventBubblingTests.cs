using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The click a <see cref="ClickedEventBinding"/> answers crosses a <c>V.Portal(layer:)</c> under the gate
    /// <see cref="PortalClickAndChangeBubblingTests"/> pins for <see cref="ClickedBinding"/>: an enabled
    /// <c>Button</c> answers a click and nothing else, and a non-Button answers nothing. The native event is
    /// simulated at the host panel's root, where the bridge listens, as that fixture does.
    /// </summary>
    [TestFixture]
    internal sealed class PortalClickedEventBubblingTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<string> _log = new();

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _log.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private FiberEventBinding[] LogClick() => new FiberEventBinding[]
        {
            new ClickedEventBinding { Handler = _ => _log.Add("click") },
        };

        private static VNode[] PortalOfAChild() => new VNode[]
        {
            V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "portal-child") }),
        };

        private static (VisualElement HostRoot, VisualElement Child) PortalChild()
        {
            foreach (var doc in UnityEngine.Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                var child = doc.rootVisualElement?.Q<VisualElement>("portal-child");
                if (child != null)
                {
                    return (doc.rootVisualElement, child);
                }
            }
            throw new InvalidOperationException("The layer portal's child mounted under no host document.");
        }

        [Test]
        public void Given_AButtonAroundALayerPortal_When_AClickBubblesToTheHostRoot_Then_ItsClickedEventBindingRuns()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Button(events: LogClick(), children: PortalOfAChild()));
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = ClickEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("click"));
        }

        [Test]
        public void Given_ADisabledButtonAroundALayerPortal_When_AClickBubblesToTheHostRoot_Then_ItsClickedEventBindingDoesNotRun()
        {
            // Arrange — the pointer-down binding beside it runs, which tells a refused click from a bridge
            // that carried nothing.
            var events = new FiberEventBinding[]
            {
                new ClickedEventBinding { Handler = _ => _log.Add("click") },
                new PointerDownBinding { Handler = _ => _log.Add("down") },
            };
            _mounted = V.Mount(_host.Root, V.Button(enabled: false, events: events, children: PortalOfAChild()));
            var (hostRoot, child) = PortalChild();

            // Act
            using var click = ClickEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(click, child);
            using var down = PointerDownEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(down, child);

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("down"));
        }

        [Test]
        public void Given_AButtonAroundALayerPortal_When_APointerDownBubblesToTheHostRoot_Then_ItsClickedEventBindingDoesNotRun()
        {
            // Arrange — the pointer-down binding beside it runs, which tells the bridge carrying the event
            // from the click binding refusing it.
            var events = new FiberEventBinding[]
            {
                new ClickedEventBinding { Handler = _ => _log.Add("click") },
                new PointerDownBinding { Handler = _ => _log.Add("down") },
            };
            _mounted = V.Mount(_host.Root, V.Button(events: events, children: PortalOfAChild()));
            var (hostRoot, child) = PortalChild();

            // Act
            using var evt = PointerDownEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(evt, child);

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("down"));
        }

        [Test]
        public void Given_AClickedEventBindingOnANonButtonAroundALayerPortal_When_AClickBubblesToTheHostRoot_Then_NothingRuns()
        {
            // Arrange — the pointer-down binding beside it runs, as in the case above.
            var events = new FiberEventBinding[]
            {
                new ClickedEventBinding { Handler = _ => _log.Add("click") },
                new PointerDownBinding { Handler = _ => _log.Add("down") },
            };
            _mounted = V.Mount(_host.Root, V.Div(events: events, children: PortalOfAChild()));
            var (hostRoot, child) = PortalChild();

            // Act
            using var click = ClickEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(click, child);
            using var down = PointerDownEvent.GetPooled();
            hostRoot.SimulateBubbledEvent(down, child);

            // Assert
            Assert.That(string.Join(",", _log), Is.EqualTo("down"));
        }
    }
}
