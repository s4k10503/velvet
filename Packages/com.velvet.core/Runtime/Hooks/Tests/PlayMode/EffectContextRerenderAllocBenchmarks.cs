using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// <see cref="UseStateRerenderAllocBenchmarks.UseState_RerenderClosureAlloc"/>'s component and window, with four
    /// effects whose dependency is the state each re-render changes and two contexts read from providers above it,
    /// so what the case records above that one is what a re-render's effects and context reads cost.
    /// </summary>
    /// <remarks>
    /// After the measurement the effect runs and their cleanups are counted against the re-renders, and the
    /// context values read against the providers', and a difference throws, so the number is not read off
    /// re-renders that skipped the effects or the reads.
    /// </remarks>
    [TestFixture]
    internal sealed class EffectContextRerenderAllocBenchmarks
    {
        private const int HooksPerRender = 16;
        private const int EffectsPerRender = 4;
        private const int RerenderIterations = 100;
        private static readonly int WarmUp = BenchmarkRun.Warmup(3);
        private static readonly int Measurements = BenchmarkRun.Measurements(10);

        private static readonly ComponentContext<string> s_first = ComponentContext<string>.Create("first-default");
        private static readonly ComponentContext<string> s_second = ComponentContext<string>.Create("second-default");
        private static readonly Action s_cleanup = () => s_cleanups++;

        private static Action<int> s_anchorSetter = null!;
        private static int s_effectRuns;
        private static int s_cleanups;
        private static string? s_firstRead;
        private static string? s_secondRead;

        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_effectRuns = 0;
            s_cleanups = 0;
            s_firstRead = null;
            s_secondRead = null;
        }

        [Component]
        public static VNode EffectContextHeavyRender()
        {
            var lastValue = 0;
            Action<int> lastSetter = null!;
            for (var i = 0; i < HooksPerRender; i++)
            {
                var (v, set) = Hooks.UseState(0);
                lastValue = v;
                lastSetter = set;
            }
            s_anchorSetter = lastSetter;

            s_firstRead = Hooks.UseContext(s_first);
            s_secondRead = Hooks.UseContext(s_second);

            Hooks.UseEffect(() =>
            {
                s_effectRuns++;
                return s_cleanup;
            }, new object[] { lastValue });
            Hooks.UseEffect(() =>
            {
                s_effectRuns++;
                return s_cleanup;
            }, new object[] { lastValue });
            Hooks.UseEffect(() =>
            {
                s_effectRuns++;
                return s_cleanup;
            }, new object[] { lastValue });
            Hooks.UseEffect(() =>
            {
                s_effectRuns++;
                return s_cleanup;
            }, new object[] { lastValue });

            return V.Label(text: lastValue.ToString());
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its effect count holds behaviour the base already has.
        [Test, Performance]
        public void UseState_RerenderClosureAlloc_With4EffectsAnd2Contexts()
        {
            var mounted = V.Mount(_root, V.Provider(s_first, "a", new VNode?[]
            {
                V.Provider(s_second, "b", new VNode?[] { V.Component(EffectContextHeavyRender, key: "alloc-bench") }),
            }));
            var rerenders = 0;
            int effectRuns, cleanups;

            try
            {
                mounted.FlushStateForTest();
                Measure.Method(() =>
                {
                    for (var i = 0; i < RerenderIterations; i++)
                    {
                        s_anchorSetter(++rerenders);
                        mounted.FlushStateForTest();
                    }
                })
                    .GC()
                    .WarmupCount(WarmUp)
                    .MeasurementCount(Measurements)
                    .Run();
                mounted.FlushEffectsForTest();
                effectRuns = s_effectRuns;
                cleanups = s_cleanups;
            }
            finally
            {
                mounted.Dispose();
            }

            // The mount ran each effect once, and every re-render ran each one again after cleaning up its last run.
            if (effectRuns != EffectsPerRender * (rerenders + 1) || cleanups != EffectsPerRender * rerenders
                || s_firstRead != "a" || s_secondRead != "b")
            {
                throw new InvalidOperationException(
                    $"{rerenders} re-renders ran {effectRuns} effects and {cleanups} cleanups, and read '{s_firstRead}' and '{s_secondRead}'");
            }
        }
    }
}
