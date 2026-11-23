using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Two small decisions of the reconcile pass, reached directly: whether a fiber still owes a Portal its
    /// retarget, and whether a mount point sits inside a dropped container.
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

        // GREEN_ON_BASE(characterization): the base reports no retarget owed by a fiber whose stored portals are all mounted.
        // Change the closing `return false;` of OwesPortalRetarget to `return true;` and it owes one.
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

        // GREEN_ON_BASE(characterization): the base clears the retarget mark of a fiber whose stored portals are all mounted.
        // Drop `fiber.PortalRetargetOwed = false;` from OwesPortalRetarget and the mark stays.
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

        // GREEN_ON_BASE(characterization): the base finds a mount point inside a dropped container by walking up its parents.
        // Change `ve = ChildListWrites.ParentOf(ve)` to `ve = null` in IsInsideOrphan and only the mount point itself
        // is tested.
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
    }
}
