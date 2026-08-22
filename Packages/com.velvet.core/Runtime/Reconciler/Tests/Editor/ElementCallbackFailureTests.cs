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
    /// </summary>
    [TestFixture]
    internal sealed class ElementCallbackFailureTests : ReconcilerTestFixture
    {
        private const string FailureMessage = "arranged failure out of an element callback";

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
