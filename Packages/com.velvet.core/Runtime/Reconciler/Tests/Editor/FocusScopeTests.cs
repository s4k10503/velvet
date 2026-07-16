using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the focus-scope layer's sequential semantics on a real (headless) editor panel — the editor
    /// focus ring maps NavigationMoveEvent Next/Previous exactly like the runtime ring's own sequential
    /// delegate, so Tab-shaped moves dispatch deterministically here via SendEvent: containment wraps within
    /// the scope, RestoreFocus returns focus where it came from on unmount, AutoFocus lands on the first
    /// focusable descendant at attach, and SingleTabStop makes a whole subtree one Tab stop (exit skips the
    /// remaining members; re-entry lands on the member last used).
    /// </summary>
    internal sealed class FocusScopeTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setShowScope;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_setShowScope = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private VisualElement Q(string name) => _host.Root.Q<VisualElement>(name);

        // Focus-ring membership requires every element's resolved display state, which batchmode never
        // computes on its own — the same forced style pass resolvedStyle-reading fixtures already use.
        private void Mount(System.Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void SendMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using var move = NavigationMoveEvent.GetPooled(direction);
            move.target = target;
            target.SendEvent(move);
        }

        [Component]
        private static VNode ContainedScopeHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "scope", contain: true, children: new VNode[]
            {
                V.Button(name: "in1"),
                V.Button(name: "in2"),
            }),
            V.Button(name: "outside"),
        });

        [Test]
        public void Given_AContainedFocusScope_When_TabDispatchesFromItsLastFocusable_Then_FocusWrapsToTheScopesFirstFocusable()
        {
            // Arrange
            Mount(ContainedScopeHost);
            var in2 = Q("in2");
            in2.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(in2),
                "Precondition: the scope's last focusable holds focus");

            // Act
            SendMove(in2, NavigationMoveEvent.Direction.Next);

            // Assert — without containment the move would land on "outside"; the scoped ring wraps instead.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("in1")));
        }

        [Component]
        private static VNode RestoreScopeHost()
        {
            var (showScope, setShowScope) = Hooks.UseState(true);
            s_setShowScope = setShowScope;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "opener"),
                showScope
                    ? V.FocusScope(name: "scope", key: "scope", restoreFocus: true, children: new VNode[]
                    {
                        V.Button(name: "inner"),
                    })
                    : null,
            });
        }

        [Test]
        public void Given_AFocusScopeWithRestoreFocus_When_TheScopeUnmountsWhileHoldingFocus_Then_TheElementFocusCameFromRegainsFocus()
        {
            // Arrange — focus enters the scope FROM the opener (the navigator's FocusIn bookkeeping
            // captures the opener as the restore target on that first entry).
            Mount(RestoreScopeHost);
            var opener = Q("opener");
            opener.Focus();
            Q("inner").Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("inner")),
                "Precondition: focus sits inside the scope");

            // Act
            s_setShowScope.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(opener));
        }

        [Component]
        private static VNode AutoFocusScopeHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "scope", autoFocus: true, children: new VNode[]
            {
                V.Button(name: "first"),
                V.Button(name: "second"),
            }),
        });

        [Test]
        public void Given_AFocusScopeWithAutoFocus_When_ItAttachesToAPanel_Then_ItsFirstFocusableDescendantHoldsFocus()
        {
            // Arrange & Act
            Mount(AutoFocusScopeHost);

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("first")));
        }

        [Component]
        private static VNode SingleTabStopHost() => V.Div(children: new VNode[]
        {
            V.Button(name: "before"),
            V.FocusScope(name: "group", singleTabStop: true, children: new VNode[]
            {
                V.Button(name: "g1"),
                V.Button(name: "g2"),
                V.Button(name: "g3"),
            }),
            V.Button(name: "after"),
        });

        [Test]
        public void Given_ASingleTabStopScopeWithAFocusedMember_When_TabDispatches_Then_FocusSkipsPastTheRemainingMembers()
        {
            // Arrange
            Mount(SingleTabStopHost);
            var g1 = Q("g1");
            g1.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(g1),
                "Precondition: a group member holds focus");

            // Act
            SendMove(g1, NavigationMoveEvent.Direction.Next);

            // Assert — the whole group is one Tab stop: g2/g3 are skipped.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("after")));
        }

        [Test]
        public void Given_ASingleTabStopScopeLastLeftFromItsSecondMember_When_TabEntersFromOutside_Then_TheSecondMemberRegainsFocus()
        {
            // Arrange — g2 held focus once (recording it as the group's roving tab stop), then focus moved
            // outside the group.
            Mount(SingleTabStopHost);
            Q("g2").Focus();
            var before = Q("before");
            before.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(before),
                "Precondition: focus sits outside the group");

            // Act
            SendMove(before, NavigationMoveEvent.Direction.Next);

            // Assert — entry lands on the remembered member, not the group's first.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("g2")));
        }

        [Test]
        public void Given_APooledElementThatCarriedATabIndex_When_ResetForThePool_Then_ItsTabIndexIsZero()
        {
            // Arrange
            var element = new VisualElement { tabIndex = 5, delegatesFocus = true };

            // Act
            FiberElementPoolReset.ResetCommonState(element);

            // Assert
            Assert.That((element.tabIndex, element.delegatesFocus), Is.EqualTo((0, false)));
        }

        [Test]
        public void Given_AButtonRecycledThroughThePool_When_RentedAgain_Then_ItIsFocusableLikeAFreshOne()
        {
            // Arrange — the pool's common reset scrubs focusable to the plain-VisualElement default (false),
            // which is NOT a Button's own constructor default: without the type-specific restore, a recycled
            // button silently drops out of Tab/gamepad navigation.
            var button = VNodePool.RentButton();

            // Act
            VNodePool.ReturnButton(button);
            var reused = VNodePool.RentButton();

            // Assert
            Assert.That(reused.focusable, Is.True);
        }
    }
}
