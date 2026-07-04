using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the commit-phase ordering contract for layout and insertion effects, and the teardown contract
    /// for a mounted tree.
    /// <list type="bullet">
    /// <item>Layout and insertion effects commit bottom-up: a child's effect runs before its parent's, through
    /// any depth of single-child nesting.</item>
    /// <item>Sibling effects commit left-to-right, then the parent — on both the mount commit and the update
    /// commit (parent re-render) path.</item>
    /// <item>Disposing a mounted tree marks the root fiber disposed, so disposed-gated closures short-circuit.</item>
    /// <item>A parent re-render that changes an inline child's props runs the prior setup's cleanup then the new
    /// setup (deps changed) for layout, insertion, and passive effects; a parent re-render that leaves the
    /// child's props unchanged runs neither (deps retained).</item>
    /// <item>Under StrictMode, the mount commit double-invokes an effect (setup, cleanup, setup), while the
    /// update-commit re-expansion runs cleanup then setup exactly once (no double-invoke).</item>
    /// <item>When a mount-staged passive entry and an update-staged entry drain in the same paint-tick under
    /// StrictMode, the drain runs each staged setup once with no spurious double-invoke.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class EffectOrderTests
    {
        private VisualElement _root;
        private static List<string> s_log;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log = new List<string>();
            s_reexpansionChildLayoutSetupCount = 0;
            s_reexpansionChildLayoutCleanupCount = 0;
            s_reexpansionChildInsertionSetupCount = 0;
            s_reexpansionChildInsertionCleanupCount = 0;
            s_reexpansionChildEffectSetupCount = 0;
            s_reexpansionChildEffectCleanupCount = 0;
            s_reexpansionParentSet = null;
            s_reexpansionParentForceReRender = null;
            s_siblingParentSet = null;
            // StrictMode is a global Editor-only toggle; reset it so a sibling fixture's failure can not leak it
            // into this run and silently flip the double-invoke expectations.
            FiberStrictMode.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            // Load-bearing across fixtures: tests below enable StrictMode mid-body, and sibling fixtures do not
            // reset FiberStrictMode.Enabled in their own SetUp, so leaving it true here would double-invoke their
            // renders and produce false failures.
            FiberStrictMode.Enabled = false;
        }

        #region Nested layout / insertion effects (bottom-up)

        [Component]
        private static VNode RenderChildWithLayoutLog()
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("layout:Child"); return (Action)null; }, System.Array.Empty<object>());
            Hooks.UseInsertionEffect(() => { s_log.Add("insertion:Child"); return (Action)null; }, System.Array.Empty<object>());
            return V.Label(name: "child-label", text: "child");
        }

        [Component]
        private static VNode RenderParentWithLayoutLog()
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("layout:Parent"); return (Action)null; }, System.Array.Empty<object>());
            Hooks.UseInsertionEffect(() => { s_log.Add("insertion:Parent"); return (Action)null; }, System.Array.Empty<object>());
            return V.Div(
                name: "parent-div",
                children: new VNode[]
                {
                    V.Component(RenderChildWithLayoutLog, key: "child"),
                });
        }

        [Test]
        public void Given_NestedInlineMount_When_Committed_Then_ChildLayoutEffectRunsBeforeParent()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RenderParentWithLayoutLog, key: "parent"));

            // Assert
            var childIdx = s_log.IndexOf("layout:Child");
            var parentIdx = s_log.IndexOf("layout:Parent");
            Assume.That(childIdx, Is.GreaterThanOrEqualTo(0), "Precondition: both layout effects ran");
            Assert.That(childIdx, Is.LessThan(parentIdx),
                "Layout effects commit bottom-up — the child's runs before the parent's");
        }

        [Test]
        public void Given_NestedInlineMount_When_Committed_Then_ChildInsertionEffectRunsBeforeParent()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RenderParentWithLayoutLog, key: "parent"));

            // Assert
            var childIdx = s_log.IndexOf("insertion:Child");
            var parentIdx = s_log.IndexOf("insertion:Parent");
            Assume.That(childIdx, Is.GreaterThanOrEqualTo(0), "Precondition: both insertion effects ran");
            Assert.That(childIdx, Is.LessThan(parentIdx),
                "Insertion effects commit bottom-up alongside layout effects");
        }

        #endregion

        #region Sibling layout / insertion effects (left-to-right then parent)

        [Component]
        private static VNode RenderSiblingChildA()
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("layout:A"); return (Action)null; }, System.Array.Empty<object>());
            Hooks.UseInsertionEffect(() => { s_log.Add("insertion:A"); return (Action)null; }, System.Array.Empty<object>());
            return V.Label(name: "sibling-a", text: "a");
        }

        [Component]
        private static VNode RenderSiblingChildB()
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("layout:B"); return (Action)null; }, System.Array.Empty<object>());
            Hooks.UseInsertionEffect(() => { s_log.Add("insertion:B"); return (Action)null; }, System.Array.Empty<object>());
            return V.Label(name: "sibling-b", text: "b");
        }

        [Component]
        private static VNode RenderSiblingParent()
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("layout:Parent"); return (Action)null; }, System.Array.Empty<object>());
            Hooks.UseInsertionEffect(() => { s_log.Add("insertion:Parent"); return (Action)null; }, System.Array.Empty<object>());
            return V.Div(
                name: "sibling-parent",
                children: new VNode[]
                {
                    V.Component(RenderSiblingChildA, key: "a"),
                    V.Component(RenderSiblingChildB, key: "b"),
                });
        }

        [Test]
        public void Given_Siblings_When_LayoutEffectsCommit_Then_LeftSiblingRunsBeforeRight()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RenderSiblingParent, key: "parent"));

            // Assert
            var idxA = s_log.IndexOf("layout:A");
            var idxB = s_log.IndexOf("layout:B");
            Assume.That(idxA, Is.GreaterThanOrEqualTo(0), "Precondition: both sibling layout effects ran");
            Assert.That(idxA, Is.LessThan(idxB), "Left sibling A commits before right sibling B");
        }

        [Test]
        public void Given_Siblings_When_LayoutEffectsCommit_Then_BothSiblingsRunBeforeParent()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RenderSiblingParent, key: "parent"));

            // Assert
            var idxB = s_log.IndexOf("layout:B");
            var idxParent = s_log.IndexOf("layout:Parent");
            Assume.That(idxParent, Is.GreaterThanOrEqualTo(0), "Precondition: the parent layout effect ran");
            Assert.That(idxB, Is.LessThan(idxParent), "Both siblings commit before the parent (bottom-up)");
        }

        [Test]
        public void Given_Siblings_When_InsertionEffectsCommit_Then_LeftSiblingRunsBeforeRight()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RenderSiblingParent, key: "parent"));

            // Assert
            var idxA = s_log.IndexOf("insertion:A");
            var idxB = s_log.IndexOf("insertion:B");
            Assume.That(idxA, Is.GreaterThanOrEqualTo(0), "Precondition: both sibling insertion effects ran");
            Assert.That(idxA, Is.LessThan(idxB), "Left sibling A insertion runs before right sibling B");
        }

        [Test]
        public void Given_Siblings_When_InsertionEffectsCommit_Then_BothSiblingsRunBeforeParent()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RenderSiblingParent, key: "parent"));

            // Assert
            var idxB = s_log.IndexOf("insertion:B");
            var idxParent = s_log.IndexOf("insertion:Parent");
            Assume.That(idxParent, Is.GreaterThanOrEqualTo(0), "Precondition: the parent insertion effect ran");
            Assert.That(idxB, Is.LessThan(idxParent), "Both siblings' insertion runs before the parent's");
        }

        #endregion

        #region Deep nesting (single-child chain)

        [Component]
        private static VNode RenderNestedDeepest()
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("layout:Deepest"); return (Action)null; }, System.Array.Empty<object>());
            return V.Label(name: "deepest", text: "deepest");
        }

        [Component]
        private static VNode RenderNestedMid()
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("layout:Mid"); return (Action)null; }, System.Array.Empty<object>());
            return V.Div(name: "mid", children: new VNode[] { V.Component(RenderNestedDeepest, key: "deepest") });
        }

        [Component]
        private static VNode RenderNestedTop()
        {
            Hooks.UseLayoutEffect(() => { s_log.Add("layout:Top"); return (Action)null; }, System.Array.Empty<object>());
            return V.Div(name: "top", children: new VNode[] { V.Component(RenderNestedMid, key: "mid") });
        }

        [Test]
        public void Given_ThreeLevelNesting_When_LayoutEffectsCommit_Then_RunsDeepestToTop()
        {
            // The post-order ancestor reconstruction must walk three fiber.Parent links without losing the chain.
            // Act
            using var mounted = V.Mount(_root, V.Component(RenderNestedTop, key: "top"));

            // Assert
            var idxDeepest = s_log.IndexOf("layout:Deepest");
            var idxMid = s_log.IndexOf("layout:Mid");
            var idxTop = s_log.IndexOf("layout:Top");
            Assume.That(idxTop, Is.GreaterThanOrEqualTo(0), "Precondition: all three layout effects ran");
            Assert.That(idxDeepest < idxMid && idxMid < idxTop, Is.True,
                "Layout effects commit deepest, then mid, then top");
        }

        #endregion

        #region Sibling layout effects on the update-commit path

        private static Action<int> s_siblingParentSet;

        [Component]
        private static VNode RenderSiblingChildAVar(int tick)
        {
            Hooks.UseLayoutEffect(() => { s_log.Add($"layout:A:{tick}"); return (Action)null; }, new object[] { tick });
            return V.Label(name: "sibling-a-var", text: $"a{tick}");
        }

        [Component]
        private static VNode RenderSiblingChildBVar(int tick)
        {
            Hooks.UseLayoutEffect(() => { s_log.Add($"layout:B:{tick}"); return (Action)null; }, new object[] { tick });
            return V.Label(name: "sibling-b-var", text: $"b{tick}");
        }

        [Component]
        private static VNode RenderSiblingParentVar()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_siblingParentSet = setTick;
            return V.Div(
                name: "sibling-parent-var",
                children: new VNode[]
                {
                    V.Component(RenderSiblingChildAVar, tick, key: "a"),
                    V.Component(RenderSiblingChildBVar, tick, key: "b"),
                });
        }

        [Test]
        public void Given_Siblings_When_ParentReRenders_Then_LayoutEffectsCommitLeftToRight()
        {
            // The update-commit path drains effects post-order with the per-fiber IsMount flag false; a left-to-
            // right sibling order must hold here too, not only on the mount path.
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RenderSiblingParentVar, key: "parent"));
            s_log.Clear();

            // Act
            s_siblingParentSet.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            var idxA = s_log.IndexOf("layout:A:1");
            var idxB = s_log.IndexOf("layout:B:1");
            Assume.That(idxA, Is.GreaterThanOrEqualTo(0), "Precondition: both sibling update effects ran");
            Assert.That(idxA, Is.LessThan(idxB),
                "Left sibling A commits before right sibling B on the update-commit path");
        }

        #endregion

        #region Mounted tree teardown

        [Test]
        public void Given_MountedTree_When_Disposed_Then_RootFiberIsMarkedDisposed()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(RenderChildWithLayoutLog, key: "root"));
            Assume.That(mounted.Root.IsDisposed, Is.False, "Precondition: the root fiber starts not-disposed");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(mounted.Root.IsDisposed, Is.True,
                "Dispose takes the FiberRenderer.Dispose path so disposed-gated closures short-circuit");
        }

        #endregion

        #region Re-expansion deps comparison (changed vs unchanged)

        private static int s_reexpansionChildLayoutSetupCount;
        private static int s_reexpansionChildLayoutCleanupCount;
        private static int s_reexpansionChildInsertionSetupCount;
        private static int s_reexpansionChildInsertionCleanupCount;
        private static int s_reexpansionChildEffectSetupCount;
        private static int s_reexpansionChildEffectCleanupCount;
        private static Action<string> s_reexpansionParentSet;
        // A second parent state whose updates do NOT flow into the child's props, used by deps-unchanged tests to
        // force a re-render without bypassing the setter's equality bailout.
        private static Action s_reexpansionParentForceReRender;

        private readonly record struct ReexpansionChildProps(string Token);

        [Component]
        private static VNode ReexpansionChildRender(ReexpansionChildProps p)
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_reexpansionChildLayoutSetupCount++;
                return () => s_reexpansionChildLayoutCleanupCount++;
            }, new object[] { p.Token });
            Hooks.UseInsertionEffect(() =>
            {
                s_reexpansionChildInsertionSetupCount++;
                return () => s_reexpansionChildInsertionCleanupCount++;
            }, new object[] { p.Token });
            Hooks.UseEffect(() =>
            {
                s_reexpansionChildEffectSetupCount++;
                return () => s_reexpansionChildEffectCleanupCount++;
            }, new object[] { p.Token });
            return V.Label(name: "reexpansion-child", text: p.Token);
        }

        [Component]
        private static VNode ReexpansionParentRender()
        {
            var (value, setValue) = Hooks.UseState("a");
            s_reexpansionParentSet = setValue;
            // A second state whose value never leaves the parent: bumping the nonce forces a parent re-render
            // (the setter equality check sees a new int) while the inline child's props stay unchanged.
            var (_, setNonce) = Hooks.UseState(0);
            s_reexpansionParentForceReRender = () => setNonce.Invoke(n => n + 1);
            return V.Div(
                name: "reexpansion-parent",
                children: new VNode[]
                {
                    V.Component(ReexpansionChildRender, new ReexpansionChildProps(value), key: "child"),
                });
        }

        [Test]
        public void Given_InlineChild_When_Mounted_Then_LayoutSetupRunsOnceWithNoCleanup()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));

            // Assert
            Assert.That((s_reexpansionChildLayoutSetupCount, s_reexpansionChildLayoutCleanupCount), Is.EqualTo((1, 0)),
                "The mount commit runs the child layout setup once with no cleanup");
        }

        [Test]
        public void Given_InlineChildProp_When_ParentReRenderChangesIt_Then_LayoutRunsCleanupThenSetup()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));

            // Act
            s_reexpansionParentSet.Invoke("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_reexpansionChildLayoutCleanupCount, s_reexpansionChildLayoutSetupCount), Is.EqualTo((1, 2)),
                "Re-expansion with changed deps runs the prior setup's cleanup then the new setup");
        }

        [Test]
        public void Given_InlineChildProp_When_ParentReRenderChangesIt_Then_InsertionRunsCleanupThenSetup()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));
            Assume.That(s_reexpansionChildInsertionSetupCount, Is.EqualTo(1),
                "Precondition: the mount commit ran the insertion setup once");

            // Act
            s_reexpansionParentSet.Invoke("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_reexpansionChildInsertionCleanupCount, s_reexpansionChildInsertionSetupCount), Is.EqualTo((1, 2)),
                "Re-expansion with changed deps runs the prior insertion cleanup then the new setup");
        }

        [Test]
        public void Given_InlineChildProp_When_ParentReRendersWithUnchangedDeps_Then_LayoutRunsNeitherCleanupNorSetup()
        {
            // The parent re-renders via a separate state so the setter equality bailout does not skip the re-render.
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));
            Assume.That(s_reexpansionChildLayoutSetupCount, Is.EqualTo(1), "Precondition: the mount setup ran once");

            // Act
            s_reexpansionParentForceReRender.Invoke();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_reexpansionChildLayoutSetupCount, s_reexpansionChildLayoutCleanupCount), Is.EqualTo((1, 0)),
                "A re-render whose child deps are unchanged bails the layout effect — no cleanup, no new setup");
        }

        [Test]
        public void Given_DepsChangedThenUnchanged_When_ParentReRenders_Then_OnlyTheChangeReRunsTheLayoutEffect()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));
            s_reexpansionParentSet.Invoke("b");
            mounted.FlushStateForTest();
            Assume.That((s_reexpansionChildLayoutSetupCount, s_reexpansionChildLayoutCleanupCount), Is.EqualTo((2, 1)),
                "Precondition: the a->b deps change re-ran the layout effect");

            // Act — a further re-render with the same child props
            s_reexpansionParentForceReRender.Invoke();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_reexpansionChildLayoutSetupCount, s_reexpansionChildLayoutCleanupCount), Is.EqualTo((2, 1)),
                "A re-render with unchanged child props after a deps change still bails the layout effect");
        }

        [Test]
        public void Given_InlineChildProp_When_ParentReRenderChangesIt_Then_PassiveEffectRunsCleanupThenSetup()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));
            mounted.FlushEffectsForTest();
            Assume.That(s_reexpansionChildEffectSetupCount, Is.EqualTo(1),
                "Precondition: the mount's scheduled passive effect ran setup once after the first paint-tick");

            // Act
            s_reexpansionParentSet.Invoke("b");
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That((s_reexpansionChildEffectCleanupCount, s_reexpansionChildEffectSetupCount), Is.EqualTo((1, 2)),
                "Re-expansion with changed deps schedules the prior passive cleanup then the new setup");
        }

        #endregion

        #region StrictMode double-invoke

        [Test]
        public void Given_StrictMode_When_Mounted_Then_LayoutEffectDoubleInvokesSetupCleanupSetup()
        {
            // Arrange
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));

            // Assert
            Assert.That((s_reexpansionChildLayoutSetupCount, s_reexpansionChildLayoutCleanupCount), Is.EqualTo((2, 1)),
                "A StrictMode mount runs setup, cleanup, setup (setup count 2, cleanup count 1)");
        }

        [Test]
        public void Given_StrictMode_When_ParentReRenderReexpandsChild_Then_LayoutEffectRunsCleanupSetupOnce()
        {
            // The update-commit path carries IsMount=false per fiber, so re-expansion must not double-invoke.
            // Arrange
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));
            // Isolate the update commit's contribution from the mount double-invoke asserted above.
            s_reexpansionChildLayoutSetupCount = 0;
            s_reexpansionChildLayoutCleanupCount = 0;

            // Act
            s_reexpansionParentSet.Invoke("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_reexpansionChildLayoutCleanupCount, s_reexpansionChildLayoutSetupCount), Is.EqualTo((1, 1)),
                "The update commit fires cleanup once and the new setup once — no StrictMode mount double-invoke");
        }

        [Test]
        public void Given_StrictMode_When_MountAndUpdatePassiveEntriesDrainTogether_Then_StagedSetupRunsOnce()
        {
            // The bundle: V.Mount stages a passive effect on the inline child (PendingEffectsAreMount=true). Before
            // the paint-tick fires, the parent re-renders with a new child token; the update-commit's schedule
            // downgrades PendingEffectsAreMount to false, so the bundled drain runs no entry under the mount
            // double-invoke. The prior mount-staged setup was abandoned by the update before paint, so only the
            // final staged setup runs and there is no committed setup to clean.
            // Arrange
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(ReexpansionParentRender, key: "parent"));
            // Intentionally do not flush effects: leave the mount-staged passive entry pending so the next setState
            // bundles its update-staged entry into the same paint-tick.

            // Act
            s_reexpansionParentSet.Invoke("b");
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That((s_reexpansionChildEffectSetupCount, s_reexpansionChildEffectCleanupCount), Is.EqualTo((1, 0)),
                "The bundled drain runs the staged setup once with no cleanup — no spurious StrictMode double-invoke");
        }

        #endregion
    }
}
