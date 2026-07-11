using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins DOM order while a non-last AnimatePresence child exits. The boundary's committed order
    /// was assembled in two unconditional passes — every current child first, every still-exiting
    /// ghost appended after — so the instant a middle item started exiting it was yanked out of its
    /// visual slot and dropped behind every later sibling: in a flex parent the layout visibly
    /// jumped before the fade even began, and the committed set's own "in DOM order" invariant was
    /// violated for the whole exit. The oracle re-inserts each exiting child at the position it held
    /// among its previous siblings, so an in-place exit stays in place.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceExitOrderTests
    {
        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore() : base(new SetState("abc")) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("abc"));
        }

        private static SetStore s_store;
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        [Component]
        private static VNode PresenceList()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_fade, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "presence-host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        private static string OrderOf(VisualElement host)
        {
            var order = "";
            for (var i = 0; i < host.childCount; i++)
            {
                var name = host.ElementAt(i).name;
                order += name.Substring(name.Length - 1);
            }
            return order;
        }

        [Test]
        public void Given_ThreeKeyedChildren_When_TheMiddleOneExits_Then_ItStaysInItsSlotWhileExiting()
        {
            // Arrange — [a,b,c] settled, then b (non-last) is removed with a real exit transition.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PresenceList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var host = _root.Q<VisualElement>("presence-host");
            Assume.That(OrderOf(host), Is.EqualTo("abc"), "Precondition: three children in declared order");

            // Act — remove the middle child; its exit keeps it mounted as a ghost.
            store.Set("ac");
            scheduler.DrainImmediateForTest();

            // Assert — the exiting ghost holds its old slot between a and c, not the tail.
            Assert.AreEqual("abc", OrderOf(host));
        }

        [Test]
        public void Given_TwoMiddleChildrenExit_When_TheGhostsRemain_Then_TheWholeOldOrderIsPreserved()
        {
            // Arrange — [a,b,c,d] settled (via an initial add), then both middles are removed.
            using var store = new SetStore();
            s_store = store;
            store.Set("abcd");
            using var mounted = V.Mount(_root, V.Component(PresenceList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var host = _root.Q<VisualElement>("presence-host");
            Assume.That(OrderOf(host), Is.EqualTo("abcd"), "Precondition: four children in declared order");

            // Act
            store.Set("ad");
            scheduler.DrainImmediateForTest();

            // Assert — both ghosts hold their old relative slots.
            Assert.AreEqual("abcd", OrderOf(host));
        }
    }
}
