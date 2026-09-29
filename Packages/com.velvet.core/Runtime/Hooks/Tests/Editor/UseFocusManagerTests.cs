using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// <c>Hooks.UseFocusManager</c> moves focus among the focusable descendants of the scope around the calling
    /// component in hierarchy order, as React Aria's scope focus manager does: a disabled element and an element
    /// delegating its focus are never landed on, a negative <c>TabIndex</c> only when the move is not
    /// <c>Tabbable</c>.
    /// </summary>
    internal sealed class UseFocusManagerTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static FocusManager s_manager;
        private static VisualElement s_portalTarget;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_manager = null;
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

        [Component]
        private static VNode Probe()
        {
            s_manager = Hooks.UseFocusManager();
            return V.Button(name: "m1");
        }

        // In hierarchy order the scope holds m1, group, m2, skip, m3, m4, delegating and d1.
        [Component]
        private static VNode ScopeHost() => V.Div(children: new VNode[]
        {
            V.Button(name: "before"),
            V.FocusScope(name: "scope", children: new VNode[]
            {
                V.Component(Probe, key: "probe"),
                V.Div(name: "group", children: new VNode[] { V.Button(name: "m2") }),
                V.Div(name: "skip", props: new FiberElementProps { Focusable = true, TabIndex = -1 }),
                V.Button(name: "m3", enabled: false),
                V.Button(name: "m4"),
                V.Div(name: "delegating", props: new FiberElementProps { Focusable = true, DelegatesFocus = true },
                    children: new VNode[] { V.Button(name: "d1") }),
            }),
            V.Button(name: "after"),
        });

        private VisualElement Q(string name) => _host.Root.Q<VisualElement>(name);

        private void Mount(System.Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private Focusable Focused => _host.Panel.focusController.focusedElement;

        [Test]
        public void Given_AFocusedScopeMember_When_FocusNextIsCalled_Then_TheNextMemberInHierarchyOrderTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m1").Focus();

            // Act
            var moved = s_manager.FocusNext();

            // Assert — m2 sits inside a container that is not focusable itself.
            Assert.That((moved, Focused), Is.EqualTo((Q("m2"), (Focusable)Q("m2"))));
        }

        [Test]
        public void Given_TheScopesLastMemberFocused_When_FocusNextIsCalledWithoutWrap_Then_NothingMoves()
        {
            // Arrange
            Mount(ScopeHost);
            Q("d1").Focus();

            // Act
            var moved = s_manager.FocusNext(new FocusManagerOptions());

            // Assert
            Assert.That((moved, Focused), Is.EqualTo(((VisualElement)null, (Focusable)Q("d1"))));
        }

        [Test]
        public void Given_TheScopesLastMemberFocused_When_FocusNextIsCalledWithWrap_Then_TheScopesFirstMemberTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("d1").Focus();

            // Act
            var moved = s_manager.FocusNext(new FocusManagerOptions(Wrap: true));

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("m1"), (Focusable)Q("m1"))));
        }

        [Test]
        public void Given_TheScopesFirstMemberFocused_When_FocusPreviousIsCalledWithoutWrap_Then_NothingMoves()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m1").Focus();

            // Act
            var moved = s_manager.FocusPrevious();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo(((VisualElement)null, (Focusable)Q("m1"))));
        }

        [Test]
        public void Given_TheScopesFirstMemberFocused_When_FocusPreviousIsCalledWithWrap_Then_TheScopesLastMemberTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m1").Focus();

            // Act
            var moved = s_manager.FocusPrevious(new FocusManagerOptions(Wrap: true));

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("d1"), (Focusable)Q("d1"))));
        }

        [Test]
        public void Given_AMemberInsideAContainer_When_FocusPreviousIsCalled_Then_TheScopesFirstMemberBeforeTheContainerTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m2").Focus();

            // Act
            var moved = s_manager.FocusPrevious();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("m1"), (Focusable)Q("m1"))));
        }

        [Test]
        public void Given_AMemberAfterADisabledOneAndANegativeTabIndex_When_FocusPreviousIsCalled_Then_TheNegativeTabIndexTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m4").Focus();

            // Act
            var moved = s_manager.FocusPrevious();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("skip"), (Focusable)Q("skip"))));
        }

        [Test]
        public void Given_AMemberAfterADisabledOneAndANegativeTabIndex_When_FocusPreviousIsCalledTabbable_Then_BothAreSkipped()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m4").Focus();

            // Act
            var moved = s_manager.FocusPrevious(new FocusManagerOptions(Tabbable: true));

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("m2"), (Focusable)Q("m2"))));
        }

        [Test]
        public void Given_AMemberBeforeAnElementDelegatingItsFocus_When_FocusNextIsCalled_Then_TheDelegatingElementIsNotReturned()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m4").Focus();

            // Act
            var moved = s_manager.FocusNext();

            // Assert
            Assert.That(moved, Is.EqualTo(Q("d1")));
        }

        [Test]
        public void Given_FocusOutsideTheScope_When_FocusNextIsCalled_Then_TheScopesFirstMemberTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("before").Focus();

            // Act
            var moved = s_manager.FocusNext();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("m1"), (Focusable)Q("m1"))));
        }

        [Test]
        public void Given_FocusOutsideTheScope_When_FocusPreviousIsCalled_Then_TheScopesLastMemberTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("after").Focus();

            // Act
            var moved = s_manager.FocusPrevious();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("d1"), (Focusable)Q("d1"))));
        }

        [Test]
        public void Given_AContainerAsFrom_When_FocusNextIsCalled_Then_TheContainersDescendantsAreSkipped()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m1").Focus();

            // Act
            var moved = s_manager.FocusNext(new FocusManagerOptions(From: Q("group")));

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("skip"), (Focusable)Q("skip"))));
        }

        [Test]
        public void Given_AnAcceptFilter_When_FocusNextIsCalled_Then_ARejectedMemberIsSkipped()
        {
            // Arrange
            Mount(ScopeHost);
            Q("m1").Focus();

            // Act
            var moved = s_manager.FocusNext(new FocusManagerOptions(Accept: element => element.name != "m2"));

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("skip"), (Focusable)Q("skip"))));
        }

        [Test]
        public void Given_AnAcceptFilterRejectingEveryMember_When_FocusFirstIsCalled_Then_NothingMoves()
        {
            // Arrange
            Mount(ScopeHost);
            Q("after").Focus();

            // Act
            var moved = s_manager.FocusFirst(new FocusManagerOptions(Accept: _ => false));

            // Assert
            Assert.That((moved, Focused), Is.EqualTo(((VisualElement)null, (Focusable)Q("after"))));
        }

        [Test]
        public void Given_FocusOutsideTheScope_When_FocusFirstIsCalled_Then_TheScopesFirstMemberTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("after").Focus();

            // Act
            var moved = s_manager.FocusFirst();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("m1"), (Focusable)Q("m1"))));
        }

        [Test]
        public void Given_FocusOutsideTheScope_When_FocusLastIsCalled_Then_TheScopesLastMemberTakesFocus()
        {
            // Arrange
            Mount(ScopeHost);
            Q("before").Focus();

            // Act
            var moved = s_manager.FocusLast();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo((Q("d1"), (Focusable)Q("d1"))));
        }

        [Test]
        public void Given_TheScopeAsFrom_When_FocusFirstIsCalled_Then_FromIsIgnored()
        {
            // Arrange
            Mount(ScopeHost);
            Q("after").Focus();

            // Act
            var moved = s_manager.FocusFirst(new FocusManagerOptions(From: Q("scope")));

            // Assert
            Assert.That(moved, Is.EqualTo(Q("m1")));
        }

        [Component]
        private static VNode NoScopeHost() => V.Div(children: new VNode[]
        {
            V.Component(Probe, key: "probe"),
            V.Button(name: "other"),
        });

        [Test]
        public void Given_AComponentInNoFocusScope_When_FocusFirstIsCalled_Then_NothingMoves()
        {
            // Arrange
            Mount(NoScopeHost);
            Q("other").Focus();

            // Act
            var moved = s_manager.FocusFirst();

            // Assert
            Assert.That((moved, Focused), Is.EqualTo(((VisualElement)null, (Focusable)Q("other"))));
        }

        [Component]
        private static VNode ScopeDeclaringAPortal() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "scope", children: new VNode[]
            {
                V.Button(name: "s1"),
                V.Portal(s_portalTarget, key: "portal", children: new VNode[]
                {
                    V.Component(Probe, key: "probe"),
                }),
            }),
        });

        [Test]
        public void Given_AComponentInAPortalDeclaredInsideAScope_When_FocusFirstIsCalled_Then_TheScopesFirstMemberTakesFocus()
        {
            // Arrange — the portal renders into an element outside the scope.
            s_portalTarget = new VisualElement { name = "target" };
            _host.Root.Add(s_portalTarget);
            Mount(ScopeDeclaringAPortal);

            // Act
            var moved = s_manager.FocusFirst();

            // Assert
            Assert.That(moved, Is.EqualTo(Q("s1")));
        }

        [Component]
        private static VNode ScopeInsideAPortal() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "outer", children: new VNode[]
            {
                V.Button(name: "s1"),
                V.Portal(s_portalTarget, key: "portal", children: new VNode[]
                {
                    V.FocusScope(name: "inner", children: new VNode[]
                    {
                        V.Component(Probe, key: "probe"),
                    }),
                }),
            }),
        });

        [Test]
        public void Given_AComponentInAScopeInsideAPortal_When_FocusFirstIsCalled_Then_TheInnerScopesFirstMemberTakesFocus()
        {
            // Arrange
            s_portalTarget = new VisualElement { name = "target" };
            _host.Root.Add(s_portalTarget);
            Mount(ScopeInsideAPortal);

            // Act
            var moved = s_manager.FocusFirst();

            // Assert
            Assert.That(moved, Is.EqualTo(Q("m1")));
        }
    }
}
