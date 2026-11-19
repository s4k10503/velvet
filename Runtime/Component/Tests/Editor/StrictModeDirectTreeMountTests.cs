using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the StrictMode double-invoke diagnostic against a directly-built mount tree. V.Mount
    /// documents plain element trees ("V.Provider, V.Component, V.Div, etc."), wiring the root
    /// fiber's Body to a constant passthrough closure — a second invocation returns the SAME node
    /// graph the commit owns. A constant Body has no render purity to validate; real coverage comes
    /// from the nested component fibers, which each run their own diagnostic.
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
    }
}
