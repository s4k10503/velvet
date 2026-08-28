using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a user callback the create and patch paths invoke does to the reconcile that
    /// invoked it when it throws. Each is application code the reconciler calls on behalf of a component
    /// that is still live, so each reaches the nearest error boundary and, with none registered, the
    /// console — one log line, unlike the two a teardown-path failure leaves.
    /// <list type="bullet">
    /// <item>A ref setup that throws does not stop the setup queued behind it, and a later patch carrying
    /// the same callback does not try it again — the entry records the identity whether or not the setup
    /// completed.</item>
    /// <item>A ref whose cleanup throws when its identity changes still has its replacement attached —
    /// the two halves are independent, as they are in React.</item>
    /// <item>An <c>onCreated</c> that throws leaves the element it was handed occupying its slot.</item>
    /// <item>A <c>wrapElement</c> that throws leaves the element itself in the slot, so the ref points at
    /// something the tree holds rather than at an orphan.</item>
    /// <item>The cleanup a setup returned for an element that left while the setup ran is fired by the drain
    /// itself, and a throw out of that firing is contained on the same terms as the setup's own: it reaches
    /// the boundary, and the abort the boundary raised is consumed before the next setup runs.</item>
    /// </list>
    /// Each reads whether the failure left the reconcile call beside the state it is named for, because a
    /// tree where it escapes never reaches that state and a bare rethrow says nothing about which of the
    /// two moved.
    /// <para>
    /// A case naming no boundary reads the console fall-through. The ones that register one read the
    /// arrangement under which a caught failure calls SetAborted — and a ref setup runs at a point where
    /// nothing downstream consumes that flag unless the drain does. Four of those read what the abort
    /// reaches from there: the boundary's own committed tree when the pass being drained is its own, a
    /// second boundary's fallback and a later setup's synchronous state write when it is not, and an
    /// enclosing fiber's committed tree when the boundary that caught is below it.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class ElementCallbackFailureTests : ReconcilerTestFixture
    {
        private const string FailureMessage = "arranged failure out of an element callback";

        private static bool s_fallbackShown;
        private static StateUpdater<string> s_setProbe;
        private static StateUpdater<int> s_setAttempt;
        private static ComponentFiber s_boundaryFiber;
        private static ComponentFiber s_hostFiber;

        public override void SetUp()
        {
            base.SetUp();
            s_fallbackShown = false;
            s_setProbe = default;
            s_setAttempt = default;
            s_boundaryFiber = null;
            s_hostFiber = null;
            s_orphanedCleanups = 0;
        }

        [Test]
        public void Given_ARefSetupThatThrows_When_TheQueuedSetupsRun_Then_TheOneBehindItStillAttaches()
        {
            // Arrange
            VisualElement second = null;
            var tree = new VNode[]
            {
                V.Div(name: "first", refCallback: _ => throw new InvalidOperationException(FailureMessage)),
                V.Div(name: "second", refCallback: element => { second = element; return null; }),
            };
            ExpectContainedFailure();

            // Act
            var escaped = EscapesFrom(() => Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree));

            // Assert
            Assert.That((escaped, second != null), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ARefSetupThatThrows_When_ALaterPatchCarriesTheSameCallback_Then_ItIsNotAttemptedAgain()
        {
            // Arrange — one delegate instance stands in both trees, so the identity gate is what decides
            // whether the failed setup is tried again.
            var attempts = 0;
            Func<VisualElement, Action> failing = _ =>
            {
                attempts++;
                throw new InvalidOperationException(FailureMessage);
            };
            var mounted = new VNode[] { V.Div(name: "host", refCallback: failing) };
            ExpectContainedFailure();
            // Read rather than left to throw: a tree where the mount's own setup escapes never reaches
            // the patch, so the count below would carry no reading there.
            var escapedOnMount = EscapesFrom(() => Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted));

            // Act
            var patched = new VNode[] { V.Div(name: "host", className: "p-1", refCallback: failing) };
            var escaped = EscapesFrom(() => Reconciler.Reconcile(Root, mounted, patched));

            // Assert
            Assert.That((escapedOnMount, escaped, attempts), Is.EqualTo((false, false, 1)));
        }

        [Test]
        public void Given_ARefCleanupThatThrows_When_TheRefIdentityChanges_Then_TheReplacementStillAttaches()
        {
            // Arrange
            VisualElement replacement = null;
            var mounted = new VNode[]
            {
                V.Div(name: "host", refCallback: _ => () => throw new InvalidOperationException(FailureMessage)),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            ExpectContainedFailure();

            // Act — a per-render lambda is a fresh identity, so the patch cycles the installed ref.
            var patched = new VNode[]
            {
                V.Div(name: "host", refCallback: element => { replacement = element; return null; }),
            };
            var escaped = EscapesFrom(() => Reconciler.Reconcile(Root, mounted, patched));

            // Assert
            Assert.That((escaped, replacement != null), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnOnCreatedThatThrows_When_TheElementIsCreated_Then_ItStillTakesItsSlot()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.ScrollView(name: "host", onCreated: _ => throw new InvalidOperationException(FailureMessage)),
            };
            ExpectContainedFailure();

            // Act
            var escaped = EscapesFrom(() => Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree));

            // Assert
            Assert.That((escaped, NamesOf(Root)), Is.EqualTo((false, "host")));
        }

        [Test]
        public void Given_AWrapElementThatThrows_When_TheElementIsCreated_Then_TheRefPointsAtWhatTheTreeHolds()
        {
            // Arrange
            VisualElement captured = null;
            var tree = new VNode[]
            {
                V.Button(name: "host",
                    wrapElement: _ => throw new InvalidOperationException(FailureMessage),
                    refCallback: element => { captured = element; return null; }),
            };
            ExpectContainedFailure();

            // Act
            var escaped = EscapesFrom(() => Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree));

            // Assert
            Assert.That((escaped, Root!.childCount == 1 && ReferenceEquals(captured, Root.ElementAt(0))),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ARefSetupThatThrows_When_ABoundaryIsRegisteredAboveIt_Then_ItCatchesInsteadOfTheConsole()
        {
            // Arrange — no LogAssert.Expect: a boundary consuming the exception is the difference this
            // reads, and an unexpected console exception fails the case on its own.

            // Act
            using var mounted = V.Mount(Root, V.Component(RefFailureHost, key: "host"));

            // Assert
            Assert.That((s_fallbackShown, Root!.Q<Label>("fallback") != null), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ABoundaryCaughtARefSetupFailure_When_AnUnrelatedFiberRendersNext_Then_ItsChangeStillCommits()
        {
            // Arrange — the probe Label is outside the boundary, so nothing about it was replaced by the
            // fallback and its next render is an ordinary pass on the same shared context.
            using var mounted = V.Mount(Root, V.Component(RefFailureHost, key: "host"));

            // Act
            s_setProbe.Invoke("after");
            mounted.FlushStateForTest();

            // Assert — the query is null-tolerant so a tree missing the probe altogether disagrees here
            // rather than raising, which is a reading and not a crash.
            Assert.That((s_fallbackShown, Root!.Q<Label>("probe")?.text), Is.EqualTo((true, "after")));
        }

        [Test]
        public void Given_ABoundaryOwnsThePassBeingDrained_When_ItsFallbackReplacesItsTree_Then_ThePrimaryIsNotCommittedOverIt()
        {
            // Arrange — the mount's setup succeeds, so the throwing attempt runs in the pass the boundary's
            // own state write opens: that pass belongs to the boundary fiber, so the drain ending it is the
            // boundary's own rather than an ancestor's.
            using var mounted = V.Mount(Root, V.Component(SelfDrivenFailingBoundary, key: "boundary"));

            // Act
            s_setAttempt.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the fallback flag gates the name, because a pass where the boundary never caught
            // reads the same absent element name as one that caught and then overwrote its own baseline.
            Assert.That(
                (s_fallbackShown, (s_boundaryFiber?.PreviousTree?[0] as BaseElementNode)?.Name),
                Is.EqualTo((true, "fallback")));
        }

        // Measured, reporting the drain's abort to the pass boundary instead of to the fiber whose
        // fallback it is reddens this case, and no other case in this fixture or RefAttachOrderingTests.
        [Test]
        public void Given_ADescendantBoundaryCaughtInTheDrain_When_ThePassEnds_Then_TheDrainingFiberStillCommitsItsTree()
        {
            // Arrange — the boundary is driven by a prop from the host above it, so the pass whose drain
            // fails belongs to the host while the fallback belongs to the boundary.
            using var mounted = V.Mount(Root, V.Component(DescendantBoundaryHost, key: "host"));

            // Act
            s_setAttempt.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the DOM name is read beside the baseline's, because either one alone says nothing
            // about whether the two still describe the same element.
            Assert.That(
                (s_fallbackShown, Root!.Q("host1") != null,
                    (s_hostFiber?.PreviousTree?[0] as BaseElementNode)?.Name),
                Is.EqualTo((true, true, "host1")));
        }

        [Test]
        public void Given_TwoBoundariesOverAThrowingSetupEach_When_TheDrainReachesTheSecond_Then_ItsFallbackReachesTheDom()
        {
            // Arrange / Act — one pass, so both setups are queued together and the first boundary catches
            // while the second's element is still ahead in the same loop.
            using var mounted = V.Mount(Root, V.Component(TwoBoundaryHost, key: "host"));

            // Assert — the first is read beside the second, because a pass where neither caught reads the
            // same absent second fallback as one where the first's abort stopped it.
            Assert.That(
                (Root!.Q<Label>("first-fallback") != null, Root.Q<Label>("second-fallback") != null),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ARefSetupThrewIntoABoundary_When_ALaterSetupCommitsAStateWrite_Then_ItStillReachesTheDom()
        {
            // Arrange / Act — the committing setup is queued behind the throwing one. A discrete event
            // dispatched from the pass boundary is not held back, which is what lets its handler's write
            // render before this call returns.
            using var mounted = V.Mount(Root, V.Component(AbortThenCommitHost, key: "host"));

            // Assert — the fallback is read beside the probe, because a pass where the first setup never
            // threw reads the same committed probe as one where it threw and the abort was consumed.
            Assert.That(
                (Root!.Q<Label>("first-fallback") != null, Root.Q<Label>("probe")?.text),
                Is.EqualTo((true, "after")));
        }

        #region Boundary components whose ref setup throws while the drain is running

        // The setup is attributed to the component that rendered the element and the boundary search starts
        // above that component, so every arrangement here puts the throwing element one component below the
        // boundary meant to catch it. The closure over attempt is what keeps the ref identity changing
        // across renders, which is what re-queues the setup.
        [Component]
        private static VNode AttemptFailingRefChild(int attempt)
            => V.Div(name: "failing", refCallback: _ =>
            {
                if (attempt > 0) throw new InvalidOperationException(FailureMessage);
                return null;
            });

        [Component(IsErrorBoundary = true)]
        private static VNode SelfDrivenFailingBoundary()
        {
            s_boundaryFiber = FiberAmbientStack.Current;
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(name: "fallback", text: "caught");
            });
            var (attempt, setAttempt) = Hooks.UseState(0);
            s_setAttempt = setAttempt;
            return V.Component(AttemptFailingRefChild, attempt, key: "child");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode PropDrivenFailingBoundary(int attempt)
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(name: "fallback", text: "caught");
            });
            return V.Component(AttemptFailingRefChild, attempt, key: "child");
        }

        // The host's own name carries the state, so its committed baseline and the element in the DOM
        // disagree by name whenever the baseline is the pre-flush one.
        [Component]
        private static VNode DescendantBoundaryHost()
        {
            s_hostFiber = FiberAmbientStack.Current;
            var (attempt, setAttempt) = Hooks.UseState(0);
            s_setAttempt = setAttempt;
            return V.Div(name: "host" + attempt, children: new VNode[]
            {
                V.Component(PropDrivenFailingBoundary, attempt, key: "boundary"),
            });
        }

        [Component]
        private static VNode NamedFailingRefChild(string which)
            => V.Div(name: which + "-failing",
                refCallback: _ => throw new InvalidOperationException(FailureMessage));

        [Component(IsErrorBoundary = true)]
        private static VNode FirstFailingBoundary()
        {
            Hooks.UseFallback(_ => V.Label(name: "first-fallback", text: "caught"));
            return V.Component(NamedFailingRefChild, "first", key: "child");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode SecondFailingBoundary()
        {
            Hooks.UseFallback(_ => V.Label(name: "second-fallback", text: "caught"));
            return V.Component(NamedFailingRefChild, "second", key: "child");
        }

        // Each boundary gets a container of its own — the same slot-0 reason the RefFailureHost region
        // below gives for putting its probe after the boundary rather than before it.
        [Component]
        private static VNode TwoBoundaryHost()
            => V.Div(children: new VNode[]
            {
                V.Div(name: "left", children: new VNode[]
                {
                    V.Component(FirstFailingBoundary, key: "first"),
                }),
                V.Div(name: "right", children: new VNode[]
                {
                    V.Component(SecondFailingBoundary, key: "second"),
                }),
            });

        [Component]
        private static VNode AbortThenCommitHost()
        {
            var (probe, setProbe) = Hooks.UseState("before");
            s_setProbe = setProbe;
            return V.Div(children: new VNode[]
            {
                V.Div(name: "left", children: new VNode[]
                {
                    V.Component(FirstFailingBoundary, key: "first"),
                }),
                V.Button(name: "trigger", onClick: () => setProbe.Invoke("after")),
                V.Div(name: "committer", refCallback: ClickTheTrigger),
                V.Label(name: "probe", text: probe),
            });
        }

        private static Action ClickTheTrigger(VisualElement element)
        {
            element.parent?.Q<Button>("trigger")?.SimulateClick();
            return null;
        }

        #endregion

        #region RefFailureHost component (a boundary over a child whose ref setup throws, beside a probe)

        [Component]
        private static VNode RefFailureHost()
        {
            var (probe, setProbe) = Hooks.UseState("before");
            s_setProbe = setProbe;
            // The probe follows the boundary rather than preceding it: a fallback swap reconciles from
            // the boundary's mount point at slot 0, so a sibling ahead of the boundary sits inside the
            // range that swap rewrites and would be replaced by the fallback instead.
            return V.Div(children: new VNode[]
            {
                V.Component(RefFailureBoundary, key: "boundary"),
                V.Label(name: "probe", text: probe),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode RefFailureBoundary()
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(name: "fallback", text: "caught");
            });
            return V.Component(FailingRefChild, key: "child");
        }

        // The setup is attributed to the component that rendered the element, and the boundary search
        // starts above that component — so the failing ref belongs to a child of the boundary rather than
        // to the boundary itself.
        [Component]
        private static VNode FailingRefChild()
            => V.Div(name: "failing",
                refCallback: _ => throw new InvalidOperationException(FailureMessage));

        #endregion

        // One line, not the two a FiberLogger.LogException site leaves: with no error boundary registered
        // the boundary search reports through Debug.LogException.
        private static void ExpectContainedFailure()
            => LogAssert.Expect(LogType.Exception, $"InvalidOperationException: {FailureMessage}");

        // Filtered on the arranged message so any other InvalidOperationException still leaves the case.
        private static bool EscapesFrom(Action reconcile)
        {
            try
            {
                reconcile();
                return false;
            }
            catch (InvalidOperationException exception) when (exception.Message == FailureMessage)
            {
                return true;
            }
        }


        #region An orphaned setup's cleanup that throws (a boundary above it, and a setup queued behind it)

        private static int s_orphanedCleanups;

        // One instance across renders, so a patch inside the drain reads the ref as unchanged rather than
        // re-queueing this setup.
        private static readonly Func<VisualElement, Action> s_selfRemovingSetupWithAThrowingCleanup =
            SelfRemovingSetupWithAThrowingCleanup;

        // The setup dispatches a discrete event whose handler stops rendering the element it was handed, so
        // the entry the drain recorded for that element is gone by the time the setup returns — which is
        // what sends the cleanup it returned down the drain's own orphan arm.
        private static Action SelfRemovingSetupWithAThrowingCleanup(VisualElement element)
        {
            element.parent?.Q<Button>("victim-trigger")?.SimulateClick();
            return () =>
            {
                s_orphanedCleanups++;
                throw new InvalidOperationException(FailureMessage);
            };
        }

        [Component]
        private static VNode SelfRemovingVictimHost()
        {
            var (show, setShow) = Hooks.UseState(true);
            return V.Div(children: show
                ? new VNode[]
                {
                    V.Button(name: "victim-trigger", onClick: () => setShow.Invoke(false)),
                    V.Div(name: "victim", refCallback: s_selfRemovingSetupWithAThrowingCleanup),
                }
                : new VNode[]
                {
                    V.Button(name: "victim-trigger", onClick: () => setShow.Invoke(false)),
                });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode OrphanedCleanupBoundary()
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(name: "orphan-fallback", text: "caught");
            });
            return V.Component(SelfRemovingVictimHost, key: "victim-host");
        }

        [Component]
        private static VNode OrphanedCleanupHost()
            => V.Div(children: new VNode[] { V.Component(OrphanedCleanupBoundary, key: "boundary") });

        // The committer's setup is queued behind the victim's, so it runs in the same drain the boundary
        // caught in — the same slot-0 reason the RefFailureHost region gives for the container around the
        // boundary.
        [Component]
        private static VNode OrphanedCleanupThenCommitHost()
        {
            var (probe, setProbe) = Hooks.UseState("before");
            s_setProbe = setProbe;
            return V.Div(children: new VNode[]
            {
                V.Div(name: "left", children: new VNode[]
                {
                    V.Component(OrphanedCleanupBoundary, key: "boundary"),
                }),
                V.Button(name: "trigger", onClick: () => setProbe.Invoke("after")),
                V.Div(name: "committer", refCallback: ClickTheTrigger),
                V.Label(name: "probe", text: probe),
            });
        }

        [Test]
        public void Given_AnOrphanedSetupsCleanupThatThrows_When_ABoundaryIsRegisteredAboveIt_Then_ItCatchesInsteadOfSwallowing()
        {
            // Arrange / Act
            using var mounted = V.Mount(Root, V.Component(OrphanedCleanupHost, key: "host"));

            // Assert — the cleanup's own count gates the fallback, because a tree that never reached the
            // orphan arm reads the same absent fallback as one that reached it and swallowed the throw.
            Assert.That((s_orphanedCleanups, Root!.Q<Label>("orphan-fallback") != null),
                Is.EqualTo((1, true)));
        }

        [Test]
        public void Given_AnOrphanedCleanupThrewIntoABoundary_When_ALaterSetupCommitsAStateWrite_Then_ItStillReachesTheDom()
        {
            // Arrange / Act
            using var mounted = V.Mount(Root, V.Component(OrphanedCleanupThenCommitHost, key: "host"));

            // Assert — the fallback is read beside the probe, because a pass where the cleanup never threw
            // reads the same committed probe as one where it threw and the abort was consumed.
            Assert.That((Root!.Q<Label>("orphan-fallback") != null, Root.Q<Label>("probe")?.text),
                Is.EqualTo((true, "after")));
        }

        #endregion

        private static string NamesOf(VisualElement parent)
        {
            var names = new List<string>();
            for (var i = 0; i < parent.childCount; i++) names.Add(parent.ElementAt(i).name);
            return string.Join(",", names);
        }
    }
}
