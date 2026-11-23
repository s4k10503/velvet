using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// The styled-row, paint and text-size shapes the bare-<c>Reconciler</c> fixtures drive, mounted through
    /// <c>V.Mount</c> under a sheet that declares a palette colour, a text size with its paired line height and
    /// breakpoints. A bare <c>Reconciler</c> never reaches a mount's theme scope, so a class gate whose
    /// answer depends on that scope is measured only here. Each sample re-renders a non-memoized component, so
    /// every row's element is patched with the class list it already holds.
    /// </summary>
    internal sealed class MountedThemeRowBenchmarks
    {
        private const string SheetPath = "Packages/com.velvet.core/Runtime/Reconciler/Tests/PlayMode/MountedThemeRowBenchmarks.uss";
        private static readonly int WarmupCount = BenchmarkRun.Warmup(5);
        private static readonly int MeasurementCount = BenchmarkRun.Measurements(20);

        private VisualElement _root = null!;
        private MountedTree _mounted = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null!;
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [Test, Performance]
        public void Rerender_UnchangedClassList_100StyledRowsUnderASheet()
            => Run(ThemeRowsApp.Shape.StyledRows);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [Test, Performance]
        public void Rerender_UnchangedClassList_100PaintRowsUnderASheet()
            => Run(ThemeRowsApp.Shape.PaintRows);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [Test, Performance]
        public void Rerender_UnchangedClassList_100TextSizeLabelsUnderASheet()
            => Run(ThemeRowsApp.Shape.TextSizeRows);

        private void Run(ThemeRowsApp.Shape shape)
        {
#if UNITY_EDITOR
            _root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(SheetPath));
#else
            Assert.Ignore("the token sheet is an asset the editor loads");
#endif
            ThemeRowsApp.Reset(shape);
            _mounted = V.Mount(_root, V.Component(ThemeRowsApp.Render));
            _mounted.DrainForTest();
            var tick = 0;

            Measure.Method(() =>
                {
                    ThemeRowsApp.SetTick.Invoke(++tick);
                    _mounted.DrainForTest();
                })
                .GC()
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }
    }

    internal static class ThemeRowsApp
    {
        internal enum Shape { StyledRows, PaintRows, TextSizeRows }

        private const int Rows = 100;

        private static Shape s_shape;

        internal static StateUpdater<int> SetTick;

        internal static void Reset(Shape shape)
        {
            s_shape = shape;
            SetTick = default;
        }

        [Component(Compiler = false)]
        internal static VNode Render()
        {
            var (_, setTick) = Hooks.UseState(0);
            SetTick = setTick;
            var rows = new VNode?[Rows];
            for (var i = 0; i < Rows; i++)
            {
                rows[i] = s_shape switch
                {
                    Shape.PaintRows => V.Div("flex flex-row -skew-x-6 shadow-lg border-dashed bg-gradient-to-r from-blue-500 to-blue-900 animate-none",
                        V.Label(text: "cell")),
                    Shape.TextSizeRows => V.Label(className: "text-sizebench text-sm", text: "cell"),
                    _ => V.Div("flex flex-row gap-4 p-2 bg-brandbench xsbench:p-4 sm:p-6 hover:bg-red-500",
                        V.Div("grid grid-cols-3 gap-4", V.Label(className: "text-sm", text: "cell-a"), V.Label(text: "cell-b")),
                        V.Label(text: "tail")),
                };
            }
            return V.Div("flex flex-col", rows);
        }
    }
}
