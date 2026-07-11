using System;
using System.Threading;
using NUnit.Framework;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a blocker's Block() side effect lands only for live registrations on a live
    /// attempt. CheckAsync snapshots the registration list (so an unregister during the pass cannot
    /// corrupt iteration) but then applied Block() unconditionally: an entry disposed during the
    /// pass (its owner unmounted, or an earlier blocker's decision tore it down) still flipped its
    /// state to Blocked, and a superseded attempt (its token already cancelled by a newer
    /// navigation) still flipped states the caller was about to discard — stranding a
    /// confirm-before-leaving UI on a navigation nobody was waiting on until some unrelated future
    /// navigation reset it.
    /// </summary>
    [TestFixture]
    internal sealed class BlockerLivenessTests
    {
        [Test]
        public void Given_ABlockerThatUnregistersItselfWhileBlocking_When_ThePassCompletes_Then_ItsStateStaysIdle()
        {
            // Arrange — the blocker's own check disposes its registration before answering true,
            // the synchronous shape of an owner unmounting mid-check.
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            IDisposable registration = null;
            registration = manager.Register(_ =>
            {
                registration.Dispose();
                return true;
            }, state);

            // Act
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

            // Assert — a dead registration neither blocks the navigation nor strands its state.
            Assert.That((blocked, state.Status), Is.EqualTo((false, RouteBlockerStatus.Idle)));
        }

        [Test]
        public void Given_AnEarlierBlockerUnregistersALaterOne_When_ThePassContinues_Then_TheLaterStateStaysIdle()
        {
            // Arrange — the first blocker's decision tears down the second's registration (e.g. it
            // unmounts the subtree owning it) before the snapshot loop reaches it.
            var manager = new RouteBlockerManager();
            var laterState = new RouteBlockerState();
            IDisposable laterRegistration = null;
            using var earlier = manager.Register(_ =>
            {
                laterRegistration.Dispose();
                return false;
            }, new RouteBlockerState());
            laterRegistration = manager.Register(_ => true, laterState);

            // Act
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

            // Assert
            Assert.That((blocked, laterState.Status), Is.EqualTo((false, RouteBlockerStatus.Idle)));
        }

        [Test]
        public void Given_AnAlreadySupersededAttempt_When_ABlockerWouldBlock_Then_NoStateIsFlipped()
        {
            // Arrange — the attempt's token is already cancelled (a newer navigation took over);
            // the caller is about to discard this result as Cancelled.
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            using var registration = manager.Register(_ => true, state);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            manager.CheckAsync(Attempt(), cts.Token).GetAwaiter().GetResult();

            // Assert — the abandoned attempt leaves no Blocked state behind.
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Idle));
        }
    }
}
