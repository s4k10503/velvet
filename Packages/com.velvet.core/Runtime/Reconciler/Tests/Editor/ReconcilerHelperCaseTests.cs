using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Three small decisions of the reconcile pass, reached directly: whether a fiber still owes a Portal its
    /// retarget, whether a mount point sits inside a dropped container, and whether realigning a layer host reads
    /// its live child list after the recorded writes have applied.
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerHelperCaseTests
    {
        private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

        private static bool OwesPortalRetarget(MountedTree mounted)
        {
            var reconciler = mounted.Root.Reconciler;
            var childPass = typeof(Reconciler).GetProperty("ChildPass", NonPublic)!.GetValue(reconciler)!;
            return (bool)childPass.GetType().GetMethod("OwesPortalRetarget", NonPublic)!
                .Invoke(childPass, new object?[] { mounted.Root })!;
        }

        [Test]
        public void Given_AFiberMarkedAsOwingARetargetWhoseStoredPortalsAreAllMounted_When_ItIsAsked_Then_ItOwesNothing()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Div());
            mounted.Root.PortalRetargetOwed = true;

            // Act
            var owes = OwesPortalRetarget(mounted);

            // Assert
            Assert.That(owes, Is.False);
        }

        [Test]
        public void Given_AFiberMarkedAsOwingARetargetWhoseStoredPortalsAreAllMounted_When_ItIsAsked_Then_TheMarkIsCleared()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Div());
            mounted.Root.PortalRetargetOwed = true;

            // Act
            OwesPortalRetarget(mounted);

            // Assert
            Assert.That(mounted.Root.PortalRetargetOwed, Is.False);
        }

        [Test]
        public void Given_AMountPointInsideADroppedContainer_When_TheContainersAreSearched_Then_ItIsInsideOne()
        {
            // Arrange
            var orphan = new VisualElement();
            var mountPoint = new VisualElement();
            orphan.Add(mountPoint);
            var method = typeof(GeneralPathReconciler).GetMethod("IsInsideOrphan", BindingFlags.NonPublic | BindingFlags.Static)!;

            // Act
            var inside = (bool)method.Invoke(null, new object[] { mountPoint, new HashSet<VisualElement> { orphan } })!;

            // Assert
            Assert.That(inside, Is.True);
        }

        [Test]
        public void Given_ALayerHostWithARecordedInsertNotYetApplied_When_ItsChildrenAreNotedReconciled_Then_TheInsertHasReachedTheLiveList()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var host = new VisualElement();
            reconciler.Context.ZLayerHosts[host] = new ZLayerHostRecord { Swapped = true };
            ChildListWrites.ViaQueue = 0;
            ChildListWrites.EndRecording();
            ChildListWrites.BeginRecording();
            try
            {
                ChildListWrites.ViaQueue++;
                try { ChildListWrites.Insert(host, 0, new VisualElement()); }
                finally { ChildListWrites.ViaQueue--; }

                // Act
                FiberZLayerCoordinator.NoteChildrenReconciled(reconciler.Context, host);

                // Assert
                Assert.That(host.hierarchy.childCount, Is.EqualTo(1));
            }
            finally
            {
                ChildListWrites.EndRecording();
            }
        }
    }
}
