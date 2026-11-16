using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a patch's second half reads of the first within one pass: the stacking classes a variant lights
    /// while the node's own classes are written.
    /// </summary>
    [TestFixture]
    internal sealed class PatchReadBackTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private static VNode[] Tree(string state)
            => new VNode[]
            {
                V.Div(name: "parent", className: "relative", children: new VNode[]
                {
                    V.Div(name: "lit", className: "data-[state=open]:absolute data-[state=open]:z-10",
                        data: new Dictionary<string, string> { ["state"] = state }),
                    V.Div(name: "ordinary"),
                }),
            };

        // GREEN_ON_BASE(characterization): the base writes the class and the variant's payload at once, so the stacking
        // classes the relocation reads are the ones the patch just lit; the case pins that read.
        [Test]
        public void Given_AVariantThatLightsAStackingClassInThePatch_When_ThePatchRuns_Then_TheElementIsRelocatedInThePass()
        {
            // Arrange
            var before = Tree("closed");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);
            var lit = _root.Q<VisualElement>("lit");

            // Act
            _reconciler.Reconcile(_root, before, Tree("open"));

            // Assert
            Assert.That(FiberZLayerCoordinator.PlaceholderOf(_reconciler.Context, lit), Is.Not.Null);
        }

        private static VNode[] KeyedRows(bool lifted, params string[] keys)
        {
            var rows = new List<VNode>();
            foreach (var key in keys)
            {
                rows.Add(V.Div(key: key, name: key, className: lifted && key == "a" ? "absolute z-10" : null));
            }
            return new VNode[] { V.Div(name: "parent", className: "relative", children: rows.ToArray()) };
        }

        // GREEN_ON_BASE(refactor): the base relocates the row inside its patch and the reorder then moves the placeholder
        // with the declared slot; what the case pins is that the same order results where the relocation waits for the
        // end of the pass and the reorder moves the row itself.
        [Test]
        public void Given_ARowPatchedToAbsoluteZ10_When_ThePassAlsoReordersItAmongKeyedSiblings_Then_TheSlotsAndTheFrontBandHoldTheFinalOrder()
        {
            // Arrange
            var before = KeyedRows(false, "a", "b", "c");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);
            var parent = _root.Q<VisualElement>("parent");

            // Act
            _reconciler.Reconcile(_root, before, KeyedRows(true, "c", "a", "b"));

            // Assert
            var order = new List<string>();
            foreach (var child in parent.Children())
            {
                order.Add(_reconciler.Context.ZLayerPlaceholders.ContainsKey(child) ? "placeholder" : child.name);
            }
            Assert.That(string.Join(",", order), Is.EqualTo("c,placeholder,b,a"));
        }

        private static VNode[] FirstRows(params string[] keys)
        {
            var rows = new List<VNode>();
            foreach (var key in keys)
            {
                rows.Add(V.Div(key: key, name: key, className: key == "a" ? "first:absolute first:z-10" : null));
            }
            return new VNode[] { V.Div(name: "parent", className: "relative", children: rows.ToArray()) };
        }

        // GREEN_ON_BASE(characterization): the base settles the row where it is patched and the parent's structural
        // pass then returns it to its slot, so the row ends in order; the case pins that a settle queued by the patch
        // does not replay the facts the structural pass has since replaced.
        [Test]
        public void Given_APatchedRowWhoseStructuralVariantTheSamePassTurnsOff_When_ThePassEnds_Then_TheRowHoldsItsDeclaredSlot()
        {
            // Arrange
            var before = FirstRows("a");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);
            var parent = _root.Q<VisualElement>("parent");

            // Act
            _reconciler.Reconcile(_root, before, FirstRows("x", "a"));

            // Assert
            var order = new List<string>();
            foreach (var child in parent.Children())
            {
                order.Add(_reconciler.Context.ZLayerPlaceholders.ContainsKey(child) ? "placeholder" : child.name);
            }
            Assert.That(string.Join(",", order), Is.EqualTo("x,a"));
        }

        private static VNode[] ClassTree(string className, VNode? later = null)
        {
            var rows = new List<VNode>
            {
                V.Div(name: "box", className: className, children: className.Contains("rounded")
                    ? new VNode[] { V.Div(name: "inserted") }
                    : Array.Empty<VNode>()),
            };
            if (later != null) rows.Add(later);
            return rows.ToArray();
        }

        [Test]
        public void Given_AnElementWhoseClassChangesAndWhoseChildIsInserted_When_ALaterRowIsCreatedInThePass_Then_ItStillHoldsTheOldClass()
        {
            // Arrange
            var before = ClassTree("p-1");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);
            var box = _root.Q<VisualElement>("box");
            var seen = true;

            // Act
            _reconciler.Reconcile(_root, before, ClassTree("p-1 rounded", V.ScrollView(onCreated: _ => seen = box.ClassListContains("rounded"))));

            // Assert
            Assert.That(seen, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base writes the class as the patch runs; what the case pins is that the
        // class is on the element once the reconcile returns, where the write waits for the end of the pass.
        [Test]
        public void Given_AnElementWhoseClassChangesAndWhoseChildIsInserted_When_TheReconcileReturns_Then_ItHoldsTheNewClass()
        {
            // Arrange
            var before = ClassTree("p-1");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);

            // Act
            _reconciler.Reconcile(_root, before, ClassTree("p-1 rounded"));

            // Assert
            Assert.That(_root.Q<VisualElement>("box").ClassListContains("rounded"), Is.True);
        }

        private static VNode[] MotionTree(bool active)
            => new VNode[]
            {
                V.Motion(name: "motion", className: "has-[.on]:bg-red-500", children: new VNode[]
                {
                    V.Div(className: active ? "on" : string.Empty),
                }),
            };

        // GREEN_ON_BASE(characterization): the base writes the child's class while it patches, so the Motion's has scan,
        // which runs after its children, sees it.
        [Test]
        public void Given_AMotionWithAHasClassVariant_When_AChildGainsTheClassWithNoInsert_Then_TheMotionHoldsThePayload()
        {
            // Arrange
            var before = MotionTree(false);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);

            // Act
            _reconciler.Reconcile(_root, before, MotionTree(true));

            // Assert
            Assert.That(_root.Q<VisualElement>("motion").ClassListContains("bg-red-500"), Is.True);
        }

        private static VNode[] LiftTree(bool on, bool withLastRow)
        {
            var rows = new List<VNode>
            {
                V.Div(name: "m", className: "has-[.on]:absolute has-[.on]:z-10", children: new VNode[]
                {
                    V.Div(className: on ? "on" : string.Empty),
                }),
                V.Div(name: "x", className: "last:bg-red-500"),
            };
            if (withLastRow) rows.Add(V.Div(name: "y", className: "last:bg-red-500"));
            return new VNode[] { V.Div(name: "parent", className: "relative", children: rows.ToArray()) };
        }

        // GREEN_ON_BASE(characterization): the base applies the recorded insert of the last row ahead of the structural
        // pass of the parent, so the pass reads the row; the case pins that the relocated element's return to its slot
        // does not drop it from what the pass reads.
        [Test]
        public void Given_ARelocatedElementWhoseHasPayloadGoesOff_When_ThePassAlsoAppendsTheLastRow_Then_TheLastRowHoldsTheLastPayload()
        {
            // Arrange
            var before = LiftTree(true, false);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);

            // Act
            _reconciler.Reconcile(_root, before, LiftTree(false, true));

            // Assert
            Assert.That(_root.Q<VisualElement>("y").ClassListContains("bg-red-500"), Is.True);
        }

        private static VNode[] AnchoredTree(bool motion)
            => new VNode[]
            {
                V.Div(name: "parent", className: "relative", children: new VNode[]
                {
                    motion
                        ? V.Motion(name: "a", props: new FiberElementProps { Anchored = new AnchoredSettings(null) })
                        : V.Anchored(target: null, name: "a"),
                    V.Div(name: "b"),
                }),
            };

        // GREEN_ON_BASE(characterization): the base records the anchored element's facts where it patches it, so it stays
        // in front of the unpositioned row after it.
        [TestCase(false)]
        [TestCase(true)]
        public void Given_AnAnchoredElementLiftedInFrontOfAnUnpositionedRow_When_ItIsPatchedWithNoStackingClass_Then_ItStaysInFrontOfTheRow(bool motion)
        {
            // Arrange
            var before = AnchoredTree(motion);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);

            // Act
            _reconciler.Reconcile(_root, before, AnchoredTree(motion));

            // Assert
            var parent = _root.Q<VisualElement>("parent");
            var anchored = _root.Q<VisualElement>("a");
            var slot = motion ? anchored : anchored.parent;
            Assert.That(parent.IndexOf(slot), Is.GreaterThan(parent.IndexOf(_root.Q<VisualElement>("b"))));
        }

        private static VNode[] DivTree(bool active)
            => new VNode[]
            {
                V.Div(name: "box", className: "has-[.on]:bg-red-500", children: new VNode[]
                {
                    V.Div(className: active ? "on" : string.Empty),
                }),
            };

        // GREEN_ON_BASE(characterization): the base applies the child's recorded class where the pass ends, ahead of the
        // second half of the Div's own patch, which scans the children for it.
        [Test]
        public void Given_ADivWithAHasClassVariant_When_AChildGainsTheClassWithNoInsert_Then_TheDivHoldsThePayload()
        {
            // Arrange
            var before = DivTree(false);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);

            // Act
            _reconciler.Reconcile(_root, before, DivTree(true));

            // Assert
            Assert.That(_root.Q<VisualElement>("box").ClassListContains("bg-red-500"), Is.True);
        }

        private static VNode[] DelayedFieldTree(string name, VNode? later = null)
        {
            var rows = new List<VNode> { V.TextField(name: name, isDelayed: true) };
            if (later != null) rows.Add(later);
            return rows.ToArray();
        }

        [Test]
        public void Given_ADelayedTextFieldThatIsRenamed_When_ALaterRowIsCreatedInThePass_Then_TheFieldStillHoldsTheOldName()
        {
            // Arrange
            var before = DelayedFieldTree("before");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), before);
            var field = (TextField)_root[0];
            var seen = "unset";

            // Act
            _reconciler.Reconcile(_root, before, DelayedFieldTree("after", V.ScrollView(onCreated: _ => seen = field.isDelayed + field.name)));

            // Assert
            Assert.That(seen, Is.EqualTo("Truebefore"));
        }
    }
}
