using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The probe an em or percentage <c>leading-[…]</c> arms on its declaring element: one per element for
    /// as long as the class is there, gone with the class, the element or the reconciler, and a fallback
    /// that reads the size from layout on a panel without the bundled sheet; and the poll a viewport unit in
    /// the leading keeps. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class LeadingLengthProbeLifecycleTests
    {
        private static VNode Owner(string cls) => V.Div(className: cls, V.Label(text: "hi"));

        // Whether the probe's viewport poll is set to run, or null where it holds none or none can be read.
        private static bool? IsPolling(LeadingLengthProbe probe)
            => (typeof(LeadingLengthProbe).GetField("_tick", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(probe)
                as IVisualElementScheduledItem)?.isActive;

        [Test]
        public void Given_AnEmLeading_When_Mounted_Then_TheElementCarriesTheMarkerClass()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { Owner("leading-[1.5em]") });

            // Assert
            Assert.That(scope.Root[0].ClassListContains(LeadingLengthProbe.MarkerClass), Is.True);
        }

        [Test]
        public void Given_AnEmLeadingThatStays_When_Patched_Then_TheSameProbeIsKept()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new[] { Owner("leading-[150%]") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var owner = scope.Root[0];
            var before = scope.Reconciler.Context.LeadingLengthProbes[owner];

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new[] { Owner("p-2 leading-[150%]") });

            // Assert
            Assert.That(scope.Reconciler.Context.LeadingLengthProbes.TryGetValue(owner, out var after) && after == before,
                Is.True);
        }

        [Test]
        public void Given_AnEmLeadingPatchedAway_When_Reconciled_Then_NeitherTheProbeNorItsMarkerRemains()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new[] { Owner("leading-[150%]") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var owner = scope.Root[0];

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, new[] { Owner("leading-[1.5]") });

            // Assert
            Assert.That(
                (scope.Reconciler.Context.LeadingLengthProbes.Count, owner.ClassListContains(LeadingLengthProbe.MarkerClass)),
                Is.EqualTo((0, false)));
        }

        [Test]
        public void Given_AnEmLeadingElement_When_Removed_Then_NoProbeRemains()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new[] { Owner("leading-[150%]") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.LeadingLengthProbes.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AReconcilerHoldingAProbe_When_Disposed_Then_NoProbeRemains()
        {
            // Arrange
            var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { Owner("leading-[150%]") });
            var context = scope.Reconciler.Context;

            // Act
            scope.Dispose();

            // Assert
            Assert.That(context.LeadingLengthProbes.Count, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base polls a leading that reads the viewport.
        [Test]
        public void Given_AViewportLeading_When_Mounted_Then_ItsProbePolls()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { Owner("leading-[10vw]") });

            // Assert
            Assert.That(IsPolling(scope.Reconciler.Context.LeadingLengthProbes[scope.Root[0]]), Is.EqualTo((bool?)true));
        }

        // GREEN_ON_BASE(characterization): the base polls a viewport leading declared again after an em one stopped it.
        [Test]
        public void Given_AViewportLeadingSwappedForAnEmOne_When_TheViewportLeadingIsDeclaredAgain_Then_ItsProbePollsAgain()
        {
            // Arrange — the em leading reads no viewport, so the probe stops polling.
            using var scope = new ReconcilerScope();
            var viewport = new[] { Owner("leading-[10vw]") };
            var em = new[] { Owner("leading-[1.5em]") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), viewport);
            scope.Reconciler.Reconcile(scope.Root, viewport, em);

            // Act
            scope.Reconciler.Reconcile(scope.Root, em, new[] { Owner("leading-[10vw]") });

            // Assert
            Assert.That(IsPolling(scope.Reconciler.Context.LeadingLengthProbes[scope.Root[0]]), Is.EqualTo((bool?)true));
        }

        // GREEN_ON_BASE(characterization): the base takes off the element both callbacks its probe registered.
        [Test]
        public void Given_AnElementWhoseEmLeadingIsDropped_When_ItsProbeDetaches_Then_NoCallbackTheProbeRegisteredRemains()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var element = new VisualElement();
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(element);
            LeadingLengthProbe.Sync(scope.Reconciler.Context, element, new LeadingValue(LeadingUnit.EmLength, 1.5f));
            var probed = CallbackRegistryProbe.BubbleUpCallbackCount(element);

            // Act
            LeadingLengthProbe.Sync(scope.Reconciler.Context, element, null);

            // Assert — -1 where the probe registered nothing, which no count is.
            Assert.That(probed > before ? CallbackRegistryProbe.BubbleUpCallbackCount(element) : -1, Is.EqualTo(before));
        }
    }
}
