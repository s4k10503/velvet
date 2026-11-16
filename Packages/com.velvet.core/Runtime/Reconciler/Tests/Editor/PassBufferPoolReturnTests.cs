using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the lists a layout commit and a context spine rent from <see cref="ReconcilerBufferPool"/>
    /// go back to it when the call that rented them ends, so a commit does not leave them behind for the collector.
    /// Each case puts a marked list on top of a pool the call rents from, runs that call and reads whether the pool
    /// holds the marked list afterwards.
    /// </summary>
    [TestFixture]
    internal sealed class PassBufferPoolReturnTests
    {
        private static ComponentFiber s_childFiber;

        [Component(Compiler = false)]
        private static VNode ChildRender()
        {
            s_childFiber = FiberAmbientStack.Current;
            return V.Label(text: "child");
        }

        [Component(Compiler = false)]
        private static VNode HostRender()
            => V.Div(name: "host", children: new VNode[] { V.Component(ChildRender, key: "child") });

        [SetUp]
        public void SetUp() => s_childFiber = null;

        // Whether the pool behind poolField holds item, read from the stack that backs it.
        private static bool PoolHolds(ReconcilerBufferPool pool, string poolField, object item)
        {
            var clearable = typeof(ReconcilerBufferPool)
                .GetField(poolField, BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(pool);
            var held = (IEnumerable)clearable.GetType()
                .GetField("_pool", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(clearable);
            foreach (var entry in held)
            {
                if (ReferenceEquals(entry, item)) return true;
            }
            return false;
        }

        // GREEN_ON_BASE(characterization): the base returns the entry list a commit rents for its roots. Drop
        // `ctx.BufferPool.ReturnCommitEntryList(roots);` in CommitAsRoot and the list is not in the pool afterwards.
        [Test]
        public void Given_AMountedFiber_When_ItsEffectsAreCommittedAsARoot_Then_TheRootsListIsBackInThePool()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(HostRender, key: "host"));
            var pool = mounted.Root.Reconciler.Context.BufferPool;
            var borrowed = pool.RentCommitEntryList();
            pool.ReturnCommitEntryList(borrowed);

            // Act
            FiberExecutionContextTestAccess.RunAsEntryForTest(() => FiberEffects.CommitSubtreeEffects(mounted.Root));

            // Assert
            Assert.That(PoolHolds(pool, "_commitEntryListPool", borrowed), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base returns the entry list the deferred commit rents for its roots.
        // Drop `ctx.BufferPool.ReturnCommitEntryList(roots);` in CommitDeferred and the list is not in the pool afterwards.
        [Test]
        public void Given_ALayoutCommitDeferredByADrain_When_TheDrainFlushesIt_Then_TheRootsListIsBackInThePool()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(HostRender, key: "host"));
            var context = mounted.Root.Reconciler.Context;
            var borrowed = context.BufferPool.RentCommitEntryList();
            context.BufferPool.ReturnCommitEntryList(borrowed);
            context.PendingDrainLayoutEffects.Add((mounted.Root, false));

            // Act
            FiberExecutionContextTestAccess.RunAsEntryForTest(() => FiberEffects.FlushDeferredDrainLayoutEffects(context));

            // Assert
            Assert.That(PoolHolds(context.BufferPool, "_commitEntryListPool", borrowed), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base returns the list a commit takes the deferred entries off the stack
        // into. Drop `bufferPool.ReturnDeferredEntryList(popped);` in TakeBatch and the list is not in the pool afterwards.
        [Test]
        public void Given_AMountedFiber_When_ItsEffectsAreCommitted_Then_TheDeferredEntryListIsBackInThePool()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(HostRender, key: "host"));
            var pool = mounted.Root.Reconciler.Context.BufferPool;
            var borrowed = pool.RentDeferredEntryList();
            pool.ReturnDeferredEntryList(borrowed);

            // Act
            FiberExecutionContextTestAccess.RunAsEntryForTest(() => FiberEffects.CommitSubtreeEffects(mounted.Root));

            // Assert
            Assert.That(PoolHolds(pool, "_deferredEntryListPool", borrowed), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base returns the list of Providers a spine pushed when it unwinds. Drop
        // `_bufferPool.ReturnProviderNodeList(_pushed);` in Unwind and the list is not in the pool afterwards.
        [Test]
        public void Given_ASpinePushedForAChildFiber_When_ItUnwinds_Then_TheProviderListIsBackInThePool()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(HostRender, key: "host"));
            var pool = mounted.Root.Reconciler.Context.BufferPool;
            var borrowed = pool.RentProviderNodeList();
            pool.ReturnProviderNodeList(borrowed);
            var spine = FiberContextSpine.Push(s_childFiber);

            // Act
            spine.Unwind();

            // Assert
            Assert.That(PoolHolds(pool, "_providerNodeListPool", borrowed), Is.True);
        }
    }
}
