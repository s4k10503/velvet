using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a resource completing while its component is rendering is taken up on a later scheduler pass
    /// of the panel the component is mounted in, which reveals the Suspense content the component suspended.
    /// </summary>
    [TestFixture]
    internal sealed class AsyncResourceCompletionWhileRenderingTests : PanelTestBase
    {
        private static VelvetTaskCompletionSource<string> s_source;
        private static ComponentFiber s_childFiber;

        [Component(Compiler = false)]
        private static VNode HostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(ChildRender, key: "child") });

        [Component(Compiler = false)]
        private static VNode ChildRender()
        {
            s_childFiber ??= Hooks.ResolveUncounted(nameof(ChildRender));
            var data = Hooks.Use(ct => s_source.Task);
            return V.Label(text: data);
        }

        // GREEN_ON_BASE(characterization): the base takes up a resource that completes during its fiber's render on a later pass of the panel's scheduler.
        // Drop the `host.Schedule(...)` statement from the `fiber.IsRendering` branch of NotifyAsyncResourceCompleted
        // and the completion is lost.
        [Test]
        public void Given_AResourceCompletingWhileItsFiberRenders_When_TheRenderEndsAndThePanelTicks_Then_TheSuspenseRevealsItsContent()
        {
            // Arrange
            s_source = new VelvetTaskCompletionSource<string>();
            s_childFiber = null;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(HostRender, key: "host"));
            s_childFiber.IsRendering = true;

            // Act
            s_source.TrySetResult("ready");
            s_childFiber.IsRendering = false;
            EditorPanelTestHelpers.DriveSchedulerOnce(_window.rootVisualElement.panel);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(_window.rootVisualElement.FindFirstLabel()?.text, Is.EqualTo("ready"));
        }
    }
}
