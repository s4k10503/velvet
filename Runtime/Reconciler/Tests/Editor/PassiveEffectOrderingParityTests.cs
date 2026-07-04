using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the ordering contract for passive (UseEffect) effects across a re-render that
    /// dirties both a parent and its child:
    /// <list type="bullet">
    /// <item>All passive cleanups run before any passive setup (tree-wide 2-phase commit).</item>
    /// <item>Child passive effects run before parent passive effects (bottom-up / post-order), for both
    /// cleanup and setup.</item>
    /// </list>
    /// Mirrors <see cref="EffectOrderTests"/> for layout / insertion effects.
    /// </summary>
    [TestFixture]
    internal sealed class PassiveEffectOrderingParityTests
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
            FiberStrictMode.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
        }

        [Component]
        private static VNode RenderPassiveChild(int tick)
        {
            Hooks.UseEffect(() =>
            {
                s_log.Add("setup:Child");
                return (Action)(() => s_log.Add("cleanup:Child"));
            }, new object[] { tick });
            return V.Label(name: "passive-child", text: $"c{tick}");
        }

        [Component]
        private static VNode RenderPassiveParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_parentSet = setTick;
            Hooks.UseEffect(() =>
            {
                s_log.Add("setup:Parent");
                return (Action)(() => s_log.Add("cleanup:Parent"));
            }, new object[] { tick });
            return V.Div(
                name: "passive-parent",
                children: new VNode[]
                {
                    V.Component(RenderPassiveChild, tick, key: "child"),
                });
        }

        [Test]
        public void Given_ParentAndChild_When_BothReRenderWithChangedDeps_Then_AllCleanupsRunBeforeAnySetup()
        {
            // Arrange — mount and settle the first round of passive setups.
            using var mounted = V.Mount(_root, V.Component(RenderPassiveParent, key: "parent"));
            mounted.FlushEffectsForTest();
            Assume.That(s_log, Does.Contain("setup:Child").And.Contain("setup:Parent"),
                "Precondition: the mount commit's passive setups ran");
            s_log.Clear();

            // Act — a re-render that changes the deps of both the parent and the child effect.
            s_parentSet.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — ALL passive cleanups run before ANY passive setup.
            var lastCleanup = Math.Max(s_log.IndexOf("cleanup:Child"), s_log.IndexOf("cleanup:Parent"));
            var firstSetup = Math.Min(IndexOrMax(s_log, "setup:Child"), IndexOrMax(s_log, "setup:Parent"));
            Assume.That(lastCleanup, Is.GreaterThanOrEqualTo(0), "Precondition: both passive cleanups ran");
            Assert.That(lastCleanup, Is.LessThan(firstSetup),
                $"All passive cleanups must run before any passive setup. Order: {string.Join(",", s_log)}");
        }

        [Test]
        public void Given_ParentAndChild_When_BothReRenderWithChangedDeps_Then_ChildSetupRunsBeforeParentSetup()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RenderPassiveParent, key: "parent"));
            mounted.FlushEffectsForTest();
            s_log.Clear();

            // Act
            s_parentSet.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — passive setups commit bottom-up: the child's before the parent's.
            var childSetup = s_log.IndexOf("setup:Child");
            var parentSetup = s_log.IndexOf("setup:Parent");
            Assume.That(childSetup, Is.GreaterThanOrEqualTo(0).And.LessThan(int.MaxValue),
                "Precondition: both passive setups ran");
            Assert.That(childSetup, Is.LessThan(parentSetup),
                $"Child passive setup must run before parent passive setup. Order: {string.Join(",", s_log)}");
        }

        [Test]
        public void Given_ParentAndChild_When_BothReRenderWithChangedDeps_Then_ChildCleanupRunsBeforeParentCleanup()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RenderPassiveParent, key: "parent"));
            mounted.FlushEffectsForTest();
            s_log.Clear();

            // Act
            s_parentSet.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — passive cleanups also commit bottom-up.
            var childCleanup = s_log.IndexOf("cleanup:Child");
            var parentCleanup = s_log.IndexOf("cleanup:Parent");
            Assume.That(childCleanup, Is.GreaterThanOrEqualTo(0), "Precondition: both passive cleanups ran");
            Assert.That(childCleanup, Is.LessThan(parentCleanup),
                $"Child passive cleanup must run before parent passive cleanup. Order: {string.Join(",", s_log)}");
        }

        private static int IndexOrMax(List<string> log, string value)
        {
            var idx = log.IndexOf(value);
            return idx < 0 ? int.MaxValue : idx;
        }
    }
}
