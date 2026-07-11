using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the StrictMode double-invoke diagnostic against a directly-built mount tree. V.Mount
    /// documents plain element trees ("V.Provider, V.Component, V.Div, etc."), wiring the root
    /// fiber's Body to a constant passthrough closure — a second invocation returns the SAME node
    /// graph the commit owns. The diagnostic recycled its second pass's output unconditionally, so
    /// it wiped the committed tree's props/events in place and pushed them into the shared pools
    /// where any unrelated mount could rent and overwrite them — exactly the state-ghosting class
    /// the diagnostic exists to catch. A constant Body has no render purity to validate; real
    /// coverage comes from the nested component fibers, which each run their own diagnostic.
    /// </summary>
    [TestFixture]
    internal sealed class StrictModeDirectTreeMountTests
    {
        private VisualElement _root;
        private bool _strictModeBefore;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _strictModeBefore = FiberStrictMode.Enabled;
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = _strictModeBefore;
        }

        [Test]
        public void Given_StrictMode_When_ADirectlyBuiltTreeIsMounted_Then_TheCommittedTreeKeepsItsPropsAndEvents()
        {
            // Arrange
            FiberStrictMode.Enabled = true;

            // Act — mount a plain element tree (no component wrapper), the documented pattern.
            using var mounted = V.Mount(_root, V.Div(name: "app", children: new VNode[]
            {
                V.Button(name: "b", text: "Click me", onClick: () => { }),
            }));

            // Assert — the committed VNode tree still carries its text and click binding.
            var app = (ElementNode)mounted.Root.PreviousTree[0];
            var button = (ElementNode)app.Children[0];
            Assert.That((button.Props?.Text, button.Events != null && button.Events[0] != null),
                Is.EqualTo(("Click me", true)));
        }

        [Test]
        public void Given_StrictMode_When_ADirectlyBuiltTreeIsMounted_Then_ItsPropsDoNotLeakIntoThePool()
        {
            // Arrange
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Div(name: "app", children: new VNode[]
            {
                V.Button(name: "b", text: "Click me", onClick: () => { }),
            }));
            var committedProps = ((ElementNode)((ElementNode)mounted.Root.PreviousTree[0]).Children[0]).Props;

            // Act — an unrelated factory call rents from the shared pool.
            var rented = VNodePool.RentProps();

            // Assert — the committed tree's live props bag was never recycled into the pool.
            Assert.That(ReferenceEquals(rented, committedProps), Is.False);
        }
    }
}
