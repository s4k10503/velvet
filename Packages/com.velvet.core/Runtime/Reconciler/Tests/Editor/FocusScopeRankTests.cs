using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Which landing a contained scope lets stand within one panel: one logically inside it, a portal's
    /// content included, or one inside a contained scope created after it. Every other landing is snapped
    /// back, whatever plain scope it sits in.
    /// </summary>
    internal sealed class FocusScopeRankTests
    {
        private static VisualElement s_portalTarget;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_portalTarget = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            s_portalTarget = null;
        }

        private VisualElement Q(string name) => _host.Root.Q<VisualElement>(name);

        private void Mount(VisualElement into, System.Func<VNode> body)
        {
            _mounted = V.Mount(into, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void FocusAndAssume(string name)
        {
            var element = Q(name);
            element.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(element),
                "Precondition: the scope under test holds focus");
        }

        // "modalB" is declared after its sibling "modalA", so its scope is created after it.
        [Component]
        private static VNode TwoContainedScopesHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modalA", contain: true, children: new VNode[]
            {
                V.Button(name: "a1"),
            }),
            V.FocusScope(name: "modalB", contain: true, children: new VNode[]
            {
                V.Button(name: "b1"),
            }),
        });

        [Test]
        public void Given_TwoContainedScopes_When_FocusMovesFromTheNewerIntoTheOlder_Then_TheNewerScopeTakesItBack()
        {
            // Arrange
            Mount(_host.Root, TwoContainedScopesHost);
            FocusAndAssume("b1");

            // Act
            Q("a1").Focus();

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("b1")));
        }

        [Component]
        private static VNode ContainedScopeBeforeAPlainScope() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
            }),
            V.FocusScope(name: "plain", children: new VNode[]
            {
                V.Button(name: "p1"),
            }),
        });

        // GREEN_ON_BASE(characterization): the base snaps a landing in a plain scope back into the modal.
        // This case pins that a plain scope created after the modal does not rank as a newer contain scope.
        [Test]
        public void Given_AContainedScope_When_FocusMovesIntoAPlainScopeCreatedAfterIt_Then_TheScopeTakesItBack()
        {
            // Arrange
            Mount(_host.Root, ContainedScopeBeforeAPlainScope);
            FocusAndAssume("m1");

            // Act
            Q("p1").Focus();

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("m1")));
        }

        [Component]
        private static VNode ModalDeclaringASamePanelPortal() => V.FocusScope(name: "modal", contain: true,
            children: new VNode[]
            {
                V.Button(name: "m1"),
                V.Portal(s_portalTarget, key: "menu", children: new VNode[]
                {
                    V.Button(name: "menuItem"),
                }),
            });

        [Test]
        public void Given_AContainedScope_When_FocusMovesToAPortalItDeclaredIntoTheSamePanel_Then_FocusStaysInThePortal()
        {
            // Arrange — the portal's target sits in the same panel, outside the modal.
            var app = new VisualElement { name = "app" };
            s_portalTarget = new VisualElement { name = "overlay" };
            _host.Root.Add(app);
            _host.Root.Add(s_portalTarget);
            Mount(app, ModalDeclaringASamePanelPortal);
            FocusAndAssume("m1");

            // Act
            Q("menuItem").Focus();

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("menuItem")));
        }
    }
}
