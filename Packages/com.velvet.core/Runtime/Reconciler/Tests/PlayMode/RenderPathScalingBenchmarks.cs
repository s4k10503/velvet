using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// The same render at several list sizes, so a cost that grows with the list shows as a series that grows
    /// with the size in the case's name. Each sample is one update and the drain that commits it: its time, and
    /// the GC.Alloc blocks it charged (<see cref="WindowCost"/>).
    /// <para>
    /// The drain is the batch scheduler's own (<c>DrainForTest</c>), which starts from the fibers an update
    /// queued. Not <c>FlushStateForTest</c>: it visits every fiber of the tree, a cost that grows with the list
    /// whatever the update.
    /// </para>
    /// <para>
    /// After every update, measured or not, the components that rendered are counted and the tree is read back,
    /// and a difference from what the update should cause throws.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class RenderPathScalingBenchmarks
    {
        private static readonly int WarmupCount = BenchmarkRun.Warmup(3);
        private static readonly int MeasurementCount = BenchmarkRun.Measurements(10);

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

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void RowSetState_Under250MemoizedRows() => RunRowSetState(250);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void RowSetState_Under500MemoizedRows() => RunRowSetState(500);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void RowSetState_Under1000MemoizedRows() => RunRowSetState(1000);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void RowSetState_Under2000MemoizedRows() => RunRowSetState(2000);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void ParentRerender_250InlineComponentRows() => RunParentRerender(250);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void ParentRerender_500InlineComponentRows() => RunParentRerender(500);

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green; its tree check holds behaviour the base already has.
        [Test, Performance]
        public void ParentRerender_1000InlineComponentRows() => RunParentRerender(1000);

        // The row in the middle of the list updates its own state; neither its parent nor another row renders.
        private void RunRowSetState(int rows)
        {
            RowStateApp.Reset(rows);
            _mounted = V.Mount(_root, V.Component(RowStateApp.RenderList));
            _mounted.DrainForTest();
            var row = rows / 2;
            var next = 0;

            Execute(
                () =>
                {
                    next++;
                    RowStateApp.ListRenders = 0;
                    RowStateApp.RowRenders = 0;
                },
                () =>
                {
                    RowStateApp.Setters[row].Invoke(next);
                    _mounted.DrainForTest();
                },
                () =>
                {
                    RequireRenders("list", RowStateApp.ListRenders, 0);
                    RequireRenders("row", RowStateApp.RowRenders, 1);
                    var shown = ((Label)_root[0][row]).text;
                    if (shown != RowStateApp.Text(row, next))
                    {
                        throw new InvalidOperationException($"row {row} shows '{shown}', its state holds {next}");
                    }
                });
        }

        // The parent's own state changes, and every row under it renders, as does the one component each row holds:
        // none of the three components is memoized, the case React re-renders a parent's whole subtree in.
        private void RunParentRerender(int rows)
        {
            InlineRowsApp.Reset(rows);
            _mounted = V.Mount(_root, V.Component(InlineRowsApp.RenderList));
            _mounted.DrainForTest();
            var next = 0;

            Execute(
                () =>
                {
                    next++;
                    InlineRowsApp.RowRenders = 0;
                    InlineRowsApp.LeafRenders = 0;
                },
                () =>
                {
                    InlineRowsApp.SetTick.Invoke(next);
                    _mounted.DrainForTest();
                },
                () =>
                {
                    RequireRenders("row", InlineRowsApp.RowRenders, rows);
                    RequireRenders("leaf", InlineRowsApp.LeafRenders, rows);
                    var box = _root[0];
                    if (box.childCount != rows || box[rows - 1].name != InlineRowsApp.Name(rows - 1))
                    {
                        throw new InvalidOperationException($"expected {rows} rows ending in '{InlineRowsApp.Name(rows - 1)}'");
                    }
                });
        }

        private static void Execute(Action arrange, Action update, Action verify)
        {
            var time = new SampleGroup("Op.Time", SampleUnit.Millisecond);
            var allocation = new SampleGroup("Op.GC", SampleUnit.Undefined);

            for (var i = 0; i < WarmupCount; i++)
            {
                arrange();
                update();
                verify();
            }

            for (var i = 0; i < MeasurementCount; i++)
            {
                arrange();
                var cost = WindowCost.Of(update);
                verify();
                Measure.Custom(time, cost.Milliseconds);
                Measure.Custom(allocation, cost.AllocationBlocks);
            }
        }

        private static void RequireRenders(string component, int rendered, int expected)
        {
            if (rendered != expected)
            {
                throw new InvalidOperationException($"expected {expected} {component} renders, saw {rendered}");
            }
        }
    }

    internal sealed record ScalingRowProps(int Index, string Key, string Label);

    // A list of memoized rows, each holding its own state, under a parent holding the rows as its own.
    internal static class RowStateApp
    {
        private static ScalingRowProps[] s_rows = Array.Empty<ScalingRowProps>();

        internal static StateUpdater<int>[] Setters = Array.Empty<StateUpdater<int>>();
        internal static int ListRenders;
        internal static int RowRenders;

        internal static void Reset(int rows)
        {
            s_rows = new ScalingRowProps[rows];
            for (var i = 0; i < rows; i++)
            {
                s_rows[i] = new ScalingRowProps(i, "row-" + i, "row " + i);
            }
            Setters = new StateUpdater<int>[rows];
            ListRenders = 0;
            RowRenders = 0;
        }

        internal static string Text(int row, int count) => count == 0 ? s_rows[row].Label : s_rows[row].Label + " " + count;

        // The rows are the parent's state, so the compiler caches the body built from them.
        [Component]
        internal static VNode RenderList()
        {
            ListRenders++;
            var (rows, _) = Hooks.UseState(s_rows);
            return V.Div("flex flex-col", V.List(rows, row => row.Key, row => V.Component(RenderRow, row)));
        }

        [Component(Memoize = true)]
        private static VNode RenderRow(ScalingRowProps props)
        {
            RowRenders++;
            var (count, setCount) = Hooks.UseState(0);
            Setters[props.Index] = setCount;
            return V.Label(text: Text(props.Index, count));
        }
    }

    // The tree the inline-component parent re-render was reported on: a parent holding one state, a row
    // per item, and one component inside each row. The keys and names are made once, before any window.
    internal static class InlineRowsApp
    {
        private static string[] s_keys = Array.Empty<string>();
        private static string[] s_names = Array.Empty<string>();

        internal static StateUpdater<int> SetTick;
        internal static int RowRenders;
        internal static int LeafRenders;

        internal static void Reset(int rows)
        {
            s_keys = new string[rows];
            s_names = new string[rows];
            for (var i = 0; i < rows; i++)
            {
                s_keys[i] = "k" + i;
                s_names[i] = "r" + i;
            }
            SetTick = default;
            RowRenders = 0;
            LeafRenders = 0;
        }

        internal static string Name(int row) => s_names[row];

        [Component(Compiler = false)]
        internal static VNode RenderList()
        {
            var (_, setTick) = Hooks.UseState(0);
            SetTick = setTick;
            var children = new VNode?[s_keys.Length];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = V.Component(RenderRow, i, key: s_keys[i]);
            }
            return V.Div(name: "box", children: children);
        }

        [Component(Compiler = false)]
        private static VNode RenderRow(int row)
        {
            RowRenders++;
            return V.Component(RenderLeaf, row, key: "inner");
        }

        [Component(Compiler = false)]
        private static VNode RenderLeaf(int row)
        {
            LeafRenders++;
            return V.Label(name: s_names[row], key: "r");
        }
    }
}
