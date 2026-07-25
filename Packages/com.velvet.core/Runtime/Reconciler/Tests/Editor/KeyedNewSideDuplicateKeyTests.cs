using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the keyed diff against a NEW-side duplicate key. The old→(index,node) map lookup never
    /// marked an entry consumed, so a second new sibling with the same key re-resolved the entry the
    /// first occurrence had already claimed: two logical rows aliased one physical element and the
    /// reorder pass collapsed them into a single DOM slot — a row silently vanished and childCount no
    /// longer matched the declared child array. The old-side duplicate guard warns loudly; the
    /// new-side case must do the same and mount a fresh element for the repeated key so every
    /// declared row commits. Covered on both the flat keyed fast path and the general expansion path
    /// (forced by a Fragment sibling), which mirrored the same unguarded lookup.
    /// </summary>
    [TestFixture]
    internal sealed class KeyedNewSideDuplicateKeyTests
    {
        private readonly record struct PhaseState(int Phase);

        private sealed class PhaseStore : Store<PhaseState>
        {
            public PhaseStore() : base(new PhaseState(0)) { }
            public void Set(int phase) => SetState(_ => new PhaseState(phase));
            protected override void ResetCore() => SetState(_ => new PhaseState(0));
        }

        private static PhaseStore s_store;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        [Component]
        private static VNode DupList()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            return V.Div(name: "list", children: phase == 0
                ? new VNode[] { V.Label(key: "x", text: "X") }
                : new VNode[]
                {
                    V.Label(key: "y", text: "Y1"),
                    V.Label(key: "x", text: "DupA"),
                    V.Label(key: "x", text: "DupB"),
                });
        }

        // The Fragment sibling forces the general expansion path instead of the flat keyed fast path.
        [Component]
        private static VNode DupListBesideFragment()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            return V.Div(name: "glist", children: phase == 0
                ? new VNode[]
                {
                    V.Fragment(new VNode?[] { V.Label(key: "f", text: "F") }),
                    V.Label(key: "x", text: "X"),
                }
                : new VNode[]
                {
                    V.Fragment(new VNode?[] { V.Label(key: "f", text: "F") }),
                    V.Label(key: "y", text: "Y1"),
                    V.Label(key: "x", text: "DupA"),
                    V.Label(key: "x", text: "DupB"),
                });
        }

        private static string[] LabelTextsOf(VisualElement root, string containerName)
        {
            var container = root.Q<VisualElement>(containerName);
            var texts = new string[container.childCount];
            for (var i = 0; i < container.childCount; i++)
            {
                texts[i] = ((Label)container.ElementAt(i)).text;
            }
            return texts;
        }

        [Test]
        public void Given_ANewSideDuplicateKey_When_Reconciled_Then_EveryDeclaredRowCommits()
        {
            // Arrange — one keyed row, then a new tree whose tail repeats a key.
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(DupList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<VisualElement>("list").childCount, Is.EqualTo(1),
                "Precondition: the single keyed row is mounted");

            // Act
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — three declared rows produce three committed children; none is silently dropped.
            Assert.AreEqual(3, _root.Q<VisualElement>("list").childCount);
        }

        [Test]
        public void Given_ANewSideDuplicateKey_When_Reconciled_Then_RowsCommitInDeclaredOrder()
        {
            // Arrange
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(DupList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;

            // Act
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the first occurrence keeps the matched element, the repeat mounts fresh after it.
            Assert.That(LabelTextsOf(_root, "list"), Is.EqualTo(new[] { "Y1", "DupA", "DupB" }));
        }

        [Test]
        public void Given_ANewSideDuplicateKey_When_Reconciled_Then_ItWarnsLikeTheOldSideGuard()
        {
            // Arrange
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(DupList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Duplicate key detected among new siblings"));

            // Act
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — every row still committed; the new-side warning itself is enforced by
            // LogAssert at test end (an Assert.Pass would bypass that unmatched-expectation check),
            // and its message is distinct from the old-side guard's so a later unmount diff cannot
            // satisfy it.
            Assert.AreEqual(3, _root.Q<VisualElement>("list").childCount);
        }

        [Test]
        public void Given_ANewSideDuplicateKeyOnTheGeneralPath_When_Reconciled_Then_EveryDeclaredRowCommits()
        {
            // Arrange — a Fragment sibling routes the reconcile through the general expansion path.
            using var store = new PhaseStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(DupListBesideFragment, key: "glist"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<VisualElement>("glist").childCount, Is.EqualTo(2),
                "Precondition: the fragment row and the keyed row are mounted");

            // Act
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the fragment row plus three declared rows; the duplicate is not dropped.
            Assert.AreEqual(4, _root.Q<VisualElement>("glist").childCount);
        }
    }

    /// <summary>
    /// "Preserving and resetting state" semantics for the KEYED-SIBLING reconcile path:
    /// component state is tied to its position in
    /// the tree, identified by element type + key. The existing suites cover the neighbouring cases — host
    /// instance reuse / DOM moves on reorder (host elements only), and single-component same-key preserve /
    /// key-change reset / type-change reset — but never the two seams pinned here, both of which sit on top of
    /// keyed component reconciliation:
    /// <list type="bullet">
    /// <item>A REORDER of stateful keyed components reuses each key's fiber and carries its hook state to the
    /// new position (only the order changes; no key's count is lost or swapped onto another key).</item>
    /// <item>When one of several keyed siblings has its key changed while the others are left in place, only the
    /// re-keyed sibling unmounts and remounts fresh; the key-stable siblings keep their state.</item>
    /// </list>
    /// All are driven through a real discrete click (<see cref="Button.SimulateClick"/>), which commits
    /// synchronously, so no manual drain is needed. GWT, one assert per case. These record the expected behaviour as
    /// the expected value, so if either turns RED it is a Velvet divergence (state leaking across a key on
    /// reorder, or a key-stable sibling getting reset alongside a re-keyed neighbour).
    /// </summary>
    [TestFixture]
    internal sealed class KeyedComponentStateParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        // A stateful child whose props carry a stable display name, so each keyed instance exposes a
        // uniquely-named button/label that survives reordering (the name follows the fiber, the key follows the
        // identity). Its own UseState count lives on its ComponentFiber.

        private sealed record ChildProps(string Name);

        [Component]
        private static VNode NamedCounter(ChildProps props)
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Div(children: new VNode[]
            {
                V.Button(name: $"inc-{props.Name}", onClick: () => setCount.Invoke(c => c + 1)),
                V.Label(name: $"out-{props.Name}", text: count.ToString()),
            });
        }

        // --- Gap 1: a reorder reuses each key's fiber and keeps its hook state, moving only the position ---

        [Component]
        private static VNode ReorderParent()
        {
            var (reordered, setReordered) = Hooks.UseState(false);
            var a = V.Component(NamedCounter, new ChildProps("a"), key: "a");
            var b = V.Component(NamedCounter, new ChildProps("b"), key: "b");
            var c = V.Component(NamedCounter, new ChildProps("c"), key: "c");
            return V.Div(children: new VNode[]
            {
                V.Button(name: "reorder", onClick: () => setReordered.Invoke(_ => true)),
                reordered
                    ? V.Fragment(new VNode[] { c, a, b })
                    : V.Fragment(new VNode[] { a, b, c }),
            });
        }

        [Test]
        public void Given_KeyedStatefulComponents_When_Reordered_Then_EachKeepsItsOwnHookState()
        {
            // Arrange — three keyed counters advanced independently to a=3, b=7, c=0.
            using var mounted = V.Mount(_root, V.Component(ReorderParent, key: "reorder-parent"));
            for (var i = 0; i < 3; i++) _root.Q<Button>("inc-a").SimulateClick();
            for (var i = 0; i < 7; i++) _root.Q<Button>("inc-b").SimulateClick();
            Assume.That(
                (_root.Q<Label>("out-a").text, _root.Q<Label>("out-b").text, _root.Q<Label>("out-c").text),
                Is.EqualTo(("3", "7", "0")),
                "Precondition: each counter advanced independently before reordering");

            // Act — the parent re-renders the siblings in a new order [c, a, b].
            _root.Q<Button>("reorder").SimulateClick();

            // Assert — each key reused its fiber, so its count follows it to the new position (none lost or swapped).
            Assert.That(
                (_root.Q<Label>("out-a").text, _root.Q<Label>("out-b").text, _root.Q<Label>("out-c").text),
                Is.EqualTo(("3", "7", "0")),
                "A reorder carries each keyed component's hook state to its new position.");
        }

        // --- Gap 2: re-keying one of several siblings remounts only that one; key-stable siblings persist ---

        [Component]
        private static VNode SelectiveReKeyParent()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            var first = V.Component(NamedCounter, new ChildProps("a"), key: "a");
            var second = swapped
                ? V.Component(NamedCounter, new ChildProps("z"), key: "z")
                : V.Component(NamedCounter, new ChildProps("b"), key: "b");
            return V.Div(children: new VNode[]
            {
                V.Button(name: "rekey", onClick: () => setSwapped.Invoke(_ => true)),
                V.Fragment(new VNode[] { first, second }),
            });
        }

        [Test]
        public void Given_TwoKeyedSiblings_When_OneKeyChanges_Then_OnlyThatSiblingRemountsFresh()
        {
            // Arrange — two keyed siblings advanced to a=5, b=9.
            using var mounted = V.Mount(_root, V.Component(SelectiveReKeyParent, key: "rekey-parent"));
            for (var i = 0; i < 5; i++) _root.Q<Button>("inc-a").SimulateClick();
            for (var i = 0; i < 9; i++) _root.Q<Button>("inc-b").SimulateClick();
            Assume.That(
                (_root.Q<Label>("out-a").text, _root.Q<Label>("out-b").text),
                Is.EqualTo(("5", "9")),
                "Precondition: both siblings advanced before the re-key");

            // Act — only the second sibling's key changes (b -> z); the first sibling's key (a) is left in place.
            _root.Q<Button>("rekey").SimulateClick();

            // Assert — the key-stable sibling keeps its state (a=5) while only the re-keyed one remounts fresh (z=0).
            Assert.That(
                (_root.Q<Label>("out-a").text, _root.Q<Label>("out-z").text),
                Is.EqualTo(("5", "0")),
                "Re-keying one sibling resets only that sibling; key-stable siblings preserve their state.");
        }
    }

    /// <summary>
    /// Pins React DOM parity for panel focus across keyed reorders — a parity the ENGINE itself
    /// provides and Velvet must not break: the placement walk moves a non-anchor element with
    /// RemoveAt + Insert inside one flush, and UI Toolkit's focus bookkeeping validates the focused
    /// element lazily, so an element re-inserted within the same frame keeps panel focus (and
    /// receives no Blur), exactly like a DOM node moved with <c>insertBefore</c>. These specs exist
    /// because the opposite was plausible enough to almost ship a re-focus workaround: any future
    /// placement change that detaches across a frame boundary (or an engine behavior change) must
    /// surface here, not as gamepad users silently losing their place in reordered lists.
    /// </summary>
    [TestFixture]
    internal sealed class KeyedReorderFocusTests
    {
        private sealed class KeysStore : Store<string>
        {
            public KeysStore() : base("abc") { }
            public void Set(string keys) => SetState(_ => keys);
            protected override void ResetCore() => SetState(_ => "abc");
        }

        private static KeysStore s_store;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_store = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Component]
        private static VNode ReorderList()
        {
            var keys = Hooks.UseStore(s_store, x => x);
            var children = new System.Collections.Generic.List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Button(name: "item-" + key, key: key.ToString(), text: key.ToString()));
            }
            return V.Div(name: "list", children: children.ToArray());
        }

        // "abc" -> "cba" leaves a single-element longest increasing subsequence, so the focused
        // middle item is NOT an anchor and the walk physically moves it (RemoveAt + Insert).

        [Test]
        public void Given_AFocusedKeyedItem_When_AReorderMovesIt_Then_ItKeepsPanelFocus()
        {
            // Arrange — real panel focus on the middle item.
            using var store = new KeysStore();
            s_store = store;
            _mounted = V.Mount(_host.Root, V.Component(ReorderList, key: "list"));
            var item = _host.Root.Q<VisualElement>("item-b");
            item.Focus();
            _mounted.FlushStateForTest();
            Assume.That(_host.Panel.focusController.focusedElement, Is.SameAs(item),
                "Precondition: the middle item holds panel focus");

            // Act — reverse the list; the focused item is moved by the placement walk.
            store.Set("cba");
            _mounted.FlushStateForTest();

            // Assert — the same element instance still holds panel focus after its move.
            Assert.That(_host.Panel.focusController.focusedElement, Is.SameAs(item),
                "A keyed reorder that moves the focused element must not drop panel focus");
        }

        [Test]
        public void Given_AFocusedKeyedItem_When_AReorderMovesIt_Then_NoBlurEdgeReachesIt()
        {
            // Arrange — hook consumers (focus rings, whileFocus styling) are edge-driven, so a
            // same-frame move must ALSO stay silent on the event channel: a spurious Blur would
            // flicker every focus-derived state even though panel focus survives.
            using var store = new KeysStore();
            s_store = store;
            _mounted = V.Mount(_host.Root, V.Component(ReorderList, key: "list"));
            var item = _host.Root.Q<VisualElement>("item-b");
            var blurCount = 0;
            item.RegisterCallback<BlurEvent>(_ => blurCount++);
            item.Focus();
            _mounted.FlushStateForTest();
            Assume.That(_host.Panel.focusController.focusedElement, Is.SameAs(item),
                "Precondition: the middle item holds panel focus");

            // Act
            store.Set("cba");
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(blurCount, Is.EqualTo(0),
                "A same-frame move must not deliver a Blur to the moved element");
        }

        [Test]
        public void Given_AFocusedKeyedItem_When_AReorderMovesIt_Then_ItsDomPositionFollowsTheNewOrder()
        {
            // Arrange — focus survival must not come at the cost of the reorder's outcome itself.
            using var store = new KeysStore();
            s_store = store;
            _mounted = V.Mount(_host.Root, V.Component(ReorderList, key: "list"));
            _host.Root.Q<VisualElement>("item-b").Focus();
            _mounted.FlushStateForTest();

            // Act
            store.Set("cba");
            _mounted.FlushStateForTest();

            // Assert — the list committed the reversed order.
            var list = _host.Root.Q<VisualElement>("list");
            Assert.That(
                (list.ElementAt(0).name, list.ElementAt(1).name, list.ElementAt(2).name),
                Is.EqualTo(("item-c", "item-b", "item-a")),
                "The reorder commits the new order with the focused element moved in place");
        }
    }
}
