using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for a pooled-element reuse bug: navigating
    /// from a screen with a footer (a V.List of buttons that each hold child labels) to another screen and back
    /// made the footer items visibly duplicate. Root cause: <see cref="Button"/> is the only poolable primitive
    /// whose DSL allows children, and a removed button-with-children was returned to <see cref="VNodePool"/>
    /// WITHOUT detaching its children (<c>CleanupDescendants</c> resource-cleans them but, by design, leaves the
    /// subtree attached for the bulk parent removal). A later <see cref="VNodePool.RentButton"/> then handed back
    /// a button that still carried its old children, and <c>CreateElement</c>'s child reconcile — which assumes an
    /// empty baseline — appended the new children on top, doubling them. The fix detaches a recycled button's
    /// children in <c>FiberButtonPoolHelper.ResetButtonForReuse</c> so a rented button is always childless.
    /// <para>
    /// Each test follows Given/When/Then and asserts exactly one fact, pinning the invariant at three altitudes:
    /// the reset helper, the pool round-trip, and an end-to-end reconcile that recreates buttons from the pool.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class ButtonChildPoolReuseTests
    {
        // Unit: the reset helper

        [Test]
        public void Given_AButtonCarryingAChild_When_ResetForReuse_Then_ItHasNoChildren()
        {
            // Arrange — a button that still holds a child, as a removed button-with-children does on its way
            // to the pool (its descendants are resource-cleaned but not detached).
            var button = new Button();
            button.Add(new Label("stale"));
            Assume.That(button.childCount, Is.EqualTo(1), "Precondition: the button starts with one child");

            // Act — it is reset for reuse.
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert — the child is gone, so the recycled button matches a freshly constructed one.
            Assert.AreEqual(0, button.childCount);
        }

        // Unit: the pool round-trip

        [Test]
        public void Given_AButtonWithAChildWasReturnedToThePool_When_RentedAgain_Then_ItHasNoChildren()
        {
            // Arrange — a button that still holds a child is returned to the pool.
            var returned = new Button();
            returned.Add(new Label("stale"));
            VNodePool.ReturnButton(returned);

            // Act — a button is rented from the pool.
            var rented = VNodePool.RentButton();

            // Assert — it is childless, so a fresh child reconcile cannot append onto leftover content.
            Assert.AreEqual(0, rented.childCount);
        }

        // Integration: recreate buttons-with-children from the pool

        private readonly record struct ToggleState(bool Show);

        private sealed class ToggleStore : Store<ToggleState>
        {
            public ToggleStore() : base(new ToggleState(true)) { }
            public void Set(bool show) => SetState(_ => new ToggleState(show));
            protected override void ResetCore() => SetState(_ => new ToggleState(true));
        }

        private static ToggleStore s_store;
        private static readonly string[] s_items = { "a", "b", "c" };

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        // A footer mirroring the demo: a V.List of buttons that each carry a child label. When hidden it renders
        // no children, so showing it again recreates every button — renting them back from the pool.
        [Component]
        private static VNode Footer()
        {
            var show = Hooks.UseStore(s_store, s => s.Show);
            return V.Div(name: "footer", children: show
                ? V.List(s_items, x => x, x =>
                    V.Button(name: "item-" + x, className: "menu-item", children: new VNode[]
                    {
                        V.Label(name: "label-" + x, text: x),
                    }))
                : Array.Empty<VNode>());
        }

        private static int ChildCountOf(VisualElement root, string name)
        {
            var el = root.Q<VisualElement>(name);
            return el?.childCount ?? -1;
        }

        [Test]
        public void Given_FooterButtonsWereRemovedAndRecreated_When_RentingThemBackFromThePool_Then_ChildrenAreNotDuplicated()
        {
            // Arrange — a mounted footer whose buttons each hold one child, then hidden so every button is removed
            // and returned to the pool with its child still attached.
            using var store = new ToggleStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Footer, key: "footer"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set(false);
            scheduler.DrainImmediateForTest();
            Assume.That(ChildCountOf(_root, "item-a"), Is.EqualTo(-1), "Precondition: the footer buttons are gone while hidden");

            // Act — the footer is shown again, recreating each button by renting it back from the pool.
            store.Set(true);
            scheduler.DrainImmediateForTest();

            // Assert — the recreated button holds exactly its one declared child (not the leftover plus the new one).
            Assert.AreEqual(1, ChildCountOf(_root, "item-a"));
        }

        [Test]
        public void Given_FooterButtonsWereRemovedAndRecreated_When_RentingThemBackFromThePool_Then_NoStaleLabelSurvives()
        {
            // Arrange — the same hide cycle that pools the buttons-with-children.
            using var store = new ToggleStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Footer, key: "footer"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set(false);
            scheduler.DrainImmediateForTest();

            // Act — the footer is shown again.
            store.Set(true);
            scheduler.DrainImmediateForTest();

            // Assert — each label appears exactly once across the whole tree (no duplicated leftover from the pool).
            Assert.AreEqual(1, _root.Query<Label>(name: "label-a").ToList().Count);
        }
    }
}
