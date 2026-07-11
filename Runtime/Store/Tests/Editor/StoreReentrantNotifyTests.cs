using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests.Editor
{
    /// <summary>
    /// Pins value freshness across a re-entrant notification. Delivering the value captured when the
    /// pass began let a listener that re-entrantly pushes a newer value leave every listener AFTER it
    /// holding the superseded value as its FINAL observation — silently diverging from
    /// <see cref="Store{TState}.Current"/> until some unrelated future mutation. Each delivery must
    /// instead read the value current at call time, so the last call any listener receives in a
    /// cascade carries the store's true state.
    /// </summary>
    [TestFixture]
    internal sealed class StoreReentrantNotifyTests
    {
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
            // Arrange — the first listener supersedes value 1 with 2 mid-pass; a later listener records.
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

            // Assert — the later listener's final delivery matches Current, not the superseded 1.
            Assert.AreEqual(store.Current.Value, lastSeenByLater);
        }

        [Test]
        public void Given_AListenerReentrantlyNotifies_When_TheOuterPassResumes_Then_ItDeliversTheLiveValue()
        {
            // Arrange — listener one pushes 2 upon seeing 1; listener two records every delivery.
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

            // Assert — the outer pass's resumed delivery carries the live value, not its stale parameter.
            Assert.That(seenByTwo, Is.EqualTo(new[] { 2, 2 }));
        }
    }
}
