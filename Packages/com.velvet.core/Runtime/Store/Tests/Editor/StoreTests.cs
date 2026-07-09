using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace Velvet.Tests.Editor
{
    /// <summary>
    /// Specifies the contract of <see cref="Store{TState}"/>, the state-management foundation that holds an
    /// immutable state record and notifies subscribers of changes.
    /// <list type="bullet">
    /// <item>A new store exposes its construction value through both <c>Current</c> and <c>InitialState</c>, and
    /// a fresh subscription created with <c>fireImmediately</c> receives that value immediately.</item>
    /// <item><c>InitialState</c> is fixed at construction time and never changes, even after the state is updated.</item>
    /// <item><c>SetState</c> updates the state and returns whether it actually changed; it bails out only when the
    /// updater returns the identical instance, so a distinct-but-value-equal record still updates and notifies.</item>
    /// <item><c>Mutate</c> updates the state and notifies unconditionally, even when the new value equals the old one.</item>
    /// <item>A selected slice notifies only when the slice changes under the chosen comparer; without a comparer it
    /// uses identity equality, and a sequence comparer bails when a fresh list holds identity-equal elements.</item>
    /// <item><c>Subscribe</c> delivers each subsequent state, optionally firing immediately with the current value;
    /// its (current, previous) overload pairs each state with the one before it. A null listener is rejected.</item>
    /// <item>Disposing runs subclass dispose logic exactly once and cancels the store's cancellation token.</item>
    /// <item><c>Reset</c> returns the state to its initial value.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StoreTests
    {
        private TestStore _sut;

        [SetUp]
        public void Setup()
        {
            StoreLogger.Default = new NullStoreLogger();
            _sut = new TestStore();
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
            StoreLogger.Default = new StoreLogger();
        }

        #region InitialState

        [Test]
        public void Given_NewStore_When_CurrentRead_Then_ReturnsInitialValueField()
        {
            // Act + Assert
            Assert.That(_sut.Current.Value, Is.EqualTo(0));
        }

        [Test]
        public void Given_NewStore_When_CurrentRead_Then_ReturnsInitialNameField()
        {
            // Act + Assert
            Assert.That(_sut.Current.Name, Is.EqualTo("Initial"));
        }

        [Test]
        public void Given_NewStore_When_SelectSubscribedWithFireImmediately_Then_InitialValueDeliveredImmediately()
        {
            // Arrange
            int? received = null;

            // Act
            using var sub = _sut.Select(
                s => s.Value,
                (cur, _) => received = cur,
                fireImmediately: true);

            // Assert
            Assert.That(received, Is.EqualTo(0));
        }

        [Test]
        public void Given_NewStore_When_InitialStateRead_Then_MatchesConstructionValue()
        {
            // Act + Assert
            Assert.That(_sut.InitialState.Value, Is.EqualTo(0));
        }

        [Test]
        public void Given_StateMutated_When_InitialStateRead_Then_StillHoldsConstructionValue()
        {
            // Arrange
            _sut.PublicSetState(s => s with { Value = 42, Name = "Modified" });
            Assume.That(_sut.Current.Value, Is.EqualTo(42), "Precondition: the state was updated");

            // Act + Assert
            Assert.That(_sut.InitialState.Value, Is.EqualTo(0));
        }

        [Test]
        public void Given_FireImmediatelyListenerThatMutates_When_Subscribed_Then_ItObservesItsOwnMutation()
        {
            // Arrange — a listener that mutates the store during its immediate (fireImmediately) invocation.
            var calls = 0;
            var lastSeen = -1;
            var sub = _sut.Subscribe(
                s =>
                {
                    calls++;
                    lastSeen = s.Value;
                    if (calls == 1) _sut.PublicSetState(st => st with { Value = 99 });
                },
                fireImmediately: true);

            // Assert — the subscription must be registered BEFORE the immediate fire, so the listener observes the
            // mutation it makes during that fire. A fire-before-subscribe order would miss it.
            sub.Dispose();
            Assert.That(lastSeen, Is.EqualTo(99),
                "A fireImmediately listener must observe a store mutation it performs during its immediate fire");
        }

        #endregion

        #region SetState (equality-check)

        [Test]
        public void Given_InitialState_When_SetStateChangesValue_Then_ReturnsTrue()
        {
            // Act
            var result = _sut.PublicSetState(s => s with { Value = 42 });

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void Given_InitialState_When_SetStateChangesValue_Then_CurrentIsUpdated()
        {
            // Act
            _sut.PublicSetState(s => s with { Value = 42 });

            // Assert
            Assert.That(_sut.Current.Value, Is.EqualTo(42));
        }

        [Test]
        public void Given_UpdaterReturnsSameInstance_When_SetState_Then_ReturnsFalse()
        {
            // Act — only an identical reference is treated as "no change"
            var result = _sut.PublicSetState(s => s);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void Given_UpdaterReturnsSameInstance_When_SetState_Then_NoNotification()
        {
            // Arrange
            var notifications = 0;
            using var sub = _sut.Subscribe(_ => notifications++);

            // Act
            _sut.PublicSetState(s => s);

            // Assert
            Assert.That(notifications, Is.EqualTo(0));
        }

        [Test]
        public void Given_UpdaterReturnsValueEqualButDistinctInstance_When_SetState_Then_ReturnsTrue()
        {
            // Act — a distinct instance with equal field values is a different reference, so it counts as a change
            var result = _sut.PublicSetState(s => s with { Value = 0, Name = "Initial" });

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void Given_UpdaterReturnsValueEqualButDistinctInstance_When_SetState_Then_Notifies()
        {
            // Arrange
            var notifications = 0;
            using var sub = _sut.Subscribe(_ => notifications++);

            // Act
            _sut.PublicSetState(s => s with { Value = 0, Name = "Initial" });

            // Assert
            Assert.That(notifications, Is.EqualTo(1));
        }

        #endregion

        #region Mutate (unconditional)

        [Test]
        public void Given_InitialState_When_Mutate_Then_CurrentIsUpdated()
        {
            // Act
            _sut.PublicMutate(s => s with { Value = 99 });

            // Assert
            Assert.That(_sut.Current.Value, Is.EqualTo(99));
        }

        [Test]
        public void Given_InitialState_When_MutateWithEqualValue_Then_Notifies()
        {
            // Arrange
            var notifications = 0;
            using var sub = _sut.Subscribe(_ => notifications++);

            // Act — Mutate notifies unconditionally even for an equal value
            _sut.PublicMutate(s => s with { Value = 0, Name = "Initial" });

            // Assert
            Assert.That(notifications, Is.EqualTo(1));
        }

        #endregion

        #region Select

        [Test]
        public void Given_SelectedSliceSubscribed_When_SliceChanges_Then_NewSliceIsObserved()
        {
            // Arrange
            var receivedNames = new List<string>();
            using var sub = _sut.Select(
                s => s.Name,
                (cur, _) => receivedNames.Add(cur));

            // Act
            _sut.PublicSetState(s => s with { Name = "Updated" });

            // Assert
            Assert.That(receivedNames, Does.Contain("Updated"));
        }

        [Test]
        public void Given_SelectedSliceSubscribed_When_UnselectedPartChanges_Then_NotNotified()
        {
            // Arrange
            var nameNotifications = 0;
            using var sub = _sut.Select(
                s => s.Name,
                (_, _) => nameNotifications++,
                fireImmediately: true);
            Assume.That(nameNotifications, Is.EqualTo(1), "Precondition: the initial slice was delivered once");

            // Act — leave Name unchanged, only update Value
            _sut.PublicSetState(s => s with { Value = 100 });

            // Assert
            Assert.That(nameNotifications, Is.EqualTo(1));
        }

        [Test]
        public void Given_SequenceComparer_When_SameElementsRepackedIntoNewList_Then_ObserverNotNotified()
        {
            // Arrange — subscribe with a sequence comparer; fireImmediately delivers the current slice once
            var a = new Item(1);
            var b = new Item(2);
            _sut.PublicSetState(s => s with { Items = new[] { a, b } });
            var notifications = 0;
            Action<IReadOnlyList<Item>, IReadOnlyList<Item>> observer = (_, __) => notifications++;
            using var _ = _sut.Select(
                s => s.Items,
                observer,
                StoreShallowEqualityComparer.Sequence<Item>(),
                fireImmediately: true);
            Assume.That(notifications, Is.EqualTo(1), "Precondition: fireImmediately delivered the current slice");

            // Act — repack the same element instances into a brand-new list reference
            _sut.PublicSetState(s => s with { Items = new[] { a, b } });

            // Assert — the comparer bails on identity-equal elements
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void Given_SequenceComparer_When_ElementInstanceChanges_Then_ObserverNotified()
        {
            // Arrange
            var a = new Item(1);
            _sut.PublicSetState(s => s with { Items = new[] { a } });
            var notifications = 0;
            Action<IReadOnlyList<Item>, IReadOnlyList<Item>> observer = (_, __) => notifications++;
            using var _ = _sut.Select(
                s => s.Items,
                observer,
                StoreShallowEqualityComparer.Sequence<Item>(),
                fireImmediately: true);
            Assume.That(notifications, Is.EqualTo(1), "Precondition: fireImmediately delivered the current slice");

            // Act — replace the element with a value-equal but distinct instance
            _sut.PublicSetState(s => s with { Items = new[] { new Item(1) } });

            // Assert
            Assert.That(notifications, Is.EqualTo(2));
        }

        [Test]
        public void Given_PrevSequenceComparer_When_SameElementsRepackedIntoNewList_Then_ObserverNotNotified()
        {
            // Arrange
            var a = new Item(1);
            _sut.PublicSetState(s => s with { Items = new[] { a } });
            var notifications = 0;
            using var _ = _sut.Select(
                s => s.Items,
                (_, _) => notifications++,
                StoreShallowEqualityComparer.Sequence<Item>());

            // Act — repack the same element instance into a new list reference
            _sut.PublicSetState(s => s with { Items = new[] { a } });

            // Assert — the comparer bails on identity-equal elements
            Assert.That(notifications, Is.EqualTo(0));
        }

        [Test]
        public void Given_PrevWithoutComparer_When_SliceReferenceChanges_Then_ObserverNotified()
        {
            // Arrange — the default uses identity equality, so a fresh list with the same elements is a change
            var a = new Item(1);
            _sut.PublicSetState(s => s with { Items = new[] { a } });
            var notifications = 0;
            using var _ = _sut.Select(
                s => s.Items,
                (_, _) => notifications++);

            // Act
            _sut.PublicSetState(s => s with { Items = new[] { a } });

            // Assert
            Assert.That(notifications, Is.EqualTo(1));
        }

        #endregion

        #region Subscribe (fireImmediately)

        [Test]
        public void Given_SubscribeWithoutFireImmediately_When_StateMutated_Then_OnlyMutationDelivered()
        {
            // Arrange
            var notifications = 0;
            using var _ = _sut.Subscribe(_ => notifications++);
            Assume.That(notifications, Is.EqualTo(0), "Precondition: subscribe does not deliver the initial value");

            // Act
            _sut.PublicSetState(s => s with { Value = 1 });

            // Assert
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void Given_SubscribeWithoutFireImmediately_When_Subscribed_Then_InitialValueNotDelivered()
        {
            // Arrange
            var notifications = 0;

            // Act
            using var _ = _sut.Subscribe(_ => notifications++);

            // Assert
            Assert.That(notifications, Is.EqualTo(0));
        }

        [Test]
        public void Given_SubscribeWithFireImmediately_When_Subscribed_Then_FiresOnceSynchronously()
        {
            // Arrange
            var notifications = 0;

            // Act
            using var _ = _sut.Subscribe(_ => notifications++, fireImmediately: true);

            // Assert
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void Given_SubscribeWithFireImmediately_When_Subscribed_Then_DeliversCurrentState()
        {
            // Arrange
            TestState observed = default;

            // Act
            using var _ = _sut.Subscribe(state => observed = state, fireImmediately: true);

            // Assert
            Assert.That(observed, Is.EqualTo(_sut.Current));
        }

        [Test]
        public void Given_NullListener_When_Subscribe_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => _sut.Subscribe((Action<TestState>)null));
        }

        [Test]
        public void Given_NullListener_When_SubscribeWithFireImmediately_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => _sut.Subscribe((Action<TestState>)null, fireImmediately: true));
        }

        [Test]
        public void Given_PrevStateListener_When_StateChanges_Then_FiresOncePerMutation()
        {
            // Arrange
            var notifications = new List<(TestState Cur, TestState Prev)>();
            using var _ = _sut.Subscribe((current, prev) => notifications.Add((current, prev)));

            // Act
            _sut.PublicSetState(s => s with { Value = 1 });
            _sut.PublicSetState(s => s with { Value = 2 });

            // Assert
            Assert.That(notifications, Has.Count.EqualTo(2));
        }

        [Test]
        public void Given_PrevStateListener_When_FirstMutation_Then_PrevIsStateAtSubscribeTime()
        {
            // Arrange
            var notifications = new List<(TestState Cur, TestState Prev)>();
            var initialState = _sut.Current;
            using var _ = _sut.Subscribe((current, prev) => notifications.Add((current, prev)));

            // Act
            _sut.PublicSetState(s => s with { Value = 1 });

            // Assert
            Assert.That(notifications[0].Prev, Is.EqualTo(initialState));
        }

        [Test]
        public void Given_PrevStateListener_When_SecondMutation_Then_PrevIsPriorState()
        {
            // Arrange
            var notifications = new List<(TestState Cur, TestState Prev)>();
            using var _ = _sut.Subscribe((current, prev) => notifications.Add((current, prev)));

            // Act
            _sut.PublicSetState(s => s with { Value = 1 });
            _sut.PublicSetState(s => s with { Value = 2 });

            // Assert
            Assert.That(notifications[1].Prev.Value, Is.EqualTo(1));
        }

        [Test]
        public void Given_PrevStateListener_When_FireImmediately_Then_PrevEqualsCurrent()
        {
            // Arrange
            var notifications = new List<(TestState Cur, TestState Prev)>();
            var initial = _sut.Current;

            // Act
            using var _ = _sut.Subscribe(
                (current, prev) => notifications.Add((current, prev)),
                fireImmediately: true);

            // Assert — at subscribe time there is no prior state, so both arguments are the current state
            Assert.That(notifications[0].Prev, Is.EqualTo(initial));
        }

        [Test]
        public void Given_SelectedSlicePrevListener_When_SliceChanges_Then_DeliversCurrentAndPreviousSlice()
        {
            // Arrange
            var notifications = new List<(int Cur, int Prev)>();
            using var _ = _sut.Select(
                s => s.Value,
                (cur, prev) => notifications.Add((cur, prev)));

            // Act
            _sut.PublicSetState(s => s with { Value = 1 });
            _sut.PublicSetState(s => s with { Value = 2 });

            // Assert
            Assert.That(notifications[1], Is.EqualTo((Cur: 2, Prev: 1)));
        }

        [Test]
        public void Given_SelectedSlicePrevListener_When_SliceValueRepeats_Then_DuplicateSkipped()
        {
            // Arrange
            var notifications = new List<(int Cur, int Prev)>();
            using var _ = _sut.Select(
                s => s.Value,
                (cur, prev) => notifications.Add((cur, prev)));

            // Act — the middle mutation re-sets the same slice value and is suppressed
            _sut.PublicSetState(s => s with { Value = 1 });
            _sut.PublicSetState(s => s with { Value = 1 });
            _sut.PublicSetState(s => s with { Value = 2 });

            // Assert
            Assert.That(notifications, Has.Count.EqualTo(2));
        }

        #endregion

        #region Dispose

        [Test]
        public void Given_Store_When_Disposed_Then_OnDisposeCalledOnce()
        {
            // Arrange
            var store = new TestStore();

            // Act
            store.Dispose();

            // Assert
            Assert.That(store.OnDisposeCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_DisposedStore_When_DisposedAgain_Then_OnDisposeNotCalledTwice()
        {
            // Arrange
            var store = new TestStore();
            store.Dispose();

            // Act
            store.Dispose();

            // Assert
            Assert.That(store.OnDisposeCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_Store_When_Disposed_Then_CancellationTokenIsCancelled()
        {
            // Arrange
            var token = _sut.PublicCancellationToken;

            // Act
            _sut.Dispose();

            // Assert
            Assert.That(token.IsCancellationRequested, Is.True);
        }

        #endregion

        #region Reset

        [Test]
        public void Given_ModifiedState_When_Reset_Then_ValueReturnsToInitial()
        {
            // Arrange
            _sut.PublicSetState(s => s with { Value = 999, Name = "Changed" });
            Assume.That(_sut.Current.Value, Is.EqualTo(999), "Precondition: the state was modified");

            // Act
            _sut.Reset();

            // Assert
            Assert.That(_sut.Current.Value, Is.EqualTo(0));
        }

        [Test]
        public void Given_ModifiedState_When_Reset_Then_NameReturnsToInitial()
        {
            // Arrange
            _sut.PublicSetState(s => s with { Value = 999, Name = "Changed" });
            Assume.That(_sut.Current.Name, Is.EqualTo("Changed"), "Precondition: the state was modified");

            // Act
            _sut.Reset();

            // Assert
            Assert.That(_sut.Current.Name, Is.EqualTo("Initial"));
        }

        #endregion

        #region Test Fixtures

        /// <summary>
        /// Test state record. <c>Items</c> defaults to an empty array so existing callers that
        /// construct <c>new TestState(value, name)</c> remain compatible while shallow-comparer
        /// tests can supply a sequence slice.
        /// </summary>
        public sealed record TestState(
            int Value,
            string Name,
            IReadOnlyList<Item> Items = null)
        {
            public IReadOnlyList<Item> Items { get; init; } = Items ?? Array.Empty<Item>();
        }

        public sealed record Item(int Id);

        /// <summary>
        /// Test Store implementation.
        /// </summary>
        public sealed class TestStore : Store<TestState>
        {
            private static readonly TestState SeedState = new(0, "Initial");

            public int OnDisposeCallCount { get; private set; }

            public TestStore() : base(SeedState)
            {
            }

            public CancellationToken PublicCancellationToken => CancellationToken;

            public bool PublicSetState(Func<TestState, TestState> updater)
                => SetState(updater);

            public void PublicMutate(Func<TestState, TestState> reducer)
                => Mutate(reducer);

            protected override void ResetCore()
            {
                Mutate(_ => SeedState);
            }

            protected override void OnDispose()
            {
                OnDisposeCallCount++;
            }
        }

        #endregion
    }
}
