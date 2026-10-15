using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when a ref attaches for a tree that two <c>V.Suspense</c> boundaries hide, the inner one revealing
    /// while the outer one still hides it: not until the outer one reveals.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseNestedRevealRefTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            s_setOuterBlock = default;
            s_setInnerBlock = default;
            s_outerSource = null;
            s_innerSource = null;
            s_refAttaches = 0;
            s_insertionSetups = 0;
        }

        [Test]
        public void Given_AnInnerBoundaryThatHidAndRevealsInsideAnOuterOneStillHiding_When_TheOuterBoundaryReveals_Then_TheRefAttachesOnlyThen()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            Invoke(mounted, s_setOuterBlock);
            Invoke(mounted, s_setInnerBlock);

            // Act
            s_innerSource.TrySetResult(3);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var whileOuterHides = (s_refAttaches, s_insertionSetups, _root.Q<Label>("inner-loading") != null);
            s_outerSource.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((whileOuterHides, s_refAttaches), Is.EqualTo(((1, 1, false), 2)),
                "The inner boundary has revealed while the outer one hides, and the ref attaches with the outer reveal");
        }

        private static void Invoke(MountedTree mounted, StateUpdater<int> set)
        {
            set.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static StateUpdater<int> s_setOuterBlock;
        private static StateUpdater<int> s_setInnerBlock;
        private static VelvetTaskCompletionSource<int> s_outerSource;
        private static VelvetTaskCompletionSource<int> s_innerSource;
        private static int s_refAttaches;
        private static int s_insertionSetups;
        private static readonly Func<VisualElement, Action> s_countingRef = _ =>
        {
            s_refAttaches++;
            return null;
        };

        [Component]
        private static VNode HostRender()
        {
            var (outerBlock, setOuterBlock) = Hooks.UseState(0);
            var (innerBlock, setInnerBlock) = Hooks.UseState(0);
            s_setOuterBlock = setOuterBlock;
            s_setInnerBlock = setInnerBlock;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Component(OuterReaderRender, outerBlock, key: "outer-reader"),
                        V.Component(MiddleRender, innerBlock, key: "middle"),
                    }),
            });
        }

        // Pending for good until s_outerSource resolves, once blocked.
        [Component]
        private static VNode OuterReaderRender(int block)
        {
            var value = Hooks.Use<int>(_ => block > 0
                ? (s_outerSource = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), block);
            return V.Label(text: "outer:" + value);
        }

        // Declares the inner boundary, and an insertion effect that a render with a new block queues.
        [Component]
        private static VNode MiddleRender(int block)
        {
            Hooks.UseInsertionEffect(() =>
            {
                s_insertionSetups++;
                return (Action)null;
            }, new object[] { block });
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(name: "inner-loading", text: "inner-loading"),
                    children: new VNode[]
                    {
                        V.Component(RefdRender, key: "refd"),
                        V.Component(InnerReaderRender, block, key: "inner-reader"),
                    }),
            });
        }

        [Component]
        private static VNode RefdRender()
        {
            return V.Div(name: "refd", refCallback: s_countingRef);
        }

        // Pending until s_innerSource resolves, once blocked.
        [Component]
        private static VNode InnerReaderRender(int block)
        {
            var value = Hooks.Use<int>(_ => block > 0
                ? (s_innerSource = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), block);
            return V.Label(text: "inner:" + value);
        }
    }
}
