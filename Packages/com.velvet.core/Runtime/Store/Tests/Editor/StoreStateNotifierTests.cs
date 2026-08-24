using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests.Editor
{
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

            // Act
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
            notifier.Notify(1);
            notifier.Notify(2);

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
                secondSub?.Dispose();
            });
            secondSub = notifier.Subscribe(_ => calls.Add(2));
            notifier.Subscribe(_ => calls.Add(3));

            // Act
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
            notifier.Notify(1);
            calls.Clear();
            notifier.Notify(2);

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

            // Act + Assert
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

            // Act
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

            // Act
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

            // Act
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

            // Act
            sub.Dispose();
            Assert.DoesNotThrow(() => sub.Dispose());
            notifier.Subscribe(_ => calls++);
            notifier.Notify(1);

            // Assert
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_ThrowingListener_When_NotifiedAgain_Then_SameListenerStillParticipates()
        {
            // Arrange
            var notifier = new StoreStateNotifier<int>(0);
            var throwCount = 0;
            notifier.Subscribe(_ => { throwCount++; throw new InvalidOperationException("boom"); });

            // Act + Assert
            Assert.Throws<InvalidOperationException>(() => notifier.Notify(1));
            Assert.Throws<InvalidOperationException>(() => notifier.Notify(2));
            Assert.That(throwCount, Is.EqualTo(2));
        }

        private readonly record struct CounterState(int Value);

        private sealed class CounterStore : Store<CounterState>
        {
            public CounterStore() : base(new CounterState(0)) { }
            public void Set(int value) => SetState(_ => new CounterState(value));
            protected override void ResetCore() => SetState(_ => new CounterState(0));
        }

        [Test]
        public void Given_AnEarlierListenerReentrantlySetsState_When_Notified_Then_ALaterListenersFinalValueIsCurrent()
        {
            // Arrange
            using var store = new CounterStore();
            var lastSeenByLater = -1;
            using var reentrant = store.Subscribe(s =>
            {
                if (s.Value == 1)
                {
                    store.Set(2);
                }
            });
            using var later = store.Subscribe(s => lastSeenByLater = s.Value);

            // Act
            store.Set(1);

            // Assert
            Assert.AreEqual(store.Current.Value, lastSeenByLater);
        }

        [Test]
        public void Given_AListenerReentrantlyNotifies_When_TheOuterPassResumes_Then_ItDeliversTheLiveValue()
        {
            // Arrange
            using var notifier = new StoreStateNotifier<int>(0);
            var seenByTwo = new List<int>();
            notifier.Subscribe(v =>
            {
                if (v == 1)
                {
                    notifier.Notify(2);
                }
            });
            notifier.Subscribe(seenByTwo.Add);

            // Act
            notifier.Notify(1);

            // Assert
            Assert.That(seenByTwo, Is.EqualTo(new[] { 2, 2 }));
        }
    }
}
