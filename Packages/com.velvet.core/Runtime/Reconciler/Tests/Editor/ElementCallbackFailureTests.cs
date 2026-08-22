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
    /// </list>
    /// Each reads whether the failure left the reconcile call beside the state it is named for, because a
    /// tree where it escapes never reaches that state and a bare rethrow says nothing about which of the
    /// two moved.
    /// <para>
    /// A case naming no boundary reads the console fall-through. The two that register one read the
    /// arrangement under which a caught failure calls SetAborted — and a ref setup runs at a point where
    /// nothing downstream consumes that flag unless the drain does.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class ElementCallbackFailureTests : ReconcilerTestFixture
    {
        private const string FailureMessage = "arranged failure out of an element callback";

        private static bool s_fallbackShown;
        private static StateUpdater<string> s_setProbe;

        public override void SetUp()
        {
            base.SetUp();
            s_fallbackShown = false;
            s_setProbe = default;
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

            // Assert
            Assert.That((s_fallbackShown, Root!.Q<Label>("probe").text), Is.EqualTo((true, "after")));
        }

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

        private static string NamesOf(VisualElement parent)
        {
            var names = new List<string>();
            for (var i = 0; i < parent.childCount; i++) names.Add(parent.ElementAt(i).name);
            return string.Join(",", names);
        }
    }
}
