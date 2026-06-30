using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests.Editor
{
    /// <summary>
    /// Pins the delivery contract of <see cref="StoreStateNotifier{T}"/>, the hand-rolled listener set
    /// behind <see cref="Store{TState}"/>. The cases below lock the most
    /// delicate invariants that <see cref="StoreTests"/> does not exercise directly:
    /// <list type="bullet">
    /// <item>Listeners are notified in registration order.</item>
    /// <item>A listener that subscribes during a notification does not join the in-flight pass; it
    /// participates only from the next <c>Notify</c> (copy-on-write snapshot).</item>
    /// <item>A listener removed during a notification still receives the value already being delivered,
    /// and is absent from the next pass.</item>
    /// <item>A throwing listener aborts the remaining listeners and propagates to the caller (no internal
    /// try/catch); the throw does not clear the snapshot, so the listener still participates next time.</item>
    /// <item>The same callback subscribed twice yields two independent registrations, each disposable on
    /// its own.</item>
    /// <item>A disposed subscription receives no further values.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StoreStateNotifierTests
    {
        [Test]
        public void Given_MultipleListeners_When_Notify_Then_InvokedInRegistrationOrder()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var order = new List<int>();
            notifier.Subscribe(_ => order.Add(1));
            notifier.Subscribe(_ => order.Add(2));
            notifier.Subscribe(_ => order.Add(3));

            // Act
            notifier.Notify(1);

            // Assert
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Given_ListenerSubscribesDuringNotify_When_Notify_Then_NewListenerSkipsCurrentPass()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var lateCalls = 0;
            notifier.Subscribe(_ => notifier.Subscribe(__ => lateCalls++));

            // Act: the listener registered mid-notify must not run during the in-flight (snapshot) pass.
            notifier.Notify(1);

            // Assert
            Assert.That(lateCalls, Is.Zero);
        }

        [Test]
        public void Given_ListenerSubscribedDuringNotify_When_NextNotify_Then_NewListenerParticipates()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var lateCalls = 0;
            var armed = false;
            notifier.Subscribe(_ =>
            {
                if (armed)
                {
                    return;
                }
                armed = true;
                notifier.Subscribe(__ => lateCalls++);
            });

            // Act
            notifier.Notify(1); // registers the late listener (does not run it this pass)
            notifier.Notify(2); // late listener now participates

            // Assert
            Assert.That(lateCalls, Is.EqualTo(1));
        }

        [Test]
        public void Given_ListenerUnsubscribesAnotherDuringNotify_When_Notify_Then_RemovedListenerStillReceivesCurrentValue()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var calls = new List<int>();
            IDisposable secondSub = null;
            notifier.Subscribe(_ =>
            {
                calls.Add(1);
                secondSub?.Dispose(); // remove listener 2 mid-pass
            });
            secondSub = notifier.Subscribe(_ => calls.Add(2));
            notifier.Subscribe(_ => calls.Add(3));

            // Act: the in-flight snapshot still delivers to listener 2 even though it was just removed.
            notifier.Notify(1);

            // Assert
            Assert.That(calls, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Given_ListenerRemovedDuringNotify_When_NextNotify_Then_RemovalTakesEffect()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var calls = new List<int>();
            IDisposable secondSub = null;
            var removed = false;
            notifier.Subscribe(_ =>
            {
                calls.Add(1);
                if (!removed)
                {
                    removed = true;
                    secondSub?.Dispose();
                }
            });
            secondSub = notifier.Subscribe(_ => calls.Add(2));
            notifier.Subscribe(_ => calls.Add(3));

            // Act
            notifier.Notify(1); // listener 2 removed mid-pass but still delivered
            calls.Clear();
            notifier.Notify(2); // next pass reflects the removal

            // Assert
            Assert.That(calls, Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void Given_ThrowingListener_When_Notify_Then_AbortsRemainingAndPropagatesToCaller()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var firstCalled = false;
            var afterThrowCalled = false;
            notifier.Subscribe(_ => firstCalled = true);
            notifier.Subscribe(_ => throw new InvalidOperationException("boom"));
            notifier.Subscribe(_ => afterThrowCalled = true);

            // Act + Assert: no internal try/catch, so the exception aborts the cycle and reaches the caller.
            Assert.Throws<InvalidOperationException>(() => notifier.Notify(1));
            Assert.That(firstCalled, Is.True);
            Assert.That(afterThrowCalled, Is.False);
        }

        [Test]
        public void Given_SameCallbackSubscribedTwice_When_Notify_Then_InvokedTwice()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var calls = 0;
            Action<int> listener = _ => calls++;
            notifier.Subscribe(listener);
            notifier.Subscribe(listener);

            // Act
            notifier.Notify(1);

            // Assert
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void Given_SameCallbackSubscribedTwice_When_OneDisposed_Then_OtherStillReceives()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var calls = 0;
            Action<int> listener = _ => calls++;
            var firstSub = notifier.Subscribe(listener);
            notifier.Subscribe(listener);

            // Act: disposing one registration leaves the independent duplicate intact.
            firstSub.Dispose();
            notifier.Notify(1);

            // Assert
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_DisposedSubscription_When_Notify_Then_ListenerNotInvoked()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var calls = 0;
            var sub = notifier.Subscribe(_ => calls++);

            // Act
            sub.Dispose();
            notifier.Notify(1);

            // Assert
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Given_Value_When_Notify_Then_ValueReflectsLatest()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(7);

            // Act + Assert
            Assert.That(notifier.Value, Is.EqualTo(7));
            notifier.Notify(42);
            Assert.That(notifier.Value, Is.EqualTo(42));
        }

        [Test]
        public void Given_DisposedNotifier_When_Notify_Then_ListenersNotInvoked()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var calls = 0;
            notifier.Subscribe(_ => calls++);

            // Act: Notify after Dispose is a no-op.
            notifier.Dispose();
            notifier.Notify(1);

            // Assert
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Given_DisposedNotifier_When_Subscribe_Then_ListenerNotInvokedAndDisposeIsSafe()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var calls = 0;
            notifier.Dispose();

            // Act: subscribing after disposal returns a disposable that is safe to dispose and never fires.
            var sub = notifier.Subscribe(_ => calls++);
            Assert.DoesNotThrow(() => sub.Dispose());
            notifier.Notify(1);

            // Assert
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void Given_Subscription_When_DisposedTwice_Then_Idempotent()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var calls = 0;
            var sub = notifier.Subscribe(_ => calls++);

            // Act: disposing the same subscription twice must not throw or disturb other registrations.
            sub.Dispose();
            Assert.DoesNotThrow(() => sub.Dispose());
            notifier.Subscribe(_ => calls++);
            notifier.Notify(1);

            // Assert: the first listener is gone after one dispose; only the live listener fires.
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_ThrowingListener_When_NotifiedAgain_Then_SameListenerStillParticipates()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var throwCount = 0;
            notifier.Subscribe(_ => { throwCount++; throw new InvalidOperationException("boom"); });

            // Act + Assert: a throw does not clear the cached snapshot, so the same listener fires next Notify.
            Assert.Throws<InvalidOperationException>(() => notifier.Notify(1));
            Assert.Throws<InvalidOperationException>(() => notifier.Notify(2));
            Assert.That(throwCount, Is.EqualTo(2));
        }
    }
}
