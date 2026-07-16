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
        private static StateUpdater<bool> s_setRotated;
        private static StateUpdater<bool> s_setShowOpener;
        private static StateUpdater<bool> s_setAutoFocusOn;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_setShowScope = default;
            s_setRotated = default;
            s_setShowOpener = default;
            s_setAutoFocusOn = default;
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

        [Component]
        private static VNode TwoScopesHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
                V.Button(name: "m2"),
            }),
            V.FocusScope(name: "drawer", restoreFocus: true, children: new VNode[]
            {
                V.Button(name: "d1"),
            }),
        });

        [Test]
        public void Given_AContainedScopeHoldingFocus_When_FocusEscapesToAnElementInsideAnotherScope_Then_FocusSnapsBackToTheContainedScope()
        {
            // Arrange
            Mount(TwoScopesHost);
            var m1 = Q("m1");
            m1.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(m1),
                "Precondition: the contained scope holds focus");

            // Act — a pointer-press-shaped escape whose landing sits inside a DIFFERENT scope (the
            // drawer): containment must hold no matter where the escape landed, not only for
            // scope-less destinations.
            Q("d1").Focus();

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(m1));
        }

        [Test]
        public void Given_AContainedScopeHoldingFocus_When_FocusIsClearedToNothing_Then_TheScopeRegainsFocusOnTheNextTick()
        {
            // Arrange
            Mount(ContainedScopeHost);
            var in1 = Q("in1");
            in1.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(in1),
                "Precondition: the scope holds focus");

            // Act — a press on empty non-focusable space clears focus to NOTHING: no FocusIn ever
            // fires for the snap-back to ride, only a FocusOut with no destination, so the
            // containment re-focus rides the panel's next scheduler tick instead.
            in1.Blur();
            Assume.That(_host.Panel.focusController.focusedElement, Is.Null,
                "Precondition: focus was cleared to nothing");
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(in1));
        }

        [Component]
        private static VNode StuckRingHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
            }),
            V.Button(name: "outside", className: "focus-visible:bg-blue-400"),
        });

        [Test]
        public void Given_ALandingSnappedBackIntoAContainedScope_When_TheRevertedElementKeepsAFocusVisibleResidue_Then_TheNextTickSettlesIt()
        {
            // Arrange — a real escape that the containment reverts: the snap-back schedules a next-tick
            // settle for the reverted element.
            Mount(StuckRingHost);
            var m1 = Q("m1");
            m1.Focus();
            var outside = Q("outside");
            outside.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(m1),
                "Precondition: the landing was reverted to the contained scope");

            // The reverted element's queued focus events can interleave with no terminating Blur
            // (observed against real editor input), stranding its focus-visible styling lit while it is
            // not focused. That interleave is not reproducible with synthetic dispatch, so the residue
            // is staged through the signal channel AFTER the revert resolved.
            using (var evt = FocusEvent.GetPooled()) outside.SimulateEvent(evt);
            Assume.That(outside.ClassListContains("bg-blue-400"), Is.True,
                "Precondition: the focus-visible payload is lit while the element is not focused");

            // Act — the panel's next tick runs the scheduled settle.
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(outside.ClassListContains("bg-blue-400"), Is.False);
        }

        [Component]
        private static VNode ReorderedAutoFocusHost()
        {
            var (rotated, setRotated) = Hooks.UseState(false);
            s_setRotated = setRotated;
            var scope = V.FocusScope(name: "scope", key: "scope", autoFocus: true, children: new VNode[]
            {
                V.Button(name: "first"),
            });
            var b1 = V.Button(name: "b1", key: "b1");
            var b2 = V.Button(name: "b2", key: "b2");
            return V.Div(children: rotated ? new VNode[] { b1, b2, scope } : new VNode[] { scope, b1, b2 });
        }

        [Test]
        public void Given_AnAutoFocusScopeInAKeyedList_When_AReorderReattachesTheScope_Then_FocusStaysWhereTheUserMovedIt()
        {
            // Arrange — AutoFocus fired once at mount; the user then moved focus outside the scope.
            Mount(ReorderedAutoFocusHost);
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("first")),
                "Precondition: AutoFocus landed on the scope's first focusable at mount");
            var b1 = Q("b1");
            b1.Focus();

            // Act — the keyed rotation physically moves the scope subtree (RemoveAt + Insert), firing
            // a fresh AttachToPanelEvent on it.
            s_setRotated.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert — autoFocus is mount-once: a re-attach must not steal focus back.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(b1));
        }

        [Component]
        private static VNode TwoGroupsHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "groupA", singleTabStop: true, children: new VNode[]
            {
                V.Button(name: "a1"),
                V.Button(name: "a2"),
            }),
            V.FocusScope(name: "groupB", singleTabStop: true, children: new VNode[]
            {
                V.Button(name: "b1"),
                V.Button(name: "b2"),
            }),
        });

        [Test]
        public void Given_TwoAdjacentSingleTabStopGroups_When_TabExitsTheFirstIntoTheSecond_Then_FocusLandsOnTheSecondGroupsRememberedMember()
        {
            // Arrange — groupB remembers b2 as its roving tab stop; focus then sits in groupA.
            Mount(TwoGroupsHost);
            Q("b2").Focus();
            var a1 = Q("a1");
            a1.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(a1),
                "Precondition: a member of the first group holds focus");

            // Act — the exit walk's raw landing crosses straight into groupB (its first member).
            SendMove(a1, NavigationMoveEvent.Direction.Next);

            // Assert — entry respects the roving contract even for a redirected landing: the
            // remembered member wins, and groupB's memory is not corrupted to the raw candidate.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("b2")));
        }

        [Test]
        public void Given_AFreshSingleTabStopGroup_When_ShiftTabEntersFromAfterIt_Then_FocusLandsOnTheGroupsFirstMember()
        {
            // Arrange
            Mount(SingleTabStopHost);
            var after = Q("after");
            after.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(after),
                "Precondition: focus sits after the group");

            // Act
            SendMove(after, NavigationMoveEvent.Direction.Previous);

            // Assert — the composite is one tab stop from either direction: with no remembered member
            // the entry is the group's FIRST member, never its ring-last.
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("g1")));
        }

        [Component]
        private static VNode GroupInModalHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
                V.FocusScope(name: "group", singleTabStop: true, children: new VNode[]
                {
                    V.Button(name: "g1"),
                    V.Button(name: "g2"),
                }),
            }),
            V.Button(name: "outside"),
        });

        [Test]
        public void Given_ASingleTabStopGroupInsideAContainedScope_When_TabExitsTheGroup_Then_FocusWrapsWithinTheContainedScope()
        {
            // Arrange
            Mount(GroupInModalHost);
            var g1 = Q("g1");
            g1.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(g1),
                "Precondition: a group member holds focus");

            // Act — the group's one-stop exit must honor the enclosing modal's containment: the exit
            // walk rides the modal's ring, wrapping to m1 instead of escaping to "outside".
            SendMove(g1, NavigationMoveEvent.Direction.Next);

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("m1")));
        }

        [Component]
        private static VNode DepartingRestoreTargetHost()
        {
            var (showOpener, setShowOpener) = Hooks.UseState(true);
            s_setShowOpener = setShowOpener;
            return V.Div(children: new VNode[]
            {
                showOpener ? V.Button(name: "opener", key: "opener") : null,
                V.FocusScope(name: "scope", key: "scope", restoreFocus: true, children: new VNode[]
                {
                    V.Button(name: "inner"),
                }),
            });
        }

        [Test]
        public void Given_AScopeWhoseRestoreTargetUnmounts_When_TheTargetsElementIsReleased_Then_TheScopeDropsItsRestoreReference()
        {
            // Arrange — focus enters the scope FROM the opener, capturing it as the restore target.
            Mount(DepartingRestoreTargetHost);
            Q("opener").Focus();
            Q("inner").Focus();
            var binding = _mounted.Root.Reconciler.Context.FocusScopeBindings[Q("scope")];
            Assume.That(binding.RestoreTarget, Is.Not.Null,
                "Precondition: the opener was captured as the restore target");

            // Act — the opener unmounts; its pooled element will be recycled into an unrelated role,
            // where the liveness checks at restore time (panel / canGrabFocus) pass again.
            s_setShowOpener.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(binding.RestoreTarget, Is.Null);
        }

        [Component]
        private static VNode PlainScopeHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "scope", contain: true, children: new VNode[]
            {
                V.Button(name: "s1"),
            }),
        });

        [Test]
        public void Given_AMountIntoADetachedRoot_When_TheRootAttachesToAPanel_Then_TheNavigatorListensOnTheTruePanelRootExactlyOnce()
        {
            // Arrange — mounting before the root has a panel: ring predictions computed from a
            // non-root subtree would diverge from the engine's own panel-wide ring.
            var detachedRoot = new VisualElement();
            _mounted = V.Mount(detachedRoot, V.Component(PlainScopeHost, key: "root"));

            // Act
            _host.Root.Add(detachedRoot);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert — one attachment, keyed by the panel's true root, never the mount target.
            Assert.That(_mounted.Root.Reconciler.Context.NavigatorAttachments.Keys,
                Is.EquivalentTo(new[] { _host.Panel.visualTree }));
        }

        [Component]
        private static VNode StackedModalsHost()
        {
            var (showSecond, setShowSecond) = Hooks.UseState(false);
            s_setShowScope = setShowSecond;
            return V.Div(children: new VNode[]
            {
                V.FocusScope(name: "modalA", key: "modalA", contain: true, children: new VNode[]
                {
                    V.Button(name: "a1"),
                }),
                showSecond
                    ? V.FocusScope(name: "modalB", key: "modalB", contain: true, autoFocus: true, children: new VNode[]
                    {
                        V.Button(name: "b1"),
                    })
                    : null,
            });
        }

        [Test]
        public void Given_AContainedScopeHoldingFocus_When_ANewContainedScopeMountsWithAutoFocus_Then_TheNewScopeTakesFocus()
        {
            // Arrange
            Mount(StackedModalsHost);
            Q("a1").Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("a1")),
                "Precondition: the first modal holds focus");

            // Act — a stacked dialog: modal B mounts on top and auto-focuses. A landing inside a
            // contained scope claims focus (the scope receiving focus wins — React Aria's newest-scope
            // activation); the old-scope snap-back must not yank it back underneath the overlay.
            s_setShowScope.Invoke(true);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("b1")));
        }

        [Component]
        private static VNode TwoContainedScopesHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modalA", contain: true, children: new VNode[]
            {
                V.Button(name: "a1"),
            }),
            V.FocusScope(name: "modalB", contain: true, children: new VNode[]
            {
                V.Button(name: "x1"),
            }),
        });

        [Test]
        public void Given_TwoContainedScopes_When_APointerStyleMoveCrossesBetweenThem_Then_TheReceivingScopeKeepsFocus()
        {
            // Arrange — pinned as a terminal-recursion guard: a landing inside a contain scope stands
            // (the receiving scope claims focus), which is what makes cross-scope moves converge. UI
            // Toolkit QUEUES focus events raised from inside a dispatch, so any design that snaps this
            // landing back degenerates into two scopes queueing Focus calls at each other forever — a
            // main-thread livelock, which is why the pre-fix behavior could not be pinned as a failing
            // assertion.
            Mount(TwoContainedScopesHost);
            var a1 = Q("a1");
            a1.Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(a1),
                "Precondition: the first contained scope holds focus");

            // Act
            var x1 = Q("x1");
            x1.Focus();

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(x1));
        }

        [Component]
        private static VNode RestoringModalHost()
        {
            var (showScope, setShowScope) = Hooks.UseState(true);
            s_setShowOpener = setShowScope;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "opener"),
                showScope
                    ? V.FocusScope(name: "modal", key: "modal", contain: true, restoreFocus: true, children: new VNode[]
                    {
                        V.Button(name: "inner"),
                    })
                    : null,
            });
        }

        [Test]
        public void Given_AContainedRestoringScope_When_ItUnmountsWhileHoldingFocus_Then_FocusReturnsToTheOpener()
        {
            // Arrange — the standard modal combo: contain + restoreFocus together.
            Mount(RestoringModalHost);
            Q("opener").Focus();
            Q("inner").Focus();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("inner")),
                "Precondition: focus sits inside the modal");

            // Act — the restore's own FocusIn must not see the dying scope as a live contain scope
            // (the snap-back would revert the restore straight back into the detaching subtree).
            s_setShowOpener.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(Q("opener")));
        }

        [Component]
        private static VNode LatchBypassHost()
        {
            var (autoFocusOn, setAutoFocusOn) = Hooks.UseState(false);
            s_setAutoFocusOn = setAutoFocusOn;
            var (rotated, setRotated) = Hooks.UseState(false);
            s_setRotated = setRotated;
            var scope = V.FocusScope(name: "scope", key: "scope", autoFocus: autoFocusOn, children: new VNode[]
            {
                V.Button(name: "first"),
            });
            var b1 = V.Button(name: "b1", key: "b1");
            var b2 = V.Button(name: "b2", key: "b2");
            return V.Div(children: rotated ? new VNode[] { b1, b2, scope } : new VNode[] { scope, b1, b2 });
        }

        [Test]
        public void Given_AScopeWhoseAutoFocusTurnedOnAfterMount_When_AReorderReattachesIt_Then_FocusIsStillNotStolen()
        {
            // Arrange — autoFocus is mount-once like React's: a post-mount settings flip must not
            // re-arm it for the next physical re-attach.
            Mount(LatchBypassHost);
            var b1 = Q("b1");
            b1.Focus();
            s_setAutoFocusOn.Invoke(true);
            _mounted.FlushStateForTest();
            Assume.That(_host.Panel.focusController.focusedElement, Is.EqualTo(b1),
                "Precondition: the settings flip alone must not move focus");

            // Act — the keyed rotation physically re-attaches the scope.
            s_setRotated.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(_host.Panel.focusController.focusedElement, Is.EqualTo(b1));
        }

        [Test]
        public void Given_APooledPropsBagThatCarriedFocusProps_When_ReturnedAndRentedAgain_Then_TheFocusPropsAreScrubbed()
        {
            // Arrange
            var props = VNodePool.RentProps();
            props.TabIndex = 3;
            props.DelegatesFocus = true;
            props.FocusScope = new FocusScopeSettings(Contain: true);

            // Act
            VNodePool.ReturnProps(props);
            var reused = VNodePool.RentProps();

            // Assert — a recycled bag must not ghost focus behavior onto an unrelated element.
            Assert.That((reused.TabIndex, reused.DelegatesFocus, reused.FocusScope),
                Is.EqualTo(((int?)null, (bool?)null, (FocusScopeSettings)null)));
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
