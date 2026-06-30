using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Measures GC alloc of Hooks.UseState during re-renders of function components.
    /// <para>
    /// The <see cref="StateUpdater{T}"/> setter's closures are built once at slot creation and cached, so
    /// closure alloc on re-render is expected to be zero.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UseStateRerenderAllocBenchmarks
    {
        private const int HooksPerRender = 16;
        private const int RerenderIterations = 100;
        private const int WarmUp = 3;
        private const int Measurements = 10;

        private static Action<int> s_anchorSetter;

        private VisualElement _root;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [Component]
        public static VNode StateHeavyRender()
        {
            // Expose the last slot's setter externally as an anchor and drive re-renders from the test body.
            // lastValue is the last slot's value forwarded to the Label, but only the setter via the anchor
            // triggers re-renders.
            var lastValue = 0;
            Action<int> lastSetter = null;
            for (var i = 0; i < HooksPerRender; i++)
            {
                var (v, set) = Hooks.UseState(0);
                lastValue = v;
                lastSetter = set;
            }
            s_anchorSetter = lastSetter;
            return V.Label(text: lastValue.ToString());
        }

        [Test, Performance]
        public void UseState_RerenderClosureAlloc()
        {
            var mounted = V.Mount(_root, V.Component(StateHeavyRender, key: "alloc-bench"));

            try
            {
                Measure.Method(() =>
                {
                    for (var i = 0; i < RerenderIterations; i++)
                    {
                        s_anchorSetter(i + 1);
                        mounted.FlushStateForTest();
                    }
                })
                    .GC()
                    .WarmupCount(WarmUp)
                    .MeasurementCount(Measurements)
                    .Run();
            }
            finally
            {
                mounted.Dispose();
            }
        }
    }
}
