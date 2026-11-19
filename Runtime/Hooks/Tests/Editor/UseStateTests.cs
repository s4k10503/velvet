using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseState{T}"/> in a function component.
    /// <list type="bullet">
    /// <item>The first render returns the initial value, or the lazy initializer's result computed exactly once.</item>
    /// <item>Invoking the setter with a value that differs from the current one schedules a re-render that observes the new value.</item>
    /// <item>Invoking the setter with an equal value is a no-op and schedules no re-render.</item>
    /// <item>The setter identity is stable across re-renders.</item>
    /// <item>A functional updater (<c>prev =&gt; next</c>) reads the latest committed value at execution time, so stacked
    /// updates compose and a stale captured updater never observes a stale value.</item>
    /// <item>Each call owns an independent slot keyed by call order; the slot count and element type must be identical on
    /// every render, otherwise an <see cref="InvalidOperationException"/> is raised.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static fields
    /// are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers, and use the
    /// <c>s_{componentName}{FieldName}</c> prefix to avoid collisions across components in the same fixture.
    /// </remarks>
    [TestFixture]
    internal sealed class UseStateTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetCounter();
            ResetTwoCounters();
            ResetConditional();
            ResetTypeMismatch();
            ResetUpdater();
        }

        [Test]
        public void Given_InitialValue_When_FirstRender_Then_ReturnsInitialValue()
        {
            // Arrange
            s_counterInitial = 42;

            // Act
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));

            // Assert
            Assert.AreEqual(42, s_counterLastValue);
        }

        [Test]
        public void Given_MountedComponent_When_SetterInvokedWithNewValue_Then_NewValueIsObserved()
        {
            // Arrange
            s_counterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));

            // Act
            s_counterSetter.Invoke(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(7, s_counterLastValue);
        }

        [Test]
        public void Given_MountedComponent_When_SetterInvokedWithNewValue_Then_ComponentReRendersExactlyOnce()
        {
            // Arrange
            s_counterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));
            Assume.That(s_counterRenderCount, Is.EqualTo(1), "Precondition: the first render happened once");

            // Act
            s_counterSetter.Invoke(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(2, s_counterRenderCount);
        }

        [Test]
        public void Given_MountedComponent_When_ReRendered_Then_SetterReferenceIsStable()
        {
            // Arrange
            s_counterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));
            var firstSetter = s_counterSetter;

            // Act
            s_counterSetter.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreSame(firstSetter, s_counterSetter, "Setter reference is identical after re-render");
        }

        [Test]
        public void Given_MountedComponent_When_SetterInvokedWithSameValue_Then_NoRerender()
        {
            // Arrange
            s_counterInitial = 5;
            using var mounted = V.Mount(_root, V.Component(CounterRender, key: "counter"));
            var renderCountBefore = s_counterRenderCount;

            // Act
            s_counterSetter.Invoke(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(renderCountBefore, s_counterRenderCount, "Setting an equal value schedules no re-render");
        }

        [Test]
        public void Given_TwoStateSlots_When_FirstSlotUpdated_Then_SecondSlotUnchanged()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoCountersRender, key: "two"));

            // Act
            s_twoCountersSetA.Invoke(10);
            mounted.FlushStateForTest();
            Assume.That(s_twoCountersA, Is.EqualTo(10), "Precondition: updating the first slot took effect");

            // Assert
            Assert.AreEqual(0, s_twoCountersB, "Updating one slot leaves the other slot untouched");
        }

        [Test]
        public void Given_TwoStateSlots_When_SecondSlotUpdated_Then_FirstSlotUnchanged()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoCountersRender, key: "two"));

            // Act
            s_twoCountersSetB.Invoke(20);
            mounted.FlushStateForTest();
            Assume.That(s_twoCountersB, Is.EqualTo(20), "Precondition: updating the second slot took effect");

            // Assert
            Assert.AreEqual(0, s_twoCountersA, "Updating one slot leaves the other slot untouched");
        }

        [Test]
        public void Given_HookCountChangesBetweenRenders_When_ReRendered_Then_LogsInvalidOperationException()
        {
            // Arrange — the hook count grows on the second render, which violates the slot-count invariant
            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("InvalidOperationException"));
            using var mounted = V.Mount(_root, V.Component(ConditionalHookRender, key: "conditional"));

            // Act
            s_conditionalHookSetExtra.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        [Test]
        public void Given_SlotTypeChangesBetweenRenders_When_ReRendered_Then_LogsInvalidOperationException()
        {
            // Arrange
            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("InvalidOperationException"));
            using var mounted = V.Mount(_root, V.Component(TypeMismatchRender, key: "typemismatch"));

            // Act
            s_typeMismatchSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        [Test]
        public void Given_SlotTypeChangesBetweenRenders_When_ReRendered_Then_ExceptionMessageNamesDeclaringComponent()
        {
            // Arrange
            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex(@"UseStateTests\.TypeMismatchRender: UseState type changed"));
            using var mounted = V.Mount(_root, V.Component(TypeMismatchRender, key: "typemismatch-decltype"));

            // Act
            s_typeMismatchSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the exception message names the declaring component
        }

        [Test]
        public void Given_FunctionalUpdater_When_Invoked_Then_ComputesNewValueFromPrevious()
        {
            // Arrange
            s_updaterInitial = 10;
            using var mounted = V.Mount(_root, V.Component(UpdaterRender, key: "updater"));

            // Act
            s_updaterUpdater.Invoke(prev => prev + 5);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(15, s_updaterLastValue,
                "A functional updater (prev => prev + 5) reads the previous value and computes the new one");
        }

        [Test]
        public void Given_MountedComponent_When_ReRendered_Then_UpdaterReferenceIsStable()
        {
            // Arrange
            s_updaterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(UpdaterRender, key: "updater"));
            var firstUpdater = s_updaterUpdater;

            // Act
            s_updaterUpdater.Invoke(prev => prev + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreSame(firstUpdater, s_updaterUpdater, "Updater reference is identical after re-render");
        }

        [Test]
        public void Given_MultipleUpdatersInOneFrame_When_Invoked_Then_EachReadsLatestValue()
        {
            // Arrange
            s_updaterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(UpdaterRender, key: "updater"));

            // Act — three updaters in the same frame; each reads the previous updater's result
            s_updaterUpdater.Invoke(prev => prev + 1); // 0 → 1
            s_updaterUpdater.Invoke(prev => prev + 10); // 1 → 11
            s_updaterUpdater.Invoke(prev => prev * 2); // 11 → 22
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(22, s_updaterLastValue,
                "Successive functional updaters chain by reading the previous updater's result");
        }

        [Test]
        public void Given_StaleUpdaterReference_When_Invoked_Then_ReadsLatestValueAtExecutionTime()
        {
            // Arrange — capture an updater reference, then move the slot value forward to 100
            s_updaterInitial = 1;
            using var mounted = V.Mount(_root, V.Component(UpdaterRender, key: "updater"));
            var capturedUpdater = s_updaterUpdater;
            s_updaterUpdater.Invoke(_ => 100);
            mounted.FlushStateForTest();
            Assume.That(s_updaterLastValue, Is.EqualTo(100), "Precondition: the slot value is already 100");

            // Act — invoke via the stale captured reference
            capturedUpdater.Invoke(prev => prev + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(101, s_updaterLastValue,
                "A stale updater reference still reads the latest committed value at execution time");
        }

        [Test]
        public void Given_FunctionalUpdater_When_ReturnsSameValue_Then_NoRerender()
        {
            // Arrange
            s_updaterInitial = 5;
            using var mounted = V.Mount(_root, V.Component(UpdaterRender, key: "updater"));
            var renderCountBefore = s_updaterRenderCount;

            // Act — an updater returning the same value uses the same equality check as the setter
            s_updaterUpdater.Invoke(prev => prev);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(renderCountBefore, s_updaterRenderCount,
                "An updater that returns an equal value schedules no re-render");
        }

        [Test]
        public void Given_NullUpdater_When_Invoked_Then_ThrowsArgumentNullException()
        {
            // Arrange
            s_updaterInitial = 0;
            using var mounted = V.Mount(_root, V.Component(UpdaterRender, key: "updater"));

            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => s_updaterUpdater.Invoke(null));
        }

        #region Updater component (3-tuple updateValue)

        private static int s_updaterInitial;
        private static int s_updaterLastValue;
        private static int s_updaterRenderCount;
        private static Action<Func<int, int>> s_updaterUpdater;

        private static void ResetUpdater()
        {
            s_updaterInitial = 0;
            s_updaterLastValue = 0;
            s_updaterRenderCount = 0;
            s_updaterUpdater = null;
        }

        [Component]
        public static VNode UpdaterRender()
        {
            s_updaterRenderCount++;
            // The single UseState setter handles the functional updater (prev => next). The implicit
            // conversion to Action<Func<T, T>> exposes that form as a delegate.
            var (value, setValue) = Hooks.UseState(s_updaterInitial);
            s_updaterLastValue = value;
            s_updaterUpdater = setValue;
            return V.Label(text: value.ToString());
        }

        #endregion

        #region Counter component (single UseState)

        private static int s_counterInitial;
        private static int s_counterLastValue;
        private static int s_counterRenderCount;
        private static Action<int> s_counterSetter;

        private static void ResetCounter()
        {
            s_counterInitial = 0;
            s_counterLastValue = 0;
            s_counterRenderCount = 0;
            s_counterSetter = null;
        }

        [Component]
        public static VNode CounterRender()
        {
            s_counterRenderCount++;
            var (value, setValue) = Hooks.UseState(s_counterInitial);
            s_counterLastValue = value;
            s_counterSetter = setValue;
            return V.Label(text: value.ToString());
        }

        #endregion

        #region TwoCounters component (two UseState slots)

        private static int s_twoCountersA;
        private static int s_twoCountersB;
        private static Action<int> s_twoCountersSetA;
        private static Action<int> s_twoCountersSetB;

        private static void ResetTwoCounters()
        {
            s_twoCountersA = 0;
            s_twoCountersB = 0;
            s_twoCountersSetA = null;
            s_twoCountersSetB = null;
        }

        [Component]
        public static VNode TwoCountersRender()
        {
            var (a, setA) = Hooks.UseState(0);
            var (b, setB) = Hooks.UseState(0);
            s_twoCountersA = a;
            s_twoCountersB = b;
            s_twoCountersSetA = setA;
            s_twoCountersSetB = setB;
            return V.Label(text: $"{a},{b}");
        }

        #endregion

        #region ConditionalHook component (hook order violation)

        private static Action<bool> s_conditionalHookSetExtra;

        private static void ResetConditional() => s_conditionalHookSetExtra = null;

        [Component]
        public static VNode ConditionalHookRender()
        {
            var (flag, setFlag) = Hooks.UseState(false);
            s_conditionalHookSetExtra = setFlag;
            if (flag)
            {
                Hooks.UseState(0);
            }
            return V.Label(text: flag.ToString());
        }

        #endregion

        #region TypeMismatch component (slot type changes between renders)

        private static int s_typeMismatchRenderCount;
        private static Action<int> s_typeMismatchSetTick;

        private static void ResetTypeMismatch()
        {
            s_typeMismatchRenderCount = 0;
            s_typeMismatchSetTick = null;
        }

        [Component]
        public static VNode TypeMismatchRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_typeMismatchSetTick = setTick;
            if (s_typeMismatchRenderCount++ == 0)
            {
                Hooks.UseState<int>(0);
            }
            else
            {
                Hooks.UseState<string>("hi");
            }
            return V.Label(text: "x");
        }

        #endregion

        #region Lazy initializer (UseState(() => factory))

        [Test]
        public void Given_LazyInitializer_When_FirstRender_Then_FactoryRunsExactlyOnce()
        {
            // Arrange
            ResetLazyState();

            // Act
            using var mounted = V.Mount(_root, V.Component(LazyStateRender, key: "lazy"));

            // Assert
            Assert.That(s_lazyFactoryInvokeCount, Is.EqualTo(1),
                "The factory runs exactly once during slot allocation on the first render");
        }

        [Test]
        public void Given_LazyInitializer_When_FirstRender_Then_ReturnsFactoryResult()
        {
            // Arrange
            ResetLazyState();

            // Act
            using var mounted = V.Mount(_root, V.Component(LazyStateRender, key: "lazy"));

            // Assert
            Assert.That(s_lazyLastValue, Is.EqualTo(123), "The initial value comes from the factory result");
        }

        [Test]
        public void Given_LazyInitializer_When_ReRendered_Then_FactoryDoesNotRunAgain()
        {
            // Arrange
            ResetLazyState();
            using var mounted = V.Mount(_root, V.Component(LazyStateRender, key: "lazy"));
            Assume.That(s_lazyFactoryInvokeCount, Is.EqualTo(1), "Precondition: the factory ran once on the first render");

            // Act
            s_lazySetter.Invoke(999);
            mounted.FlushStateForTest();
            Assume.That(s_lazyLastValue, Is.EqualTo(999), "Precondition: the setter scheduled a re-render");

            // Assert
            Assert.That(s_lazyFactoryInvokeCount, Is.EqualTo(1),
                "The factory does not re-run on re-render even when the slot value changes");
        }

        [Test]
        public void Given_NullLazyInitializer_When_UseStateCalled_Then_ThrowsArgumentNullException()
        {
            // Arrange
            ResetLazyState();

            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => Hooks.UseState<int>((Func<int>)null));
        }

        private static int s_lazyFactoryInvokeCount;
        private static int s_lazyLastValue;
        private static Action<int> s_lazySetter;

        private static void ResetLazyState()
        {
            s_lazyFactoryInvokeCount = 0;
            s_lazyLastValue = 0;
            s_lazySetter = null;
        }

        [Component]
        public static VNode LazyStateRender()
        {
            var (value, setValue) = Hooks.UseState(() =>
            {
                s_lazyFactoryInvokeCount++;
                return 123;
            });
            s_lazyLastValue = value;
            s_lazySetter = setValue;
            return V.Label(text: value.ToString());
        }

        #endregion
    }
}
