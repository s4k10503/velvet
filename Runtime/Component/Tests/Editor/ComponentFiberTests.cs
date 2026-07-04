using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="ComponentFiber"/> as the persistent identity node of the
    /// component tree.
    /// <list type="bullet">
    /// <item>A new fiber is an isolated node with no parent, child, or sibling.</item>
    /// <item><c>AppendChild</c> links a child under a parent and appends it to the tail of the parent's
    /// sibling chain, preserving insertion order; appending an already-parented child reparents it,
    /// detaching it from its previous parent first.</item>
    /// <item><c>RemoveChild</c> unlinks a child from any position in the sibling chain (head, middle, tail,
    /// or sole child) and clears the removed node's parent and sibling pointers; removing a child that
    /// does not belong to the parent is a no-op.</item>
    /// <item><c>Detach</c> removes the fiber from its own parent, and is a no-op on an orphan.</item>
    /// <item><c>ComponentBoundarySearch.FindNearestSuspenseBoundary</c> walks the parent chain (including
    /// self) and returns the closest suspense boundary, or null when none exists.</item>
    /// <item>Context dependencies form a set: registering a context is idempotent, distinct contexts are
    /// stored separately, membership is queryable, and the set can be cleared.</item>
    /// <item>Async slots are an ordered list with a monotonically advancing cursor that can be reset to
    /// zero; disposing the slots cancels and empties them without forcing the resources to a terminal state.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ComponentFiberTests
    {
        #region Tree shape

        [Test]
        public void Given_NewFiber_When_Constructed_Then_HasNoParentChildOrSibling()
        {
            // Act
            var fiber = new ComponentFiber();

            // Assert
            Assert.That(
                new[] { fiber.Parent, fiber.Child, fiber.Sibling },
                Is.All.Null,
                "A new fiber is an isolated node with no parent, child, or sibling");
        }

        [Test]
        public void Given_Parent_When_FirstChildAppended_Then_ChildLinksToParent()
        {
            // Arrange
            var parent = new ComponentFiber();
            var child = new ComponentFiber();

            // Act
            parent.AppendChild(child);

            // Assert — the first child becomes the head, links back to its parent, and has no sibling
            Assert.That(parent.Child, Is.SameAs(child), "The child becomes the parent's head");
            Assert.That(child.Parent, Is.SameAs(parent), "The child links back to its parent");
            Assert.That(child.Sibling, Is.Null, "The sole child has no sibling");
        }

        [Test]
        public void Given_ParentWithOneChild_When_SecondChildAppended_Then_LinksAsSiblingOfFirst()
        {
            // Arrange
            var parent = new ComponentFiber();
            var first = new ComponentFiber();
            var second = new ComponentFiber();
            parent.AppendChild(first);

            // Act
            parent.AppendChild(second);

            // Assert — the second child is appended after the first in the sibling chain
            Assert.That(parent.Child, Is.SameAs(first), "The first child remains the head");
            Assert.That(first.Sibling, Is.SameAs(second), "The second child follows the first");
            Assert.That(second.Parent, Is.SameAs(parent), "The second child links back to the parent");
        }

        [Test]
        public void Given_ParentWithTwoChildren_When_ThirdChildAppended_Then_AppendedToTailOfSiblingChain()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            var c = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);

            // Act
            parent.AppendChild(c);

            // Assert — AppendChild preserves insertion order to the tail of the chain
            Assert.That(parent.Child, Is.SameAs(a), "The head is the first appended child");
            Assert.That(a.Sibling, Is.SameAs(b), "The second follows the first");
            Assert.That(b.Sibling, Is.SameAs(c), "The third follows the second");
            Assert.That(c.Sibling, Is.Null, "The last appended child is the tail");
        }

        [Test]
        public void Given_ChildOfAnotherParent_When_AppendedToNewParent_Then_DetachedFromOldParent()
        {
            // Arrange
            var oldParent = new ComponentFiber();
            var newParent = new ComponentFiber();
            var child = new ComponentFiber();
            oldParent.AppendChild(child);

            // Act
            newParent.AppendChild(child);

            // Assert — reparenting detaches the child from its previous parent's chain
            Assert.That(child.Parent, Is.SameAs(newParent), "The child links to the new parent");
            Assert.That(newParent.Child, Is.SameAs(child), "The new parent adopts the child as its head");
            Assert.That(oldParent.Child, Is.Null, "The old parent no longer references the child");
        }

        #endregion

        #region Child removal

        [Test]
        public void Given_SiblingChain_When_HeadChildRemoved_Then_SecondBecomesNewHead()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);

            // Act
            parent.RemoveChild(a);

            // Assert — removing the head promotes the next sibling and clears the removed node's links
            Assert.That(parent.Child, Is.SameAs(b), "The next sibling becomes the new head");
            Assert.That(a.Parent, Is.Null, "The removed node has no parent");
            Assert.That(a.Sibling, Is.Null, "The removed node has no sibling");
        }

        [Test]
        public void Given_SiblingChain_When_MiddleChildRemoved_Then_ChainRelinksAcrossTheGap()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            var c = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.AppendChild(c);

            // Act
            parent.RemoveChild(b);

            // Assert — removing a middle node relinks its predecessor to its successor
            Assert.That(a.Sibling, Is.SameAs(c), "The predecessor now points to the successor");
            Assert.That(b.Parent, Is.Null, "The removed node has no parent");
            Assert.That(b.Sibling, Is.Null, "The removed node has no sibling");
        }

        [Test]
        public void Given_SiblingChain_When_TailChildRemoved_Then_ChainTruncates()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);

            // Act
            parent.RemoveChild(b);

            // Assert — removing the tail leaves the predecessor as the new tail
            Assert.That(parent.Child, Is.SameAs(a), "The head is unchanged");
            Assert.That(a.Sibling, Is.Null, "The predecessor becomes the new tail");
            Assert.That(b.Parent, Is.Null, "The removed tail has no parent");
        }

        [Test]
        public void Given_SoleChild_When_Removed_Then_ParentChildPointerCleared()
        {
            // Arrange
            var parent = new ComponentFiber();
            var only = new ComponentFiber();
            parent.AppendChild(only);

            // Act
            parent.RemoveChild(only);

            // Assert — removing the only child empties the parent
            Assert.That(parent.Child, Is.Null, "The parent has no child");
            Assert.That(only.Parent, Is.Null, "The removed child has no parent");
        }

        [Test]
        public void Given_ChildOfAnotherParent_When_RemovedFromUnrelatedParent_Then_NoOp()
        {
            // Arrange
            var parentA = new ComponentFiber();
            var parentB = new ComponentFiber();
            var child = new ComponentFiber();
            parentA.AppendChild(child);

            // Act
            parentB.RemoveChild(child);

            // Assert — removing a child that does not belong to the parent leaves the real parent untouched
            Assert.That(child.Parent, Is.SameAs(parentA), "The child still belongs to its real parent");
            Assert.That(parentA.Child, Is.SameAs(child), "The real parent still references the child");
        }

        [Test]
        public void Given_RemovingForeignChild_When_Invoked_Then_DoesNotThrow()
        {
            // Arrange
            var parentA = new ComponentFiber();
            var parentB = new ComponentFiber();
            var child = new ComponentFiber();
            parentA.AppendChild(child);

            // Act + Assert
            Assert.DoesNotThrow(() => parentB.RemoveChild(child));
        }

        [Test]
        public void Given_ChildInSiblingChain_When_Detached_Then_RemovesSelfAndClearsParent()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);

            // Act
            a.Detach();

            // Assert — Detach removes the fiber from its own parent's chain
            Assert.That(parent.Child, Is.SameAs(b), "The remaining sibling becomes the head");
            Assert.That(a.Parent, Is.Null, "The detached fiber has no parent");
        }

        [Test]
        public void Given_OrphanFiber_When_Detached_Then_DoesNotThrow()
        {
            // Arrange
            var fiber = new ComponentFiber();

            // Act + Assert
            Assert.DoesNotThrow(() => fiber.Detach());
        }

        #endregion

        #region Boundary search

        [Test]
        public void Given_AncestorSuspenseBoundary_When_SearchedFromDescendant_Then_WalksUpToBoundary()
        {
            // Arrange
            var grandparent = new ComponentFiber { IsSuspenseBoundary = true };
            var parent = new ComponentFiber();
            var child = new ComponentFiber();
            grandparent.AppendChild(parent);
            parent.AppendChild(child);

            // Act
            var result = ComponentBoundarySearch.FindNearestSuspenseBoundary(child);

            // Assert
            Assert.That(result, Is.SameAs(grandparent),
                "Suspense boundary search walks the parent chain (self-inclusive) to the nearest boundary");
        }

        #endregion

        #region Context dependencies

        [Test]
        public void Given_NewContext_When_Registered_Then_StoredAsADependency()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var ctx = new object();

            // Act
            fiber.RegisterContextDependency(ctx);

            // Assert
            Assert.That(fiber.Dependencies.Count, Is.EqualTo(1),
                "A registered context produces exactly one dependency entry");
        }

        [Test]
        public void Given_NewContext_When_Registered_Then_EntryCarriesThatContext()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var ctx = new object();

            // Act
            fiber.RegisterContextDependency(ctx);

            // Assert
            Assert.That(fiber.Dependencies[0].Context, Is.SameAs(ctx), "The dependency entry references the context");
        }

        [Test]
        public void Given_AlreadyRegisteredContext_When_RegisteredAgain_Then_NoDuplicateEntry()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var ctx = new object();
            fiber.RegisterContextDependency(ctx);

            // Act
            fiber.RegisterContextDependency(ctx);

            // Assert
            Assert.That(fiber.Dependencies.Count, Is.EqualTo(1), "Re-registering the same context is idempotent");
        }

        [Test]
        public void Given_TwoDistinctContexts_When_Registered_Then_EachStoredAsAMembership()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var ctxA = new object();
            var ctxB = new object();

            // Act
            fiber.RegisterContextDependency(ctxA);
            fiber.RegisterContextDependency(ctxB);

            // Assert
            Assert.That(
                (fiber.Dependencies.Count, fiber.HasDependencyOn(ctxA), fiber.HasDependencyOn(ctxB)),
                Is.EqualTo((2, true, true)),
                "Distinct contexts are stored separately and are each queryable");
        }

        [Test]
        public void Given_FiberWithoutDependency_When_QueriedForUnknownContext_Then_ReturnsFalse()
        {
            // Arrange
            var fiber = new ComponentFiber();

            // Act + Assert
            Assert.That(fiber.HasDependencyOn(new object()), Is.False, "An unregistered context is not a member");
        }

        [Test]
        public void Given_FiberWithDependencies_When_Cleared_Then_DependencySetIsEmpty()
        {
            // Arrange
            var fiber = new ComponentFiber();
            fiber.RegisterContextDependency(new object());
            fiber.RegisterContextDependency(new object());

            // Act
            fiber.ClearDependencies();

            // Assert
            Assert.That(fiber.Dependencies, Is.Empty, "Clearing removes every dependency entry");
        }

        #endregion

        #region Async slots

        [Test]
        public void Given_NewFiber_When_Inspected_Then_AsyncSlotsAreEmpty()
        {
            // Arrange
            var fiber = new ComponentFiber();

            // Act + Assert
            Assert.That(fiber.AsyncSlots, Is.Empty, "A new fiber owns no async slots");
        }

        [Test]
        public void Given_AsyncSlotCursor_When_AdvancedRepeatedly_Then_YieldsConsecutiveIndices()
        {
            // Arrange
            var fiber = new ComponentFiber();

            // Act
            var indices = (fiber.NextAsyncSlotIndex(), fiber.NextAsyncSlotIndex(), fiber.NextAsyncSlotIndex());

            // Assert
            Assert.That(indices, Is.EqualTo((0, 1, 2)), "The cursor advances by one on each call");
        }

        [Test]
        public void Given_AdvancedAsyncSlotCursor_When_Reset_Then_NextIndexRestartsAtZero()
        {
            // Arrange
            var fiber = new ComponentFiber();
            fiber.NextAsyncSlotIndex();
            fiber.NextAsyncSlotIndex();

            // Act
            fiber.ResetAsyncSlotCursor();

            // Assert
            Assert.That(fiber.NextAsyncSlotIndex(), Is.EqualTo(0), "Resetting restarts the cursor from zero");
        }

        [Test]
        public void Given_FiberWithAsyncSlots_When_Disposed_Then_SlotsAreEmptied()
        {
            // Arrange
            var fiber = new ComponentFiber();
            fiber.AsyncSlots.Add(new FiberAsyncResource<int>(System.Array.Empty<object>()));
            fiber.AsyncSlots.Add(new FiberAsyncResource<string>(System.Array.Empty<object>()));

            // Act
            fiber.DisposeAsyncSlots();

            // Assert
            Assert.That(fiber.AsyncSlots, Is.Empty, "Disposing clears every async slot");
        }

        [Test]
        public void Given_PendingAsyncResource_When_SlotsDisposed_Then_TerminalStateIsUnchanged()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var resource = new FiberAsyncResource<int>(System.Array.Empty<object>());
            fiber.AsyncSlots.Add(resource);

            // Act
            fiber.DisposeAsyncSlots();

            // Assert
            Assert.That(resource.Status, Is.EqualTo(FiberAsyncResourceStatus.Pending),
                "Dispose cancels the resource but does not force it into a terminal Success/Error state");
        }

        #endregion
    }
}
