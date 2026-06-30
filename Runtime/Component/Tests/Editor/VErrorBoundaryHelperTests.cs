using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <see cref="V.ErrorBoundary"/> helper, which wraps children in an error boundary inline
    /// without a dedicated <c>[Component(IsErrorBoundary = true)]</c> wrapper.
    /// <list type="bullet">
    /// <item>On a normal mount the children render and the fallback factory is not called.</item>
    /// <item>When a child throws during render, the boundary catches the exception, invokes the fallback
    /// factory with that exception, renders the returned fallback VNode, and the normal child is not shown.</item>
    /// <item>A render throw in the first of several children fires the fallback (healthy siblings are not
    /// guaranteed to unmount on a synchronous throw inside the wrapping Fragment).</item>
    /// <item>A null fallback or null children is rejected with <see cref="ArgumentNullException"/>.</item>
    /// <item>A Provider value placed outside the boundary propagates to a child consumer inside it via
    /// <c>Hooks.UseContext</c>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VErrorBoundaryHelperTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_throwOnRender = false;
            s_fallbackInvoked = false;
            s_fallbackException = null;
        }

        [Test]
        public void Given_NoChildException_When_Mounted_Then_RendersChildren()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.ErrorBoundary(
                    fallback: BuildFallback,
                    children: new VNode[] { V.Component(NormalChildRender, key: "child") }));

            // Assert
            Assert.That(_root.Q<Label>(name: "ok-label"), Is.Not.Null, "The children are rendered");
        }

        [Test]
        public void Given_NoChildException_When_Mounted_Then_FallbackFactoryIsNotCalled()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.ErrorBoundary(
                    fallback: BuildFallback,
                    children: new VNode[] { V.Component(NormalChildRender, key: "child") }));

            // Assert
            Assert.That(s_fallbackInvoked, Is.False);
        }

        [Test]
        public void Given_ChildThrowsOnRender_When_WrappedByHelper_Then_FallbackUIReplacesChild()
        {
            // Arrange
            s_throwOnRender = true;

            // Act
            using var mounted = V.Mount(_root,
                V.ErrorBoundary(
                    fallback: BuildFallback,
                    children: new VNode[] { V.Component(ThrowingChildRender, key: "throw") }));

            // Assert
            Assert.That((_root.Q<Label>(name: "fallback-label") != null, _root.Q<Label>(name: "ok-label") != null),
                Is.EqualTo((true, false)),
                "The fallback VNode is rendered and the normal child is not shown");
        }

        [Test]
        public void Given_ChildThrowsOnRender_When_WrappedByHelper_Then_FallbackReceivesTheException()
        {
            // Arrange
            s_throwOnRender = true;

            // Act
            using var mounted = V.Mount(_root,
                V.ErrorBoundary(
                    fallback: BuildFallback,
                    children: new VNode[] { V.Component(ThrowingChildRender, key: "throw") }));
            Assume.That(s_fallbackInvoked, Is.True, "Precondition: the fallback factory ran");

            // Assert
            Assert.That(s_fallbackException, Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Given_MultipleChildren_When_FirstThrows_Then_FallbackUIIsRendered()
        {
            // Arrange
            s_throwOnRender = true;

            // Act
            using var mounted = V.Mount(_root,
                V.ErrorBoundary(
                    fallback: BuildFallback,
                    children: new VNode[]
                    {
                        V.Component(ThrowingChildRender, key: "throw"),
                        V.Component(NormalChildRender, key: "normal"),
                    }));

            // Assert
            Assert.That(_root.Q<Label>(name: "fallback-label"), Is.Not.Null,
                "A render throw in the first child fires the boundary fallback");
        }

        [Test]
        public void Given_NullFallback_When_ErrorBoundary_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.ErrorBoundary(fallback: null, children: new VNode[] { V.Label(text: "x") }));
        }

        [Test]
        public void Given_NullChildren_When_ErrorBoundary_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.ErrorBoundary(fallback: _ => V.Label(text: "fb"), children: null));
        }

        [Test]
        public void Given_ProviderOutsideBoundary_When_ChildConsumesContext_Then_ProviderValuePropagates()
        {
            // Act — a consumer inside the boundary reads the value provided just outside it
            using var mounted = V.Mount(_root,
                V.Provider(s_testCtx, value: "hello",
                    children: new VNode[]
                    {
                        V.ErrorBoundary(
                            fallback: BuildFallback,
                            children: new VNode[] { V.Component(ContextConsumerRender, key: "consumer") }),
                    }));

            // Assert
            Assert.That(_root.Q<Label>(name: "ctx-label")?.text, Is.EqualTo("hello"),
                "The Provider value propagates to the child even through the boundary");
        }

        #region Test components

        private static readonly ComponentContext<string> s_testCtx
            = ComponentContext<string>.Create(defaultValue: null);

        private static bool s_throwOnRender;
        private static bool s_fallbackInvoked;
        private static Exception s_fallbackException;

        // The [Component] attribute is required because Hooks.UseContext must run under a fiber.
        [Component]
        private static VNode ContextConsumerRender()
        {
            var value = Hooks.UseContext(s_testCtx);
            return V.Label(text: value ?? "<null>", name: "ctx-label");
        }

        private static VNode BuildFallback(Exception ex)
        {
            s_fallbackInvoked = true;
            s_fallbackException = ex;
            return V.Label(text: "fallback", name: "fallback-label");
        }

        [Component]
        private static VNode NormalChildRender()
            => V.Label(text: "ok", name: "ok-label");

        [Component]
        private static VNode ThrowingChildRender()
        {
            if (s_throwOnRender) throw new InvalidOperationException("test render error");
            return V.Label(text: "ok", name: "ok-label");
        }

        #endregion
    }
}
