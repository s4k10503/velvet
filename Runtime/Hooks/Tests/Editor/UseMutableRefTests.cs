using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseMutableRef{T}(T)"/> in a function component.
    /// <list type="bullet">
    /// <item>The first render exposes the initial value (or the lazy factory's result) as <c>Current</c>.</item>
    /// <item>Writing to <c>Current</c> does not schedule a re-render.</item>
    /// <item>The same <see cref="MutableRef{T}"/> instance and its <c>Current</c> value persist across re-renders.</item>
    /// <item>The lazy factory runs exactly once on the first render and never again.</item>
    /// <item>A null lazy factory raises an <see cref="ArgumentNullException"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers, using the
    /// <c>s_{componentName}{FieldName}</c> prefix to avoid collisions across components in the same fixture.
    /// </remarks>
    [TestFixture]
    internal sealed class UseMutableRefTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetCounter();
            ResetLazy();
        }

        [Test]
        public void Given_InitialValue_When_FirstRender_Then_CurrentReturnsInitialValue()
        {
            // Arrange
            s_counterInitial = 42;

            // Act
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "mref-initial"));

            // Assert
            Assert.AreEqual(42, s_counterCapturedRef.Current);
        }

        [Test]
        public void Given_MountedComponent_When_CurrentMutated_Then_NoRerender()
        {
            // Arrange
            s_counterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "mref-no-rerender"));
            var renderCountBefore = s_counterRenderCount;

            // Act
            s_counterCapturedRef.Current = 7;
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(renderCountBefore, s_counterRenderCount,
                "Writing to MutableRef.Current schedules no re-render");
        }

        [Test]
        public void Given_MountedComponent_When_CurrentMutated_Then_MutationPersistsOnSameInstance()
        {
            // Arrange
            s_counterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "mref-mutation"));

            // Act
            s_counterCapturedRef.Current = 7;

            // Assert
            Assert.AreEqual(7, s_counterCapturedRef.Current, "The mutation persists on the same MutableRef instance");
        }

        [Test]
        public void Given_MountedComponent_When_ReRendered_Then_SameInstanceIsReturned()
        {
            // Arrange
            s_counterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "mref-persist"));
            var firstRef = s_counterCapturedRef;

            // Act
            s_counterForceUpdateSetter.Invoke(s_counterForceUpdateValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreSame(firstRef, s_counterCapturedRef, "The same MutableRef instance is returned across re-renders");
        }

        [Test]
        public void Given_MutatedRef_When_ReRendered_Then_CurrentValuePersists()
        {
            // Arrange
            s_counterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "mref-persist-value"));
            s_counterCapturedRef.Current = 99;

            // Act
            s_counterForceUpdateSetter.Invoke(s_counterForceUpdateValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(99, s_counterCapturedRef.Current, "Current value persists across re-renders");
        }

        [Test]
        public void Given_LazyFactory_When_FirstRender_Then_CurrentReturnsFactoryResult()
        {
            // Arrange + Act
            using var mounted = V.Mount(_root, V.Component(LazyRender, key: "mref-lazy"));

            // Assert
            Assert.AreEqual(123, s_lazyCapturedRef.Current, "Current is seeded from the lazy factory result");
        }

        [Test]
        public void Given_LazyFactory_When_ReRendered_Then_FactoryRunsExactlyOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(LazyRender, key: "mref-lazy"));
            Assume.That(s_lazyFactoryCallCount, Is.EqualTo(1), "Precondition: the factory ran once on the first render");

            // Act
            s_lazyForceUpdateSetter.Invoke(s_lazyForceUpdateValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(1, s_lazyFactoryCallCount, "The lazy factory does not run again on subsequent renders");
        }

        [Test]
        public void Given_NullLazyFactory_When_UseMutableRefCalled_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => Hooks.UseMutableRef<int>(initialFactory: null));
        }

        #region Counter component

        private static int s_counterInitial;
        private static MutableRef<int> s_counterCapturedRef;
        private static int s_counterRenderCount;
        private static Action<int> s_counterForceUpdateSetter;
        private static int s_counterForceUpdateValue;

        private static void ResetCounter()
        {
            s_counterInitial = 0;
            s_counterCapturedRef = null;
            s_counterRenderCount = 0;
            s_counterForceUpdateSetter = null;
            s_counterForceUpdateValue = 0;
        }

        [Component]
        public static VNode CounterRender()
        {
            s_counterRenderCount++;
            s_counterCapturedRef = Hooks.UseMutableRef(s_counterInitial);
            var (value, setValue) = Hooks.UseState(0);
            s_counterForceUpdateValue = value;
            s_counterForceUpdateSetter = setValue;
            return V.Label(text: s_counterCapturedRef.Current.ToString());
        }

        #endregion

        #region Lazy component

        private static int s_lazyFactoryCallCount;
        private static MutableRef<int> s_lazyCapturedRef;
        private static Action<int> s_lazyForceUpdateSetter;
        private static int s_lazyForceUpdateValue;

        private static void ResetLazy()
        {
            s_lazyFactoryCallCount = 0;
            s_lazyCapturedRef = null;
            s_lazyForceUpdateSetter = null;
            s_lazyForceUpdateValue = 0;
        }

        [Component]
        public static VNode LazyRender()
        {
            s_lazyCapturedRef = Hooks.UseMutableRef(() =>
            {
                s_lazyFactoryCallCount++;
                return 123;
            });
            var (value, setValue) = Hooks.UseState(0);
            s_lazyForceUpdateValue = value;
            s_lazyForceUpdateSetter = setValue;
            return V.Label(text: s_lazyCapturedRef.Current.ToString());
        }

        #endregion
    }
}
