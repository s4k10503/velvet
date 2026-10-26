using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the Suspense and AnimatePresence records kept against elements are pruned from an index of
    /// the elements they name, so tearing down an element no record names reads no table, and that no record is
    /// left behind by the index.
    /// <list type="bullet">
    /// <item>An element a record is written against is in the index, and a prune takes it out.</item>
    /// <item>A prune finds a record through the index by whichever element of the record the prune is for.</item>
    /// <item>A record put back after a failed render is found by a later prune.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ContainerKeyedStatePruneIndexTests
    {
        private static readonly ReconcilerContext.SuspenseFallbackRecord s_record = new(null!, false);

        // Whether the field exists is part of the reading, so a context that keeps no such index answers wrongly
        // rather than reading as one whose index names nothing.
        private static (bool Indexed, bool Names) IndexNames(ReconcilerContext ctx, string field, VisualElement element)
        {
            var index = typeof(ReconcilerContext).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ctx)
                as HashSet<VisualElement>;
            return (index != null, index != null && index.Contains(element));
        }

        // GREEN_ON_BASE(characterization): the base finds the record by scanning its tables, which the index must keep finding.
        [Test]
        public void Given_AFallbackShownInAContainer_When_TheContainerIsPruned_Then_TheFallbackIsNoLongerShown()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var boundary = new ComponentFiber();
            var container = new VisualElement();
            ctx.SetSuspenseFallbackShown(boundary, container, null, 1, s_record);

            // Act
            ctx.PruneSuspenseContainerState(container);

            // Assert
            Assert.That(ctx.IsSuspenseFallbackShown(boundary, container, null, 1), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base finds the record by scanning its tables, which the index must keep finding.
        [Test]
        public void Given_ARootlessFallbackShownInAContainer_When_TheContainerIsPruned_Then_TheFallbackIsNoLongerShown()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var container = new VisualElement();
            ctx.SetSuspenseFallbackShown(null, container, null, 1, s_record);

            // Act
            ctx.PruneSuspenseContainerState(container);

            // Assert
            Assert.That(ctx.IsSuspenseFallbackShown(null, container, null, 1), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base finds the record by scanning its tables, which the index must keep finding.
        [Test]
        public void Given_AFallbackShownUnderAPortalScope_When_TheScopeIsPruned_Then_TheFallbackIsNoLongerShown()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var boundary = new ComponentFiber();
            var container = new VisualElement();
            var scope = new VisualElement();
            ctx.SetSuspenseFallbackShown(boundary, container, scope, 1, s_record);

            // Act
            ctx.PruneSuspenseContainerState(scope);

            // Assert
            Assert.That(ctx.IsSuspenseFallbackShown(boundary, container, scope, 1), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base finds the record by scanning its tables, which the index must keep finding.
        [Test]
        public void Given_AFallbackShownInAContainer_When_AnotherElementIsPruned_Then_TheFallbackIsStillShown()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var boundary = new ComponentFiber();
            var container = new VisualElement();
            ctx.SetSuspenseFallbackShown(boundary, container, null, 1, s_record);

            // Act
            ctx.PruneSuspenseContainerState(new VisualElement());

            // Assert
            Assert.That(ctx.IsSuspenseFallbackShown(boundary, container, null, 1), Is.True);
        }

        [Test]
        public void Given_AFallbackShownInAContainer_When_TheContainerIsPruned_Then_TheIndexNoLongerNamesIt()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var container = new VisualElement();
            ctx.SetSuspenseFallbackShown(new ComponentFiber(), container, null, 1, s_record);

            // Act
            ctx.PruneSuspenseContainerState(container);

            // Assert
            Assert.That(IndexNames(ctx, "_suspenseContainers", container), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base finds the record by scanning its tables, which the index must keep finding.
        [Test]
        public void Given_RecordsPutBackAfterTheirContainerWasPruned_When_TheContainerIsPrunedAgain_Then_TheFallbackIsNoLongerShown()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var boundary = new ComponentFiber();
            var container = new VisualElement();
            ctx.SetSuspenseFallbackShown(boundary, container, null, 1, s_record);
            var before = ctx.RecordsOf(boundary);
            ctx.PruneSuspenseContainerState(container);
            ctx.RestoreRecordsOf(boundary, before);

            // Act
            ctx.PruneSuspenseContainerState(container);

            // Assert
            Assert.That(ctx.IsSuspenseFallbackShown(boundary, container, null, 1), Is.False);
        }

        [Test]
        public void Given_APresenceUnderAParentElement_When_TheParentIsPruned_Then_TheEntryIsRetired()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var parent = new VisualElement();
            ctx.RegisterPresenceState((new ComponentFiber(), parent, 1L));

            // Act
            ctx.PrunePresenceParentElementState(parent);

            // Assert
            Assert.That(ctx.PresenceStates.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_APresenceRenderedByAFiber_When_TheFiberIsPruned_Then_TheEntryIsRetired()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var boundary = new ComponentFiber();
            ctx.RegisterPresenceState((boundary, new VisualElement(), 1L));

            // Act
            ctx.PrunePresenceBoundaryState(boundary);

            // Assert
            Assert.That(ctx.PresenceStates.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_APresenceExpandedByAPortalPlaceholder_When_ThePlaceholderIsPruned_Then_TheEntryIsRetired()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var placeholder = new VisualElement();
            var state = ctx.RegisterPresenceState((new ComponentFiber(), new VisualElement(), 1L));
            ctx.SetPresenceOwningPortal(state, placeholder);

            // Act
            ctx.PrunePresencePortalState(placeholder);

            // Assert
            Assert.That(ctx.PresenceStates.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_APresenceUnderAParentElement_When_AnotherElementIsPruned_Then_TheEntryStays()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            ctx.RegisterPresenceState((new ComponentFiber(), new VisualElement(), 1L));

            // Act
            ctx.PrunePresenceParentElementState(new VisualElement());

            // Assert
            Assert.That(ctx.PresenceStates.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_APresenceUnderAParentElement_When_TheParentIsPrunedAfterItsEntryWasRetiredAndRegisteredAgain_Then_TheNewEntryIsRetired()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var parent = new VisualElement();
            var boundary = new ComponentFiber();
            ctx.RegisterPresenceState((boundary, parent, 1L));
            ctx.PrunePresenceBoundaryState(boundary);
            ctx.RegisterPresenceState((new ComponentFiber(), parent, 2L));

            // Act
            ctx.PrunePresenceParentElementState(parent);

            // Assert
            Assert.That(ctx.PresenceStates.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_APresenceUnderAParentElement_When_TheParentIsPruned_Then_TheIndexNoLongerNamesIt()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var parent = new VisualElement();
            ctx.RegisterPresenceState((new ComponentFiber(), parent, 1L));

            // Act
            ctx.PrunePresenceParentElementState(parent);

            // Assert
            Assert.That(IndexNames(ctx, "_presenceParents", parent), Is.EqualTo((true, false)));
        }
    }
}
