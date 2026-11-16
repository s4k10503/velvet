using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins, as one log string per case, the order of a deleted component's <c>UseEffect</c> cleanup against the pool
    /// and the commit around it, in each of the four shapes a deferred cleanup has to survive: a cleanup that writes to
    /// the element its setup captured, while a later mount rents from the shared pool. The cleanup runs in the deletion
    /// walk, so each case reads the captured element unreset and sees it before anything the commit mounts.
    /// </summary>
    [TestFixture]
    internal sealed class DeletionCleanupShapeTests
    {
        private static readonly List<string> s_log = new();
        private static StateUpdater<bool> s_setFirst;
        private static StateUpdater<bool> s_setSecond;
        private static StateUpdater<bool> s_setThird;
        private static StateUpdater<int> s_setStage;

        [SetUp]
        public void SetUp()
        {
            s_log.Clear();
            VNodePoolTestAccess.ClearButtonPoolForTest();
        }

        [TearDown]
        public void TearDown() => VNodePoolTestAccess.ClearButtonPoolForTest();

        // A button whose effect captures it at setup and reads its text at cleanup.
        [Component(Compiler = false)]
        private static VNode Captor(string name)
        {
            var node = Hooks.UseRef<VisualElement>();
            Hooks.UseEffect(() =>
            {
                var captured = (Button)node.Current;
                return (Action)(() => s_log.Add("cleanup:" + name + "=" + captured.text));
            }, Array.Empty<object>());
            return V.Button(text: name + "-text", refCallback: node.SetElement);
        }

        // A button a pass creates, which rents from the pool.
        [Component(Compiler = false)]
        private static VNode Spawn(string name)
            => V.Button(text: name + "-text", refCallback: _ =>
            {
                s_log.Add("mount:" + name);
                return () => { };
            });

        [Component(Compiler = false)]
        private static VNode Plain() => V.Button(text: "plain");

        [Component(Compiler = false)]
        private static VNode ParentOf(string name)
        {
            Hooks.UseEffect(() => (Action)(() => s_log.Add("cleanup:" + name)), Array.Empty<object>());
            return V.Div(children: new VNode[]
            {
                V.Component(Captor, "c1", key: "c1"),
                V.Component(Captor, "c2", key: "c2"),
            });
        }

        [Component(Compiler = false)]
        private static VNode LayoutProbe(string name)
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("lay:" + name);
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div();
        }

        // Shape 1: a later row of the pass rents a button, with no hold between the removal and the rent.
        [Component(Compiler = false)]
        private static VNode RemovalThenRent()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setFirst = setShown;
            return V.Fragment(
                V.Div(key: "l", children: new[] { shown ? V.Component(Captor, "a", key: "a") : null }),
                V.Div(key: "r", children: new[] { shown ? null : V.Component(Spawn, "n", key: "n") }));
        }

        // Shape 2: one container's removal and a second container's, ahead of a third container that mounts.
        [Component(Compiler = false)]
        private static VNode TwoContainersThenRent()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setFirst = setShown;
            return V.Fragment(
                V.Div(key: "x", children: new[] { shown ? V.Component(Captor, "x", key: "x") : null }),
                V.Div(key: "y", children: new[] { shown ? V.Component(Captor, "y", key: "y") : null }),
                V.Div(key: "z", children: new[] { shown ? null : V.Component(Spawn, "z", key: "z") }));
        }

        // Shape 3: three fibers, each with a state of its own, that one drain reconciles one after the other.
        [Component(Compiler = false)]
        private static VNode FirstFiber()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setFirst = setShown;
            return V.Div(children: new[] { shown ? V.Component(Captor, "f1", key: "f1") : null });
        }

        [Component(Compiler = false)]
        private static VNode SecondFiber()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setSecond = setShown;
            return V.Div(children: new[] { shown ? V.Component(Plain, key: "plain") : null });
        }

        [Component(Compiler = false)]
        private static VNode ThirdFiber()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setThird = setShown;
            return V.Div(children: new[] { shown ? V.Component(Spawn, "f3", key: "f3") : null });
        }

        [Component(Compiler = false)]
        private static VNode ThreeFibers()
            => V.Fragment(
                V.Component(FirstFiber, key: "1"),
                V.Component(SecondFiber, key: "2"),
                V.Component(ThirdFiber, key: "3"));

        // Shape 4: two deletions in two commits, the second beside a mount with a layout effect.
        [Component(Compiler = false)]
        private static VNode TwoCommits()
        {
            var (stage, setStage) = Hooks.UseState(0);
            s_setStage = setStage;
            return V.Fragment(
                stage < 1 ? V.Component(Captor, "a", key: "a") : null,
                stage < 2 ? V.Component(ParentOf, "p", key: "p") : null,
                stage >= 2 ? V.Component(LayoutProbe, "n", key: "n") : null);
        }

        // Each step is its own drain, so it is its own commit.
        private static string Mounted(VNode node, params Action[] steps)
        {
            using var mounted = V.Mount(new VisualElement(), node);
            mounted.DrainForTest();
            foreach (var step in steps)
            {
                step();
                mounted.DrainForTest();
            }
            return string.Join(",", s_log);
        }

        // GREEN_ON_BASE(characterization): the base runs the cleanup in the walk, ahead of the rent and the reset.
        [Test]
        public void Given_ADeletedButtonItsEffectCaptured_When_ALaterRowOfThePassRentsAButton_Then_TheCleanupReadsItsOwnText()
        {
            // Arrange / Act
            var log = Mounted(V.Component(RemovalThenRent), () => s_setFirst.Invoke(false));

            // Assert
            Assert.That(log, Is.EqualTo("cleanup:a=a-text,mount:n"));
        }

        // GREEN_ON_BASE(characterization): the base runs each cleanup as its container's walk reaches it.
        [Test]
        public void Given_TwoContainersDeletingCapturedButtons_When_ALaterContainerMountsAButton_Then_BothCleanupsReadTheirOwnTextAndRunFirst()
        {
            // Arrange / Act
            var log = Mounted(V.Component(TwoContainersThenRent), () => s_setFirst.Invoke(false));

            // Assert
            Assert.That(log, Is.EqualTo("cleanup:x=x-text,cleanup:y=y-text,mount:z"));
        }

        // GREEN_ON_BASE(characterization): the base runs each fiber's cleanup in the pass that deletes it.
        [Test]
        public void Given_ThreeFibersOfOneDrain_When_TheFirstDeletesAndTheThirdMounts_Then_TheCleanupRunsBeforeTheMount()
        {
            // Arrange / Act
            var log = Mounted(V.Component(ThreeFibers), () =>
            {
                s_setFirst.Invoke(false);
                s_setSecond.Invoke(false);
                s_setThird.Invoke(true);
            });

            // Assert
            Assert.That(log, Is.EqualTo("cleanup:f1=f1-text,mount:f3"));
        }

        // GREEN_ON_BASE(characterization): the base runs a deletion's cleanups in the walk, pre-order, whichever
        // commit they follow, and ahead of the layout effect the same commit mounts.
        [Test]
        public void Given_TwoCommitsEachDeleting_When_TheSecondAlsoMountsALayoutEffect_Then_TheCleanupsRunInCommitAndPreOrderAheadOfIt()
        {
            // Arrange / Act
            var log = Mounted(V.Component(TwoCommits), () => s_setStage.Invoke(1), () => s_setStage.Invoke(2));

            // Assert
            Assert.That(log, Is.EqualTo("cleanup:a=a-text,cleanup:p,cleanup:c1=c1-text,cleanup:c2=c2-text,lay:n"));
        }
    }
}
