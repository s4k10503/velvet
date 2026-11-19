using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that applying a walk whose <c>V.Suspense</c> boundaries hid and revealed nothing takes no instance from
    /// <c>SuspenseOffscreenCommit</c>'s pool, so it allocates nothing even with the pool empty.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseOffscreenCommitPoolTests
    {
        [Test]
        public void Given_AnEmptyPool_When_AWalkWithNothingToHideOrRevealApplies_Then_NothingIsAllocated()
        {
            // Arrange — emptied before each window, so an instance taken from it would be one allocated
            using var mounted = V.Mount(new VisualElement(), V.Div());
            var context = mounted.Root.Reconciler!.Context;
            var pool = (Stack<SuspenseOffscreenCommit>)typeof(SuspenseOffscreenCommit)
                .GetField("s_pool", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Action emptyThePool = pool.Clear;
            Action applyNothing = () => SuspenseOffscreenCommit.Apply(context, null, null);
            Action canary = () => GC.KeepAlive(new object());
            applyNothing();
            canary();

            // Act
            var canaryBlocks = GCAllocationProbe.MedianBlocksDuring(canary);
            var blocks = GCAllocationProbe.MedianBlocksDuring(emptyThePool, applyNothing);

            // Assert — a probe that counts nothing for the canary cannot vouch for a zero
            Assert.That(canaryBlocks > 0 ? blocks : -1, Is.EqualTo(0));
        }
    }
}
