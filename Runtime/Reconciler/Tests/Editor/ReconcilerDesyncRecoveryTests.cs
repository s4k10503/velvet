using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Characterizes the ChildReconciler DOM-desync RECOVERY guards: <c>TryRebuildDesyncedSlotRange</c> for keyed
    /// children, and the <c>slotExists</c> / remove-skip guards for indexed children. They recover when the live
    /// container is SHORTER than the fiber's committed baseline claims — the state a completing AnimatePresence
    /// exit leaves when its ghost element is dropped out of band while the baseline still counts it.
    ///
    /// Production reaches that state through a rare real-time presence-ghost overlap that does not reproduce
    /// deterministically in a headless run (neither EditMode nor PlayMode batchmode hits the timing window). So
    /// rather than chase the emergent crash, this pins the guard's CONTRACT directly: the same desync condition
    /// is created deterministically — by dropping a live child element out of band — and a re-render over the
    /// short container must RECOVER (rebuild the missing slots) instead of over-indexing <c>parent.ElementAt</c>.
    /// RED without the guards (the reconcile throws / leaves the container short), GREEN with them.
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerDesyncRecoveryTests
    {
        private VisualElement _root;
        private static Action<int> s_setTick;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setTick = null;
        }

        // Compiler-memo OFF so a state-only re-render always re-runs the child reconcile (auto-memo would bail on
        // unchanged children and skip the very path under test).
        [Component(Compiler = false)]
        private static VNode IndexedHost()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            // Unkeyed children → the indexed reconcile path (slotExists guard).
            return V.Div(name: "box", children: new VNode[]
            {
                V.Label(text: "a"), V.Label(text: "b"), V.Label(text: "c"),
            });
        }

        [Component(Compiler = false)]
        private static VNode KeyedHost()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            // Keyed list → the keyed reconcile path (TryRebuildDesyncedSlotRange).
            return V.Div(name: "box", children: V.List(new[] { "a", "b", "c" }, s => s, s => V.Label(text: s)));
        }

        // Indexed and keyed children reach DIFFERENT recovery guards (slotExists vs TryRebuildDesyncedSlotRange) but
        // run the identical body; the host render selects the path, named per case to keep each Given.
        private static IEnumerable<TestCaseData> DesyncRecoveryCases()
        {
            yield return new TestCaseData("indexed", (Func<VNode>)IndexedHost)
                .SetName("Given_IndexedChildren_When_LiveContainerIsShorterThanBaseline_Then_ReconcileRecoversInsteadOfOverIndexing");
            yield return new TestCaseData("keyed", (Func<VNode>)KeyedHost)
                .SetName("Given_KeyedChildren_When_LiveContainerIsShorterThanBaseline_Then_ReconcileRecoversInsteadOfOverIndexing");
        }

        [TestCaseSource(nameof(DesyncRecoveryCases))]
        public void Given_Children_When_LiveContainerIsShorterThanBaseline_Then_ReconcileRecoversInsteadOfOverIndexing(
            string path, Func<VNode> host)
        {
            // Arrange — three children committed via the indexed or keyed path.
            using var mounted = V.Mount(_root, V.Component(host, key: path));
            var box = _root.Q<VisualElement>("box");
            Assume.That(box.childCount, Is.EqualTo(3), "Precondition: three children committed");
            // Drop the tail element out of band, mirroring a completing exit whose ghost VE was removed while the
            // fiber baseline still counts it: the live container is now SHORTER than the baseline.
            box.RemoveAt(2);
            Assume.That(box.childCount, Is.EqualTo(2), "Precondition: the live container is now short of the baseline");

            // Act — re-render the owner so the reconcile runs against the short container.
            s_setTick.Invoke(1);
            mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();

            // Assert — recovered the full ordered child set (not just the count) rather than over-indexing
            // parent.ElementAt, so a guard that recovered the count via a wrong slot/order would still fail.
            var texts = box.Children().Select(c => (c as Label)?.text).ToList();
            Assert.That(texts, Is.EqualTo(new[] { "a", "b", "c" }),
                "The " + path + " desync guard recovered the ordered child set in order rather than over-indexing parent.ElementAt");
        }
    }
}
