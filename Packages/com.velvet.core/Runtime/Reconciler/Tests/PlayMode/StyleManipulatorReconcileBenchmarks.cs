// The class-name parse cache's drain hook is editor-only, and the unchanged-class measurement below
// depends on it to put two content-identical trees on the class diff's content-compare path.
#if UNITY_EDITOR
using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine.UIElements;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Benchmarks the per-element style-manipulator configure pass: the state / conditional /
    /// relational / has- variant families (re-derived only when the class list changed CONTENT) and
    /// the child-variant / gap / divide / grid / text-balance families (re-applied on every patch,
    /// because they re-derive against the current child set).
    /// A class-less tree never enters any of those blocks — every family early-outs on the first
    /// token scan — so the label-only reconciler benchmarks cannot detect a cost added there. The
    /// rows here carry live tokens of all nine families, which makes this fixture the measurement
    /// that gates changes to the configure pass. Between them the four cases drive all three
    /// branches: create, update and teardown.
    /// </summary>
    internal sealed class StyleManipulatorReconcileBenchmarks
    {
        private const int k_Rows = 100;
        private const int k_WarmupCount = 5;
        private const int k_MeasurementCount = 20;

        // grid-cols-* suppresses the gap manipulator (the grid owns the child margins), so the gap
        // family is exercised on the row containers and its suppression path on the grid containers.
        private const string k_GridClass = "grid grid-cols-3 gap-4";

        // The stripped counterparts keep the element type and child count of their decorated twins so
        // a reconcile between the two is a pure class change: same elements, no create or destroy.
        private const string k_StrippedRowClass = "flex flex-row";
        private const string k_StrippedGridClass = "flex flex-col";

        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
        }

        // Element creation runs the create branch of every family.
        [Test, Performance]
        public void Mount_100StyledRows()
        {
            var rows = BuildRows("red");

            Measure.Method(() =>
                {
                    _reconciler.Reconcile(_root, Array.Empty<VNode>(), rows);
                    _reconciler.Reconcile(_root, rows, Array.Empty<VNode>());
                })
                .GC()
                .WarmupCount(k_WarmupCount)
                .MeasurementCount(k_MeasurementCount)
                .Run();
        }

        // Distinct VNode instances carrying the same tokens in freshly parsed arrays: the reference
        // skips in ChildReconciler and DiffClassList both miss, so every element is patched and the
        // class diff runs its full content comparison — which reports no change, leaving the four
        // variant families unvisited. What remains is the every-patch cost of child-variant / gap /
        // divide / grid / text-balance.
        [Test, Performance]
        public void Reconcile_UnchangedClassList_100StyledRows()
        {
            var mounted = BuildRows("red");
            // Parsed class-name arrays are cached by string CONTENT, so a second build from the same
            // strings would hand back the very same arrays and the class diff would exit at its
            // identity check. Draining the cache reproduces what a component that rebuilds its VNode
            // tree every render actually hands the reconciler.
            V.ClearClassNameCacheForTesting();
            var repeat = BuildRows("red");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);

            Measure.Method(() => _reconciler.Reconcile(_root, mounted, repeat))
                .GC()
                .WarmupCount(k_WarmupCount)
                .MeasurementCount(k_MeasurementCount)
                .Run();
        }

        // Only the variant PAYLOADS differ between the two trees, and no variant token ever enters
        // the USS class list, so the class diff itself does almost nothing while all four variant
        // families take their update branch on every row: the warm path a consolidation of the
        // configure shape has to leave untouched.
        [Test, Performance]
        public void Reconcile_ChangedClassList_100StyledRows()
        {
            var oldRows = BuildRows("red");
            var newRows = BuildRows("blue");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldRows);

            Measure.Method(() => _reconciler.Reconcile(_root, oldRows, newRows))
                .GC()
                .WarmupCount(k_WarmupCount)
                .MeasurementCount(k_MeasurementCount)
                .Run();
        }

        // Losing a utility class while staying mounted: every family finds its manipulator in the
        // table, no longer wanted, and has to detach it from the still-live element and drop the
        // table entry. A wholesale unmount cannot stand in for this — that path runs through
        // FiberElementCleaner and never reaches the configure step at all.
        // Measured as a round trip because a single strip only tears down on its first iteration;
        // re-decorating first puts every iteration back on the teardown branch.
        [Test, Performance]
        public void Reconcile_StripAndRestoreClassList_100StyledRows()
        {
            var decorated = BuildRows("red");
            var stripped = BuildStrippedRows();
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), stripped);

            Measure.Method(() =>
                {
                    _reconciler.Reconcile(_root, stripped, decorated);
                    _reconciler.Reconcile(_root, decorated, stripped);
                })
                .GC()
                .WarmupCount(k_WarmupCount)
                .MeasurementCount(k_MeasurementCount)
                .Run();
        }

        private static VNode[] BuildRows(string tint)
        {
            var rowClass = RowClass(tint);
            var leafClass = LeafClass(tint);
            var rows = new VNode[k_Rows];
            for (var i = 0; i < k_Rows; i++)
            {
                rows[i] = V.Div(rowClass,
                    V.Div(k_GridClass,
                        V.Label(className: leafClass, text: "cell-a"),
                        V.Label(className: leafClass, text: "cell-b")),
                    V.Label(className: leafClass, text: "tail"));
            }
            return rows;
        }

        private static VNode[] BuildStrippedRows()
        {
            var rows = new VNode[k_Rows];
            for (var i = 0; i < k_Rows; i++)
            {
                rows[i] = V.Div(k_StrippedRowClass,
                    V.Div(k_StrippedGridClass,
                        V.Label(text: "cell-a"),
                        V.Label(text: "cell-b")),
                    V.Label(text: "tail"));
            }
            return rows;
        }

        // One token of every family whose manipulator hangs off the container: state variants,
        // responsive + dark conditionals, group-/peer- relationals, event-driven has-, the [&>*]:
        // child combinator, gap and divide. `tint` varies only the variant payloads.
        private static string RowClass(string tint) =>
            "flex flex-row group gap-4 divide-y divide-gray-200 [&>*]:mt-2 "
            + $"hover:bg-{tint}-500 focus:bg-{tint}-400 active:bg-{tint}-300 "
            + $"sm:p-2 md:p-3 lg:p-4 dark:bg-{tint}-500 "
            + "group-hover:opacity-50 peer-hover:opacity-75 "
            + $"has-[:checked]:bg-{tint}-500 has-[:focus]:text-{tint}-500";

        // Leaves carry text-balance plus a single variant family, the shape most elements in a real
        // tree have.
        private static string LeafClass(string tint) => $"text-balance hover:text-{tint}-500";
    }
}
#endif
