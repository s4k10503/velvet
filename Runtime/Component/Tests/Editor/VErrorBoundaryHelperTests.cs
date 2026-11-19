using System;
using System.Linq;
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
    /// <item>A parent's render passing new children, or a new fallback after a catch, renders those.</item>
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
            s_setTick = null;
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
                    children: new VNode[] { V.Component(ThrowingChildRender, key: "throw") }), CaughtErrors.Unlogged);

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
                    children: new VNode[] { V.Component(ThrowingChildRender, key: "throw") }), CaughtErrors.Unlogged);
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
                    }), CaughtErrors.Unlogged);

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

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private MountedTree MountCaught()
        {
            s_throwOnRender = true;
            var mounted = V.Mount(_root, V.Component(PlainHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throwOnRender = false;
            return mounted;
        }

        private static void Settle(MountedTree mounted, Action change)
        {
            change();
            mounted.FlushStateForTest();
        }

        // GREEN_ON_BASE(characterization): the merge base already renders the children the latest call passed.
        // What this pins is that the children come from the latest call rather than from the first mount.
        [Test]
        public void Given_AHelperBoundary_When_ItsParentRendersAgainWithNewChildren_Then_ItRendersTheNewChildren()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PlainHostRender, key: "host"), CaughtErrors.Unlogged);

            // Act
            Settle(mounted, () => s_setTick.Invoke(1));

            // Assert
            Assert.That(Texts(), Is.EqualTo("child:1"));
        }

        // GREEN_ON_BASE(characterization): the merge base already renders the fallback the latest call passed.
        // What this pins is that a boundary that caught renders the fallback its parent's latest render passed.
        [Test]
        public void Given_AHelperBoundaryThatCaught_When_ItsParentRendersAgainWithANewFallback_Then_ItRendersTheNewFallback()
        {
            // Arrange
            using var mounted = MountCaught();

            // Act
            Settle(mounted, () => s_setTick.Invoke(1));

            // Assert
            Assert.That(Texts(), Is.EqualTo("fallback:1"));
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

        private static Action<int> s_setTick;

        [Component(Compiler = false)]
        private static VNode TickChildRender(int tick)
        {
            if (s_throwOnRender) throw new InvalidOperationException("test render error");
            return V.Label(text: "child:" + tick);
        }

        [Component(Compiler = false)]
        private static VNode PlainHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.ErrorBoundary(
                    fallback: _ => V.Label(text: "fallback:" + tick),
                    children: new VNode[] { V.Component(TickChildRender, tick, key: "child") },
                    key: "boundary"),
            });
        }

        [Component]
        private static VNode ThrowingChildRender()
        {
            if (s_throwOnRender) throw new InvalidOperationException("test render error");
            return V.Label(text: "ok", name: "ok-label");
        }

        #endregion
    }
}
