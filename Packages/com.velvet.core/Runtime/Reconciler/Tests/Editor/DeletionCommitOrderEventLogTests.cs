using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a commit that deletes components runs, as one <see cref="EventLog"/> string per case, each the
    /// sequence react-reconciler 19.1's <c>ReactFiberCommitWork</c> produces for the same tree: the deletion walk
    /// (<c>commitDeletionEffectsOnFiber</c>) runs a component's insertion and layout destroys and detaches an
    /// element's ref where its pre-order walk reaches them, and the passive destroys run in the passive phase
    /// (<c>commitPassiveUnmountEffectsInsideOfDeletedTree_begin</c>), where that phase's walk enters the deleted
    /// fiber's parent. A case whose name says it reads the passive phase renders the deletion first and logs only
    /// the drain after it: the walk takes deletions where the reconcile reaches them, ahead of the mutation work
    /// React gives the components that come before them.
    /// </summary>
    [TestFixture]
    internal sealed class DeletionCommitOrderEventLogTests
    {
        private sealed record Named(string Name);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode Rendered(Named props)
        {
            EventLog.UseRender(props.Name);
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode DeletedParent()
        {
            EventLog.UseEffects("P", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("P"), children: new VNode[]
            {
                V.Component(Probed, new Named("C1"), key: "c1"),
                V.Component(Probed, new Named("C2"), key: "c2"),
            });
        }

        [Component]
        private static VNode ParentHost()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            EventLog.UseRender("Root");
            EventLog.UseEffects("Root", Array.Empty<object>());
            return V.Fragment(shown ? V.Component(DeletedParent, key: "p") : null);
        }

        [Component]
        private static VNode ElementHost()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            EventLog.UseRender("Root");
            var holderRef = EventLog.UseRefCallback("H");
            return V.Fragment(shown
                ? V.Div(refCallback: holderRef, children: new VNode[]
                {
                    V.Component(Probed, new Named("C1"), key: "c1"),
                    V.Component(Probed, new Named("C2"), key: "c2"),
                })
                : null);
        }

        [Component]
        private static VNode KeyChanger()
        {
            var (second, _) = EventLog.UseState("Key", false);
            EventLog.UseRender("Key");
            return V.Fragment(second
                ? V.Component(Rendered, new Named("Cb"), key: "b")
                : V.Component(Rendered, new Named("Ca"), key: "a"));
        }

        [Component]
        private static VNode SwappedOut()
        {
            EventLog.UseEffects("A", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("A"));
        }

        [Component]
        private static VNode SwappedIn()
        {
            EventLog.UseRender("B");
            EventLog.UseEffects("B", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("B"));
        }

        [Component]
        private static VNode TypeSwapper()
        {
            var (swapped, _) = EventLog.UseState("Type", false);
            EventLog.UseRender("Type");
            return V.Fragment(swapped ? V.Component(SwappedIn) : V.Component(SwappedOut));
        }

        private sealed record Dep(string Name, int Value);

        // Its element keeps its ref callback, so a ref entry beside it says the commit took a kept element down.
        [Component]
        private static VNode Dependent(Dep props)
        {
            EventLog.UseRender(props.Name);
            EventLog.UseEffects(props.Name, new object[] { props.Value });
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode SilentDependent(Dep props)
        {
            EventLog.UseEffects(props.Name, new object[] { props.Value });
            return V.Fragment(Array.Empty<VNode>());
        }

        // The deleted child shares its element with a sibling the same render updates.
        [Component]
        private static VNode SiblingsInOneElement()
        {
            var (dep, _) = EventLog.UseState("P", 0);
            EventLog.UseRender("P");
            EventLog.UseEffects("P", new object[] { dep });
            return V.Div(refCallback: EventLog.UseRefCallback("P"), children: new VNode[]
            {
                V.Component(Dependent, new Dep("S", dep), key: "s"),
                dep == 0 ? V.Component(Probed, new Named("D"), key: "d") : null,
            });
        }

        // The deleted child sits in an element that follows a sibling the same render updates.
        [Component]
        private static VNode SiblingBeforeTheElement()
        {
            var (dep, _) = EventLog.UseState("P", 0);
            EventLog.UseEffects("P", new object[] { dep });
            return V.Fragment(
                V.Component(Dependent, new Dep("S", dep), key: "s"),
                V.Div(children: new VNode[] { dep == 0 ? V.Component(Probed, new Named("D"), key: "d") : null }));
        }

        // As above with an element holding the deleted child, so the deletion is the element's, and a sibling that
        // renders nothing.
        [Component]
        private static VNode ElementDeletedAfterASibling()
        {
            var (dep, _) = EventLog.UseState("P", 0);
            EventLog.UseEffects("P", new object[] { dep });
            return V.Fragment(
                V.Component(SilentDependent, new Dep("S", dep), key: "s"),
                V.Div(children: new VNode[]
                {
                    dep == 0 ? V.Div(children: new VNode[] { V.Component(Probed, new Named("D"), key: "d") }) : null,
                }));
        }

        [Component]
        private static VNode Stateful()
        {
            var (value, _) = EventLog.UseState("S", 0);
            EventLog.UseEffects("S", new object[] { value });
            return V.Div();
        }

        [Component]
        private static VNode Dropper()
        {
            var (shown, _) = EventLog.UseState("M", true);
            return V.Fragment(shown ? V.Component(Probed, new Named("D"), key: "d") : null);
        }

        // The deleted child's parent is a component that follows a sibling updated in the same commit.
        [Component]
        private static VNode SiblingBeforeTheParent()
            => V.Div(children: new VNode[] { V.Component(Stateful, key: "s"), V.Component(Dropper, key: "m") });

        // Two deletions in one render: one inside an element, and one in the component's own rows after it.
        [Component]
        private static VNode TwoDeletions()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Div(children: new VNode[] { shown ? V.Component(Probed, new Named("A"), key: "a") : null }),
                shown ? V.Component(Probed, new Named("B"), key: "b") : null);
        }

        [Component]
        private static VNode InnerEmpty()
        {
            EventLog.UseEffects("Y", Array.Empty<object>());
            return V.Fragment(Array.Empty<VNode>());
        }

        [Component]
        private static VNode OuterEmpty()
        {
            EventLog.UseEffects("X", Array.Empty<object>());
            return V.Fragment(V.Component(InnerEmpty, key: "y"));
        }

        // Neither component holds a row, so the walk reaches no element for either.
        [Component]
        private static VNode EmptyHost()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            EventLog.UseRender("Root");
            return V.Fragment(shown ? V.Component(OuterEmpty, key: "x") : null);
        }

        private sealed record Keys(string[] Shown, int Dep);

        private static VNode[] KeyedRows(string[] keys, Func<VisualElement, Action> refA, Func<VisualElement, Action> refB)
        {
            var rows = new VNode[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                rows[i] = V.Div(key: keys[i], refCallback: keys[i] == "A" ? refA : refB, children: new VNode[]
                {
                    V.Component(Probed, new Named("C" + keys[i]), key: "c"),
                });
            }
            return rows;
        }

        // A flat list of keyed elements, each holding a component, so a removal takes the list's own diff.
        [Component]
        private static VNode KeyedElements()
        {
            var (keys, _) = EventLog.UseState("Keys", new[] { "A", "B" });
            EventLog.UseRender("L");
            var refA = EventLog.UseRefCallback("A");
            var refB = EventLog.UseRefCallback("B");
            return V.Div(children: KeyedRows(keys, refA, refB));
        }

        // As above, after a sibling the same render updates.
        [Component]
        private static VNode KeyedElementsAfterASibling()
        {
            var (keys, _) = EventLog.UseState("Keys", new Keys(new[] { "A", "B" }, 0));
            EventLog.UseEffects("L", new object[] { keys.Dep });
            var refA = EventLog.UseRefCallback("A");
            var refB = EventLog.UseRefCallback("B");
            return V.Fragment(
                V.Component(Dependent, new Dep("S", keys.Dep), key: "s"),
                V.Div(children: KeyedRows(keys.Shown, refA, refB)));
        }

        private sealed record Targets(VisualElement Removed, VisualElement Kept);

        [Component]
        private static VNode PortalChild(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name), children: new VNode[]
            {
                V.Component(Probed, new Named(props.Name + "C"), key: "c"),
            });
        }

        // A portal inside the removed element, and one beside it into another target that stays.
        [Component]
        private static VNode PortalInARemovedElement(Targets targets)
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            EventLog.UseRender("Root");
            var holderRef = EventLog.UseRefCallback("O");
            return V.Fragment(
                shown
                    ? V.Div(refCallback: holderRef, children: new VNode[]
                    {
                        V.Portal(targets.Removed, new VNode[] { V.Component(PortalChild, new Named("PC"), key: "pc") },
                            key: "portal"),
                    })
                    : null,
                V.Portal(targets.Kept, new VNode[] { V.Component(PortalChild, new Named("K"), key: "k") }, key: "kept"));
        }

        // A deletion in an element, and one in an element inside it.
        [Component]
        private static VNode NestedElementDeletions()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                shown ? V.Component(Probed, new Named("A"), key: "a") : null,
                V.Div(children: new VNode[] { shown ? V.Component(Probed, new Named("B"), key: "b") : null }),
            });
        }

        [Component]
        private static VNode ElementDeleting(Dep props)
            => V.Div(children: new VNode[] { props.Value == 0 ? V.Component(Probed, new Named("A"), key: "a") : null });

        // A deletion inside a child component's element, and one in this component's own rows after the child.
        [Component]
        private static VNode DeletionsAtTwoLevels()
        {
            var (hidden, _) = EventLog.UseState("Hidden", 0);
            return V.Fragment(
                V.Component(ElementDeleting, new Dep("M", hidden), key: "m"),
                hidden == 0 ? V.Component(Probed, new Named("B"), key: "b") : null);
        }

        // A deleted child ahead of a sibling that stays, for a disposal that follows the deletion.
        [Component]
        private static VNode DeletionBeforeASibling()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                shown ? V.Component(Probed, new Named("X"), key: "x") : null,
                V.Component(Probed, new Named("Y"), key: "y"));
        }

        private sealed record Hider(int Dep, StateUpdater<bool> SetShown);

        [Component]
        private static VNode PassiveOnly(Named props)
        {
            var log = EventLog.UseLog();
            Hooks.UseEffect(() => () => log.Add("eff-x:" + props.Name), Array.Empty<object>());
            return V.Div();
        }

        // Its layout effect deletes the sibling ahead of it once its dep changes, which is a second commit.
        [Component]
        private static VNode HidesItsSibling(Hider props)
        {
            var log = EventLog.UseLog();
            Hooks.UseEffect(() =>
            {
                log.Add("eff:S");
                return () => log.Add("eff-x:S");
            }, new object[] { props.Dep });
            Hooks.UseLayoutEffect(() =>
            {
                if (props.Dep == 1) props.SetShown.Invoke(false);
                return default(Action);
            }, new object[] { props.Dep });
            return V.Div();
        }

        [Component]
        private static VNode SiblingDeletedByALayoutEffect()
        {
            var (shown, setShown) = EventLog.UseState("Shown", true);
            var (dep, _) = EventLog.UseState("Dep", 0);
            return V.Fragment(
                shown ? V.Component(PassiveOnly, new Named("D"), key: "d") : null,
                V.Component(HidesItsSibling, new Hider(dep, setShown), key: "s"));
        }

        private const int SlicedRows = 4;
        private static ComponentFiber s_slicedList;
        private static int s_slicedGeneration;

        [SetUp]
        public void SetUp()
        {
            s_slicedList = null;
            s_slicedGeneration = 0;
        }

        // Keyed elements at the top level keep the list's own reconcile on the path that time-slices, as in
        // ParkedPassResumeTests; the first row holds the child the list's transition deletes.
        [Component(Compiler = false)]
        private static VNode SlicedList()
        {
            s_slicedList = FiberAmbientStack.Current;
            var generation = s_slicedGeneration;
            EventLog.UseEffects("L", new object[] { generation });
            return V.Fragment(children: SlicedRowsOf(generation));
        }

        private static VNode[] SlicedRowsOf(int generation)
        {
            var rows = new VNode[SlicedRows];
            rows[0] = V.Div(key: "row0", children: new VNode[]
            {
                generation == 0 ? V.Component(Probed, new Named("D"), key: "d") : null,
            });
            for (var i = 1; i < SlicedRows; i++)
            {
                rows[i] = V.Div(key: "row" + i, children: new VNode[] { V.Label(text: "gen" + generation) });
            }
            return rows;
        }

        // As SlicedList, with no effect of its own.
        [Component(Compiler = false)]
        private static VNode QuietSlicedList()
        {
            s_slicedList = FiberAmbientStack.Current;
            return V.Fragment(children: SlicedRowsOf(s_slicedGeneration));
        }

        [Component]
        private static VNode LayoutAndRefOnly(Named props)
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() => () => log.Add("lay-x:" + props.Name), Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode SlicedListBeforeASibling()
            => V.Fragment(V.Component(QuietSlicedList, key: "list"), V.Component(LayoutAndRefOnly, new Named("Y"), key: "y"));

        [Component(Compiler = false)]
        private static VNode LayoutCleanupThrower()
        {
            Hooks.UseLayoutEffect(
                () => () => throw new InvalidOperationException("Deletion commit order test throw"), Array.Empty<object>());
            return V.Label(text: "thrower");
        }

        [Component]
        private static VNode LabelThenChild(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Fragment(V.Label(text: props.Name), V.Component(Unreffed, new Named(props.Name + "c"), key: "c"));
        }

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryOverAThrower(Shown props)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Div(children: new VNode[] { props.Value ? V.Component(LayoutCleanupThrower, key: "t") : null });
        }

        private sealed record Shown(bool Value);

        // A component nesting another is dropped ahead of a boundary whose element deletes a component whose layout
        // cleanup throws into the boundary.
        [Component]
        private static VNode DropsNestedAheadOfACatch(Shown props)
            => V.Fragment(
                props.Value ? V.Component(LabelThenChild, new Named("P"), key: "p") : null,
                V.Component(BoundaryOverAThrower, props, key: "b"));

        [Component]
        private static VNode HostOfNestedAndACatch()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Component(DropsNestedAheadOfACatch, new Shown(shown), key: "host");
        }

        // The first row swaps its element type, and the second row's element deletes its child.
        [Component]
        private static VNode TypeSwapThenAnElementDeletion()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                shown
                    ? V.Div(children: new VNode[] { V.Component(Probed, new Named("A"), key: "a") })
                    : V.Label(text: "swapped"),
                V.Div(children: new VNode[] { shown ? V.Component(Probed, new Named("B"), key: "b") : null }),
            });
        }

        [Component]
        private static VNode EmptiedBy(Dep props)
            => V.Fragment(props.Value == 0 ? V.Component(Probed, new Named("A"), key: "a") : null);

        // A child component deletes its own child, and an element after it deletes another.
        [Component]
        private static VNode DeletionsInAChildAndAnElementAfterIt()
        {
            var (dep, _) = EventLog.UseState("Dep", 0);
            return V.Fragment(
                V.Component(EmptiedBy, new Dep("M", dep), key: "m"),
                V.Div(children: new VNode[] { dep == 0 ? V.Component(Probed, new Named("B"), key: "b") : null }));
        }

        // The deleted child sits in an element ahead of a sibling the same render updates.
        [Component]
        private static VNode ElementAheadOfASiblingThatUpdates()
        {
            var (dep, _) = EventLog.UseState("Dep", 0);
            return V.Fragment(
                V.Div(children: new VNode[] { dep == 0 ? V.Component(Probed, new Named("D"), key: "d") : null }),
                V.Component(Dependent, new Dep("F", dep), key: "f"));
        }

        [Component]
        private static VNode ListRow(Named props)
        {
            var (value, _) = EventLog.UseState(props.Name, 0);
            EventLog.UseEffects(props.Name, new object[] { value });
            return V.Label(text: props.Name);
        }

        // The deleted child sits in an element ahead of a list whose row is mounted on an element of its own.
        [Component]
        private static VNode ElementAheadOfAList()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Div(children: new VNode[] { shown ? V.Component(Probed, new Named("D"), key: "d") : null }),
                V.VirtualList(
                    items: new[] { "R" },
                    keySelector: item => item,
                    itemHeight: 50f,
                    renderer: item => V.Component(ListRow, new Named(item), key: item),
                    overscan: 0));
        }

        [Component]
        private static VNode Unreffed(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Label(text: props.Name);
        }

        [Component]
        private static VNode UnreffedHolder(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(children: new VNode[] { V.Component(Unreffed, new Named(props.Name + "i"), key: "i") });
        }

        // Two removed rows, each holding a component, in a tree with no ref anywhere.
        [Component]
        private static VNode UnreffedRows()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                shown ? V.Component(UnreffedHolder, new Named("A"), key: "a") : null,
                shown ? V.Component(UnreffedHolder, new Named("B"), key: "b") : null);
        }

        // Two siblings removed together, each its own row with a ref and nothing inside it.
        [Component]
        private static VNode TwoRemovedSiblings()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                shown ? V.Component(Probed, new Named("A"), key: "a") : null,
                shown ? V.Component(Probed, new Named("B"), key: "b") : null);
        }

        [Component]
        private static VNode Rowless(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Fragment(new VNode[] { null });
        }

        // A deleted component holding no row ahead of a sibling's row that stays, and a deletion after that row.
        [Component]
        private static VNode RowlessAheadOfAKeptRow()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                shown ? V.Component(Rowless, new Named("A"), key: "a") : null,
                V.Component(Probed, new Named("S"), key: "s"),
                shown ? V.Component(Probed, new Named("B"), key: "b") : null);
        }

        private sealed record InnerTarget(string Name, VisualElement Target, bool Shown);

        // A Portal child whose element holds a second Portal, of a list, ahead of a component.
        [Component]
        private static VNode HoldsAListPortal(InnerTarget props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name), children: new VNode[]
            {
                props.Shown
                    ? V.Portal(props.Target, new VNode[]
                    {
                        V.VirtualList(
                            items: new[] { "R" },
                            keySelector: item => item,
                            itemHeight: 50f,
                            renderer: item => V.Component(ListRow, new Named(item), key: item),
                            overscan: 0),
                    }, key: "inner")
                    : null,
                V.Component(Probed, new Named("Z"), key: "z"),
            });
        }

        private sealed record PortalTargets(VisualElement Spare, VisualElement Outer, VisualElement Inner);

        // Phase 1 takes a spare Portal away and gives the outer Portal's child its own Portal, recorded after the
        // spare's went; phase 2 removes the element around the outer Portal.
        [Component]
        private static VNode PortalOfAListPortalInARemovedElement(PortalTargets targets)
        {
            var (phase, _) = EventLog.UseState("Phase", 0);
            var holderRef = EventLog.UseRefCallback("O");
            return V.Fragment(
                phase == 0 ? V.Portal(targets.Spare, new VNode[] { V.Div() }, key: "spare") : null,
                phase < 2
                    ? V.Div(refCallback: holderRef, children: new VNode[]
                    {
                        V.Portal(targets.Outer, new VNode[]
                        {
                            V.Component(HoldsAListPortal, new InnerTarget("P", targets.Inner, phase >= 1), key: "p"),
                        }, key: "outer"),
                    })
                    : null);
        }

        private static MountedTree Mount(EventLog log, VNode root)
        {
            var mounted = V.Mount(new VisualElement(), log.Provide(root));
            mounted.DrainForTest();
            log.Clear();
            return mounted;
        }

        [Test]
        public void Given_AComponentWithNestedComponentsAndRefs_When_ItsParentStopsRenderingIt_Then_EachIsTakenDownInPreOrderAndItsPassiveCleanupsRunAfterTheCommit()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ParentHost));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Root ins-x:P lay-x:P ref-x:P ins-x:C1 lay-x:C1 ref-x:C1 ins-x:C2 lay-x:C2 ref-x:C2"
                + " eff-x:P eff-x:C1 eff-x:C2"));
        }

        [Test]
        public void Given_AnElementHoldingComponentsWithRefs_When_ItsParentStopsRenderingIt_Then_ItsRefDetachesAheadOfTheComponentsInside()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ElementHost));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Root ref-x:H ins-x:C1 lay-x:C1 ref-x:C1 ins-x:C2 lay-x:C2 ref-x:C2 eff-x:C1 eff-x:C2"));
        }

        [Test]
        public void Given_NestedComponentsHoldingNoRow_When_TheirParentStopsRenderingThem_Then_TheOuterIsTakenDownFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(EmptyHost));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Root ins-x:X lay-x:X ins-x:Y lay-x:Y eff-x:X eff-x:Y"));
        }

        [Test]
        public void Given_AKeyedElementHoldingAComponent_When_ItLeavesAListOfElements_Then_ItsRefDetachesAheadOfTheComponentInside()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(KeyedElements));

            // Act
            log.Setter<string[]>("Keys").Invoke(new[] { "B" });
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:L ref-x:A ins-x:CA lay-x:CA ref-x:CA eff-x:CA"));
        }

        [Test]
        public void Given_AKeyedElementLeavingAListAfterASiblingThatUpdates_When_ThePassivePhaseRuns_Then_TheSiblingsCleanupRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(KeyedElementsAfterASibling));
            log.Setter<Keys>("Keys").Invoke(new Keys(new[] { "B" }, 1));
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:S eff-x:CA eff-x:L eff:S eff:L"));
        }

        [Test]
        public void Given_APortalInsideARemovedElement_When_TheElementIsRemoved_Then_ThePortalsChildrenAreTakenDownWhereThePortalSits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log,
                V.Component(PortalInARemovedElement, new Targets(new VisualElement(), new VisualElement())));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Root ref-x:O ins-x:PC lay-x:PC ref-x:PC ins-x:PCC lay-x:PCC ref-x:PCC eff-x:PC eff-x:PCC"));
        }

        [Test]
        public void Given_ADeletionInAnElementAndOneInAnElementInsideIt_When_ThePassivePhaseRuns_Then_TheOuterOneRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(NestedElementDeletions));
            log.Setter<bool>("Shown").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:A eff-x:B"));
        }

        [Test]
        public void Given_ADeletionInAChildsElementAndOneInTheParentsRows_When_ThePassivePhaseRuns_Then_TheParentsRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(DeletionsAtTwoLevels));
            log.Setter<int>("Hidden").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:B eff-x:A"));
        }

        [Test]
        public void Given_AKeyedChild_When_ItsKeyChanges_Then_TheOldChildsPassiveCleanupRunsAfterTheNewChildsLayoutSetup()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(KeyChanger));

            // Act
            log.Setter<bool>("Key").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Key render:Cb ins-x:Ca lay-x:Ca ref-x:Ca ins:Cb ref:Cb lay:Cb eff-x:Ca eff:Cb"));
        }

        [Test]
        public void Given_AChild_When_ItsTypeChanges_Then_TheOldChildsPassiveCleanupRunsAfterTheNewChildsLayoutSetup()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TypeSwapper));

            // Act
            log.Setter<bool>("Type").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Type render:B ins-x:A lay-x:A ref-x:A ins:B ref:B lay:B eff-x:A eff:B"));
        }

        [Test]
        public void Given_AChildDeletedFromAnElementBesideASiblingThatUpdates_When_TheCommitRuns_Then_TheDeletionsPassiveCleanupRunsAheadOfTheSiblings()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SiblingsInOneElement));

            // Act
            log.Setter<int>("P").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:P render:S ins-x:D lay-x:D ref-x:D ins-x:S ins:S lay-x:S ins-x:P ins:P lay-x:P lay:S lay:P"
                + " eff-x:D eff-x:S eff-x:P eff:S eff:P"));
        }

        [Test]
        public void Given_AChildDeletedFromAnElementAfterASiblingThatUpdates_When_ThePassivePhaseRuns_Then_TheSiblingsCleanupRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SiblingBeforeTheElement));
            log.Setter<int>("P").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:S eff-x:D eff-x:P eff:S eff:P"));
        }

        [Test]
        public void Given_AnElementHoldingAChildDeletedAfterASiblingThatUpdates_When_ThePassivePhaseRuns_Then_TheSiblingsCleanupRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ElementDeletedAfterASibling));
            log.Setter<int>("P").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:S eff-x:D eff-x:P eff:S eff:P"));
        }

        [Test]
        public void Given_AChildDeletedByAComponentAfterASiblingThatUpdates_When_ThePassivePhaseRuns_Then_TheSiblingsCleanupRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SiblingBeforeTheParent));
            log.Setter<int>("S").Invoke(1);
            log.Setter<bool>("M").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:S eff-x:D eff:S"));
        }

        [Test]
        public void Given_ADeletionInsideAnElementAndOneAfterIt_When_ThePassivePhaseRuns_Then_TheOneTheParentHoldsDirectlyRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TwoDeletions));
            log.Setter<bool>("Shown").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:B eff-x:A"));
        }

        [Test]
        public void Given_ADeletionWhosePassiveCleanupIsStillQueued_When_TheTreeIsDisposed_Then_ItRunsAheadOfTheUnmount()
        {
            // Arrange
            var log = new EventLog();
            var mounted = Mount(log, V.Component(DeletionBeforeASibling));
            log.Setter<bool>("Shown").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:X ins-x:Y lay-x:Y ref-x:Y eff-x:Y"));
        }

        [Test]
        public void Given_ATransitionThatDeletesAChildAndParks_When_ItCommits_Then_TheChildsPassiveCleanupRunsAfterTheCommitsLayoutEffects()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SlicedList));
            s_slicedGeneration = 1;
            s_slicedList.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_slicedList.FlushStateWithTinyBudgetForTest();
            var parkedAfterTheDeletion = s_slicedList.HasPendingReconcileWorkForTest()
                && log.ToString().Contains("ins-x:D");

            // Act
            s_slicedList.DrainTimeSlicedReconcileForTest();
            mounted.FlushEffectsForTest();

            // Assert — a pass that deleted the child in the slice it committed in reads the same either way, so that
            // reading is a sentinel.
            Assert.That(parkedAfterTheDeletion ? log.ToString() : "the pass did not park after the deletion",
                Is.EqualTo("ins-x:D lay-x:D ref-x:D ins-x:L ins:L lay-x:L lay:L eff-x:D eff-x:L eff:L"));
        }

        // GREEN_ON_BASE(characterization): the base runs the first commit's passive effects first, as React does.
        // They run before the update the sibling's layout effect makes renders the deletion.
        [Test]
        public void Given_ALayoutEffectThatDeletesASiblingAheadOfIt_When_ItsUpdateCommits_Then_TheEarlierCommitsPassiveEffectsRunFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SiblingDeletedByALayoutEffect));

            // Act
            log.Setter<int>("Dep").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:S eff:S eff-x:D"));
        }

        [Test]
        public void Given_APortalInsideARemovedElementAndOneBesideItOnTheSameTarget_When_TheElementIsRemoved_Then_TheOtherPortalsChildrenStay()
        {
            // Arrange
            var log = new EventLog();
            var target = new VisualElement();
            using var mounted = Mount(log, V.Component(PortalInARemovedElement, new Targets(target, target)));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Root ref-x:O ins-x:PC lay-x:PC ref-x:PC ins-x:PCC lay-x:PCC ref-x:PCC eff-x:PC eff-x:PCC"));
        }

        [Test]
        public void Given_AnElementRemovedByATypeSwapAheadOfAnElementThatDeletesAChild_When_ThePassivePhaseRuns_Then_TheSwappedOneRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TypeSwapThenAnElementDeletion));
            log.Setter<bool>("Shown").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:A eff-x:B"));
        }

        [Test]
        public void Given_ADeletionInAChildComponentAndOneInAnElementAfterIt_When_ThePassivePhaseRuns_Then_TheChildsRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(DeletionsInAChildAndAnElementAfterIt));
            log.Setter<int>("Dep").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:A eff-x:B"));
        }

        [Test]
        public void Given_AChildDeletedFromAnElementAheadOfASiblingThatUpdates_When_ThePassivePhaseRuns_Then_TheDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ElementAheadOfASiblingThatUpdates));
            log.Setter<int>("Dep").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:D eff-x:F eff:F"));
        }

        [Test]
        public void Given_AChildDeletedFromAnElementAheadOfAListWhoseRowUpdates_When_ThePassivePhaseRuns_Then_TheDeletionRunsFirst()
        {
            // Arrange — the row renders from a range update outside any pass, the way a scroll renders one
            var log = new EventLog();
            var root = new VisualElement();
            using var mounted = V.Mount(root, log.Provide(V.Component(ElementAheadOfAList)));
            mounted.DrainForTest();
            var list = mounted.Root.Reconciler!.Context.VirtualListControllers[root.Q<ScrollView>()];
            list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);
            mounted.DrainForTest();
            log.Setter<bool>("Shown").Invoke(false);
            log.Setter<int>("R").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:D eff-x:R eff:R"));
        }

        [Test]
        public void Given_RowsHoldingComponentsInATreeWithNoRef_When_TheirParentStopsRenderingThem_Then_EachRowIsTakenDownBeforeTheNext()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(UnreffedRows));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "ins-x:A lay-x:A ins-x:Ai lay-x:Ai ins-x:B lay-x:B ins-x:Bi lay-x:Bi eff-x:A eff-x:Ai eff-x:B eff-x:Bi"));
        }

        [Test]
        public void Given_AChildOfAComponentWhoseRowsFollowASiblings_When_ItsParentStopsRenderingIt_Then_ItIsTakenDownAheadOfItsElement()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SiblingBeforeTheParent));

            // Act
            log.Setter<bool>("M").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:D lay-x:D ref-x:D eff-x:D"));
        }

        [Test]
        public void Given_TwoSiblingsWithRefsRemovedTogether_When_TheirParentStopsRenderingThem_Then_EachIsTakenDownWithItsRefBeforeTheNext()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TwoRemovedSiblings));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "ins-x:A lay-x:A ref-x:A ins-x:B lay-x:B ref-x:B eff-x:A eff-x:B"));
        }

        [Test]
        public void Given_AComponentHoldingNoRowAheadOfARowThatStays_When_ItAndARowAfterAreDeleted_Then_ItIsTakenDownFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(RowlessAheadOfAKeptRow));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:A lay-x:A ins-x:B lay-x:B ref-x:B eff-x:A eff-x:B"));
        }

        [Test]
        public void Given_APortalChildHoldingAPortalOfAList_When_TheElementAroundTheOuterPortalIsRemoved_Then_TheListsRowIsTakenDownWhereItsPortalSits()
        {
            // Arrange — the list holds its row once a range update has placed it
            var log = new EventLog();
            var inner = new VisualElement();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(PortalOfAListPortalInARemovedElement,
                new PortalTargets(new VisualElement(), new VisualElement(), inner))));
            mounted.DrainForTest();
            log.Setter<int>("Phase").Invoke(1);
            mounted.DrainForTest();
            mounted.Root.Reconciler!.Context.VirtualListControllers[inner.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Phase").Invoke(2);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "ref-x:O ins-x:P lay-x:P ref-x:P ins-x:R lay-x:R ins-x:Z lay-x:Z ref-x:Z eff-x:P eff-x:R eff-x:Z"));
        }

        [Test]
        public void Given_ATransitionParkedAfterDeletingAChild_When_TheTreeIsDisposed_Then_TheChildsPassiveCleanupRunsAfterTheUnmountsLayoutCleanups()
        {
            // Arrange — React commits no deletion of a parked transition, so the unmount deletes the child
            var log = new EventLog();
            var mounted = Mount(log, V.Component(SlicedListBeforeASibling));
            s_slicedGeneration = 1;
            s_slicedList.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_slicedList.FlushStateWithTinyBudgetForTest();
            mounted.FlushEffectsForTest();
            var heldWhileParked = s_slicedList.HasPendingReconcileWorkForTest()
                && log.ToString() == "ins-x:D lay-x:D ref-x:D";
            log.Clear();

            // Act
            mounted.Dispose();

            // Assert — a child deleted in no slice, or one whose cleanup ran while parked, reads otherwise, so that
            // reading is a sentinel.
            Assert.That(heldWhileParked ? log.ToString() : "the deletion was not held while parked",
                Is.EqualTo("lay-x:Y ref-x:Y eff-x:D"));
        }

        [Test]
        public void Given_ANestedComponentDroppedAheadOfABoundaryThatCatchesALayoutCleanup_When_TheRenderCommits_Then_TheOuterIsTakenDownFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(HostOfNestedAndACatch)),
                CaughtErrors.Unlogged);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:P lay-x:P ins-x:Pc lay-x:Pc eff-x:P eff-x:Pc"));
        }
    }
}
