using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseInsertionEffect"/> in a function component.
    /// <list type="bullet">
    /// <item>The insertion effect runs synchronously at mount, before the layout effect of the same commit.</item>
    /// <item>Its cleanup runs on unmount.</item>
    /// <item>A re-render with unchanged deps skips the effect; a re-render with changed deps re-runs it.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. The mount
    /// double-invoke diagnostic is disabled in <see cref="SetUp"/> so the ordering assertion observes a single
    /// setup pass.
    /// </remarks>
    [TestFixture]
    internal sealed class UseInsertionEffectTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            ResetOrdering();
            ResetDeps();
        }

        [Test]
        public void Given_InsertionAndLayoutEffect_When_Mounted_Then_InsertionRunsSynchronouslyBeforeLayout()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(OrderingRender, key: "insertion-order"));

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "insertion", "layout" }),
                "The insertion effect runs synchronously at mount, before the layout effect");
        }

        [Test]
        public void Given_MountedInsertionEffect_When_Unmounted_Then_CleanupRuns()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(OrderingRender, key: "insertion-cleanup"));
            s_log.Clear();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_log, Does.Contain("insertion-cleanup"), "The insertion effect cleanup runs on unmount");
        }

        [Test]
        public void Given_MountedInsertionEffect_When_ReRenderedWithUnchangedDeps_Then_EffectIsSkipped()
        {
            // Arrange
            s_depsValue = 1;
            using var mounted = V.Mount(_root, V.Component(DepsRender, key: "insertion-deps"));
            Assume.That(s_depsRunCount, Is.EqualTo(1), "Precondition: the effect ran once on mount");

            // Act — re-render with the same dep value
            s_depsForceSetter.Invoke(s_depsForceValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_depsRunCount, Is.EqualTo(1), "Unchanged deps skip the insertion effect re-run");
        }

        [Test]
        public void Given_MountedInsertionEffect_When_ReRenderedWithChangedDeps_Then_EffectReRuns()
        {
            // Arrange
            s_depsValue = 1;
            using var mounted = V.Mount(_root, V.Component(DepsRender, key: "insertion-deps"));
            Assume.That(s_depsRunCount, Is.EqualTo(1), "Precondition: the effect ran once on mount");

            // Act — change the dep value
            s_depsValue = 2;
            s_depsForceSetter.Invoke(s_depsForceValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_depsRunCount, Is.EqualTo(2), "Changed deps re-run the insertion effect");
        }

        #region Ordering component (insertion + layout effect)

        private static readonly List<string> s_log = new();

        private static void ResetOrdering() => s_log.Clear();

        [Component]
        private static VNode OrderingRender()
        {
            Hooks.UseInsertionEffect(() =>
            {
                s_log.Add("insertion");
                return (Action)(() => s_log.Add("insertion-cleanup"));
            }, Array.Empty<object>());
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("layout");
                return (Action)(() => s_log.Add("layout-cleanup"));
            }, Array.Empty<object>());
            return V.Label();
        }

        #endregion

        #region Deps component

        private static int s_depsValue;
        private static int s_depsRunCount;
        private static int s_depsForceValue;
        private static Action<int> s_depsForceSetter;

        private static void ResetDeps()
        {
            s_depsValue = 0;
            s_depsRunCount = 0;
            s_depsForceValue = 0;
            s_depsForceSetter = null;
        }

        [Component]
        private static VNode DepsRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_depsForceValue = tick;
            s_depsForceSetter = setTick;
            Hooks.UseInsertionEffect(() =>
            {
                s_depsRunCount++;
                return (Action)null;
            }, new object[] { s_depsValue });
            return V.Label();
        }

        #endregion
    }
}
