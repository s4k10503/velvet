using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins React's all-cleanups-before-all-setups for inline-effect commits: across the parent/inline-child
    /// boundary (a parent cleanup that reads state an inline child's setup writes observes the pre-update value)
    /// and across a batch of inline siblings (every sibling's cleanup precedes any sibling's setup). Committing
    /// a fiber fully — its setup included — before a later fiber's cleanup ran would invert the pair.
    /// </summary>
    [TestFixture]
    internal sealed class InlineChildLayoutEffectTwoPhaseTests
    {
        private VisualElement _root;
        private static List<string> s_log;
        private static Action<int> s_parentSet;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log = new List<string>();
            s_parentSet = null;
        }

        // Inline child: keyed on the tick prop so its layout effect re-runs when the parent re-renders. The
        // setup logs; a null cleanup keeps the cleanup pass silent so only the parent's cleanup is recorded.
        [Component]
        private static VNode Child(int tick)
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("child:setup"); return (Action)null; }, new object[] { tick });
            return V.Label(name: "child", text: $"c{tick}");
        }

        // Parent: owns the tick state, passes it to the inline child, and has a layout effect whose CLEANUP
        // logs. The parent's cleanup must land before the child's setup on the re-render commit.
        [Component]
        private static VNode Parent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_parentSet = setTick;
            Hooks.UseLayoutEffect(
                () => (Action)(() => s_log.Add("parent:cleanup")),
                new object[] { tick });
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Component(Child, tick, key: "child"),
            });
        }

        [Test]
        public void Given_ParentAndInlineChildLayoutEffectsRerun_When_Committed_Then_ParentCleanupPrecedesChildSetup()
        {
            // Arrange — mount, then discard the mount-phase log so only the update commit is measured.
            using var mounted = V.Mount(_root, V.Component(Parent, key: "parent"));
            s_log.Clear();

            // Act — the parent re-renders and re-expands the child inline; both layout effects re-run.
            s_parentSet.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — parent cleanup (mutation phase) runs before child setup (layout phase).
            Assume.That(s_log, Does.Contain("parent:cleanup").And.Contain("child:setup"),
                "Precondition: both the parent cleanup and the child setup ran on this commit");
            Assert.That(s_log.IndexOf("parent:cleanup"), Is.LessThan(s_log.IndexOf("child:setup")),
                "React commits all layout cleanups before all layout setups across the parent/inline-child boundary");
        }

        private static Action<int> s_siblingSet;

        // Inline sibling: logs both its cleanup and setup, keyed on the tick prop so both re-run on the parent's
        // re-render. Two siblings together pin that every sibling's cleanup precedes any sibling's setup.
        [Component]
        private static VNode SiblingA(int tick)
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("a:setup");
                return (Action)(() => s_log.Add("a:cleanup"));
            }, new object[] { tick });
            return V.Label(name: "a", text: $"a{tick}");
        }

        [Component]
        private static VNode SiblingB(int tick)
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("b:setup");
                return (Action)(() => s_log.Add("b:cleanup"));
            }, new object[] { tick });
            return V.Label(name: "b", text: $"b{tick}");
        }

        [Component]
        private static VNode SiblingParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_siblingSet = setTick;
            return V.Div(name: "sibling-parent", children: new VNode[]
            {
                V.Component(SiblingA, tick, key: "a"),
                V.Component(SiblingB, tick, key: "b"),
            });
        }

        [Test]
        public void Given_TwoInlineSiblingsLayoutEffectsRerun_When_Committed_Then_AllCleanupsPrecedeAllSetups()
        {
            // Arrange — mount, then discard the mount-phase log (setups only) so only the update commit counts.
            using var mounted = V.Mount(_root, V.Component(SiblingParent, key: "sibling-parent"));
            s_log.Clear();

            // Act — the parent re-renders and re-expands both siblings inline; every layout effect re-runs.
            s_siblingSet.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the last cleanup across the batch runs before the first setup (all cleanups, then all setups).
            var lastCleanup = Math.Max(s_log.IndexOf("a:cleanup"), s_log.IndexOf("b:cleanup"));
            var firstSetup = Math.Min(s_log.IndexOf("a:setup"), s_log.IndexOf("b:setup"));
            Assume.That(s_log, Does.Contain("a:cleanup").And.Contain("b:setup"),
                "Precondition: both siblings' cleanup and setup ran on this commit");
            Assert.That(lastCleanup, Is.LessThan(firstSetup),
                "Every inline sibling's layout cleanup commits before any sibling's layout setup (React mutation-before-layout)");
        }
    }
}
