using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Hook-lifecycle regression coverage exercised through a REAL discrete click (<see cref="Button.SimulateClick"/>)
    /// rather than a direct setter or a manual drain. A click runs the handler inside the discrete-event boundary
    /// (Urgent lane + a synchronous <c>FlushImmediate</c> at the end), which is the path production actually takes
    /// and the one earlier unit-level tests skipped — the footer-duplication bug only reproduced through it. These
    /// pin: synchronous commit inside the click, fresh hook state on remount at the same key, exactly-once effect
    /// cleanup on click-driven unmount, ordered cleanup-then-setup on a dep change, no leaked/duplicated store
    /// subscription across remount, stable ref identity across an event-driven re-render, and safe self-unmount from
    /// within a component's own handler. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClickDrivenHookLifecycleTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetAll();
        }

        private static void ResetAll()
        {
            s_count = 0;
            s_renderCountChild = 0;
            s_lastChildN = -1;
            s_cleanupCount = 0;
            s_effectLog.Clear();
            s_refValues.Clear();
            s_store = null;
            s_setShow = default;
            s_dep = 0;
        }

        private static int s_count;
        private static int s_renderCountChild;
        private static int s_lastChildN;
        private static int s_cleanupCount;
        private static readonly List<string> s_effectLog = new();
        private static readonly List<object> s_refValues = new();
        private static StateUpdater<bool> s_setShow;
        private static int s_dep;

        // C2: setState inside a real click commits synchronously (no manual drain)

        [Component]
        private static VNode Counter()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Div(name: "counter", children: new VNode[]
            {
                V.Button(name: "inc", onClick: () => setCount.Invoke(c => c + 1)),
                V.Label(name: "out", text: count.ToString()),
            });
        }

        [Test]
        public void Given_AMountedCounter_When_ItsButtonIsClicked_Then_TheLabelReflectsTheNewStateWithoutAManualDrain()
        {
            // Arrange — a mounted counter showing "0".
            using var mounted = V.Mount(_root, V.Component(Counter, key: "counter"));
            Assume.That(_root.Q<Label>("out").text, Is.EqualTo("0"), "Precondition: starts at 0");

            // Act — the increment button is clicked through the real discrete-event path.
            _root.Q<Button>("inc").SimulateClick();

            // Assert — the commit ran synchronously inside the click; the label already shows "1".
            Assert.AreEqual("1", _root.Q<Label>("out").text);
        }

        // C7: UseRef identity is stable across an event-driven re-render

        [Component]
        private static VNode RefHolder()
        {
            var box = Hooks.UseRef(() => new object());
            s_refValues.Add(box.Current);
            var (_, setTick) = Hooks.UseState(0);
            return V.Div(name: "ref-holder", children: new VNode[]
            {
                V.Button(name: "tick", onClick: () => setTick.Invoke(t => t + 1)),
            });
        }

        [Test]
        public void Given_AComponentHoldingAUseRef_When_AClickForcesAReRender_Then_TheRefValueIsTheSameInstance()
        {
            // Arrange — a mounted component that recorded its ref value on the first render.
            using var mounted = V.Mount(_root, V.Component(RefHolder, key: "ref-holder"));
            Assume.That(s_refValues.Count, Is.EqualTo(1), "Precondition: one render recorded");

            // Act — a click forces a re-render that records the ref value again.
            _root.Q<Button>("tick").SimulateClick();

            // Assert — the ref carried the identical instance across the re-render (UseRef is not re-initialised).
            Assert.AreSame(s_refValues[0], s_refValues[1]);
        }

        // Shared parent that shows/hides a child via a click, for remount/unmount cases

        [Component]
        private static VNode TogglingParent()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Button(name: "toggle", onClick: () => setShow.Invoke(s => !s)),
                show ? V.Component(StatefulChild, key: "child") : V.Fragment(Array.Empty<VNode>()),
            });
        }

        [Component]
        private static VNode StatefulChild()
        {
            s_renderCountChild++;
            var (n, setN) = Hooks.UseState(0);
            s_lastChildN = n;
            return V.Div(name: "child", children: new VNode[]
            {
                V.Button(name: "child-inc", onClick: () => setN.Invoke(v => v + 1)),
                V.Label(name: "child-out", text: n.ToString()),
            });
        }

        // C1: remove-then-recreate at the same key starts with fresh hook state

        [Test]
        public void Given_AChildWhoseStateWasAdvanced_When_ItIsRemovedAndRecreatedAtTheSameKey_Then_ItsHookStateIsFresh()
        {
            // Arrange — a child advanced to n=2, then removed by a click.
            using var mounted = V.Mount(_root, V.Component(TogglingParent, key: "parent"));
            _root.Q<Button>("child-inc").SimulateClick();
            _root.Q<Button>("child-inc").SimulateClick();
            Assume.That(_root.Q<Label>("child-out").text, Is.EqualTo("2"), "Precondition: child advanced to 2");
            _root.Q<Button>("toggle").SimulateClick();
            Assume.That(_root.Q<Label>("child-out"), Is.Null, "Precondition: child removed");

            // Act — the child is recreated at the same key.
            _root.Q<Button>("toggle").SimulateClick();

            // Assert — the recreated child starts from its initial state, not the disposed instance's n=2.
            Assert.AreEqual("0", _root.Q<Label>("child-out").text);
        }

        // C5: a component can unmount itself from within its own handler without throwing

        [Test]
        public void Given_AMountedChild_When_AClickHandlerHidesTheComponentRunningIt_Then_TheChildIsRemovedSynchronously()
        {
            // Arrange — a mounted parent+child.
            using var mounted = V.Mount(_root, V.Component(TogglingParent, key: "parent"));
            Assume.That(_root.Q<VisualElement>("child"), Is.Not.Null, "Precondition: child is mounted");

            // Act — the toggle handler flips show=false, unmounting the subtree as the discrete event flushes.
            _root.Q<Button>("toggle").SimulateClick();

            // Assert — the child is gone after the click returns (self-driven unmount commits inside the event).
            Assert.IsNull(_root.Q<VisualElement>("child"));
        }

        // C3: UseEffect cleanup runs exactly once on a click-driven unmount

        [Component]
        private static VNode EffectParent()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(name: "effect-parent", children: new VNode[]
            {
                V.Button(name: "toggle", onClick: () => setShow.Invoke(s => !s)),
                show ? V.Component(EffectChild, key: "ec") : V.Fragment(Array.Empty<VNode>()),
            });
        }

        [Component]
        private static VNode EffectChild()
        {
            Hooks.UseEffect(() => (Action)(() => s_cleanupCount++), Array.Empty<object>());
            return V.Label(name: "effect-child", text: "x");
        }

        [Test]
        public void Given_AMountedEffectChild_When_AClickUnmountsIt_Then_ItsEffectCleanupRunsExactlyOnce()
        {
            // Arrange — a mounted child whose mount-only effect has run its setup.
            using var mounted = V.Mount(_root, V.Component(EffectParent, key: "effect-parent"));
            mounted.FlushEffectsForTest();
            Assume.That(s_cleanupCount, Is.EqualTo(0), "Precondition: cleanup has not run while mounted");

            // Act — a click unmounts the child and effects settle.
            _root.Q<Button>("toggle").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert — the cleanup ran exactly once (not zero, not doubled).
            Assert.AreEqual(1, s_cleanupCount);
        }

        // C6: a dep changed by a click runs cleanup-of-old before setup-of-new, in order

        [Component]
        private static VNode DepEffectComponent()
        {
            var (dep, setDep) = Hooks.UseState(0);
            Hooks.UseEffect(() =>
            {
                s_effectLog.Add($"setup{dep}");
                return (Action)(() => s_effectLog.Add($"cleanup{dep}"));
            }, new object[] { dep });
            return V.Button(name: "bump", onClick: () => setDep.Invoke(d => d + 1));
        }

        [Test]
        public void Given_AnEffectKeyedOnState_When_AClickChangesTheDep_Then_OldCleanupRunsBeforeNewSetup()
        {
            // Arrange — a mounted effect with dep=0 (setup0 has run).
            using var mounted = V.Mount(_root, V.Component(DepEffectComponent, key: "dep"));
            mounted.FlushEffectsForTest();
            Assume.That(s_effectLog, Is.EqualTo(new[] { "setup0" }), "Precondition: only setup0 has run");

            // Act — a click bumps the dep and effects settle.
            _root.Q<Button>("bump").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert — the old cleanup ran before the new setup (ordered re-synchronisation).
            Assert.AreEqual(new[] { "setup0", "cleanup0", "setup1" }, s_effectLog.ToArray());
        }

        // C4: UseStore subscription is released on unmount and not duplicated on remount

        private readonly record struct NState(int N);

        private sealed class NStore : Store<NState>
        {
            public NStore() : base(new NState(0)) { }
            public void Bump() => SetState(s => new NState(s.N + 1));
            protected override void ResetCore() => SetState(_ => new NState(0));
        }

        private static NStore s_store;

        [Component]
        private static VNode StoreParent()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(name: "store-parent", children: new VNode[]
            {
                V.Button(name: "toggle", onClick: () => setShow.Invoke(s => !s)),
                show ? V.Component(StoreChild, key: "sc") : V.Fragment(Array.Empty<VNode>()),
            });
        }

        [Component]
        private static VNode StoreChild()
        {
            var n = Hooks.UseStore(s_store, s => s.N);
            return V.Label(name: "store-child", text: n.ToString());
        }

        [Test]
        public void Given_AChildSubscribedToAStore_When_AClickUnmountsIt_Then_TheStoreSubscriptionIsReleased()
        {
            // Arrange — the store's subscriber baseline, then a child subscribed to it via UseStore.
            using var store = new NStore();
            s_store = store;
            var baseline = StoreSubscriberCount(store);
            using var mounted = V.Mount(_root, V.Component(StoreParent, key: "store-parent"));
            Assume.That(StoreSubscriberCount(store), Is.EqualTo(baseline + 1),
                "Precondition: mounting the child adds exactly one subscriber");

            // Act — a click unmounts the child.
            _root.Q<Button>("toggle").SimulateClick();

            // Assert — the subscription is released synchronously with the unmount (no leak).
            Assert.AreEqual(baseline, StoreSubscriberCount(store));
        }

        [Test]
        public void Given_AStoreChildWasUnmounted_When_ItIsRemountedByAClick_Then_ThereIsExactlyOneSubscription()
        {
            // Arrange — the store baseline and a child unmounted by a click.
            using var store = new NStore();
            s_store = store;
            var baseline = StoreSubscriberCount(store);
            using var mounted = V.Mount(_root, V.Component(StoreParent, key: "store-parent"));
            _root.Q<Button>("toggle").SimulateClick();
            Assume.That(StoreSubscriberCount(store), Is.EqualTo(baseline), "Precondition: subscription released on unmount");

            // Act — the child is remounted by another click.
            _root.Q<Button>("toggle").SimulateClick();

            // Assert — remount installs exactly one fresh subscription (no duplicate from the prior mount).
            Assert.AreEqual(baseline + 1, StoreSubscriberCount(store));
        }

        /// <summary>
        /// Reads the live listener count of a <see cref="Store{TState}"/> by reflecting its private
        /// <c>StoreStateNotifier</c> and that notifier's <c>_listeners</c> list. This is the only way to observe a
        /// leaked or duplicated UseStore subscription, since the count is otherwise encapsulated. Internal layout,
        /// hence reflection.
        /// </summary>
        private static int StoreSubscriberCount<TState>(Store<TState> store)
        {
            var stateField = typeof(Store<TState>).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find Store<T>._state. The internal layout may have changed.");
            var notifier = stateField.GetValue(store)
                ?? throw new InvalidOperationException("Store<T>._state was null.");
            var listenersField = notifier.GetType().GetField("_listeners", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find StoreStateNotifier._listeners. The internal layout may have changed.");
            return ((System.Collections.ICollection)listenersField.GetValue(notifier)).Count;
        }
    }
}
