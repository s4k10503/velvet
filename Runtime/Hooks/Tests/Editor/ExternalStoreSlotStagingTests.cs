using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that what a <see cref="Hooks.UseSyncExternalStore{T}"/> render reads reaches the passive effects
    /// only once that render settles, so a later render that never settles leaves an earlier commit's pending
    /// subscription as that commit staged it.
    /// </summary>
    /// <remarks>
    /// The slot is reached directly because a render-phase re-run stages the slot again before it settles, and a
    /// throw a boundary catches unmounts the fiber along with the effects its last commit queued.
    /// </remarks>
    [TestFixture]
    internal sealed class ExternalStoreSlotStagingTests
    {
        private static int s_firstSubscribeCalls;
        private static ComponentFiber s_readerFiber;

        [SetUp]
        public void SetUp()
        {
            s_firstSubscribeCalls = 0;
            s_readerFiber = null;
        }

        [Test]
        public void Given_ACommitsSubscriptionStillPending_When_ALaterRenderThatNeverSettlesReadsAnotherStore_Then_TheEffectSubscribesTheCommittedStore()
        {
            // Arrange — Render called with a list of its own stands for a render that never settles: what it read
            // is left on the slot, and what it would queue reaches none of the effects the mount queued
            using var mounted = V.Mount(new VisualElement(), V.Component(ReaderRender, key: "reader"));
            var slot = (HookExternalStoreSlot<int>)s_readerFiber.StoreSlots[0];
            slot.Render(new List<HookEffectSlot>(), _ => () => { }, () => 2, 2);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.AreEqual(1, s_firstSubscribeCalls);
        }

        private static Action SubscribeFirst(Action onStoreChange)
        {
            s_firstSubscribeCalls++;
            return () => { };
        }

        private static int ReadFirst() => 1;

        [Component]
        private static VNode ReaderRender()
        {
            s_readerFiber = FiberAmbientStack.Current;
            var value = Hooks.UseSyncExternalStore(SubscribeFirst, ReadFirst);
            return V.Label(text: value.ToString());
        }
    }
}
