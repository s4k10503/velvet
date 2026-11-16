using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a label's text patch, recorded behind the pass's writes, is not written onto the label once a
    /// boundary caught later in the same pass has cleaned and pooled it.
    /// </summary>
    [TestFixture]
    internal sealed class StagedTextPatchRollbackTests
    {
        private static bool s_throws;
        private static Action<int> s_setTick;

        [SetUp]
        public void SetUp()
        {
            s_throws = false;
            s_setTick = null;
            FiberStrictMode.Enabled = false;
        }

        [Component(Compiler = false)]
        private static VNode ThrowerRender(int tick)
        {
            if (s_throws) throw new InvalidOperationException("thrown");
            return V.Div();
        }

        [Component(Compiler = false)]
        private static VNode HostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.ErrorBoundary(
                    fallbackRender: (error, reset) => V.Div(name: "fallback"),
                    children: new VNode[]
                    {
                        V.Div(children: new VNode[]
                        {
                            V.Div(children: new VNode[] { V.Text("tick " + tick) }),
                            V.Div(children: new VNode[] { V.Component(ThrowerRender, tick, key: "thrower") }),
                        }),
                    },
                    key: "boundary"),
            });
        }

        // GREEN_ON_BASE(characterization): the label sits in a Div of its own, patched before the next Div expands the
        // component that throws. The base writes the text at the patch and the boundary then cleans the label, so no
        // table entry outlives it; the case pins that the text write recorded for that label leaves none either.
        [Test]
        public void Given_ALabelPatchedBeforeABoundaryCatchesInTheSamePass_When_ThePassEnds_Then_NoFlexMinManipulatorIsLeftOnAnElementOutsideTheTree()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);

            // Act
            s_throws = true;
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            var stray = mounted.Root.Reconciler.Context.FlexMinSizeManipulators.Keys.Count(element => !root.Contains(element));
            Assert.That(stray, Is.Zero);
        }
    }
}
