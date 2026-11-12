using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the order a commit takes deleted components down in, as one <see cref="EventLog"/> string per case, each
    /// the sequence react-reconciler 19.1's deletion walk (<c>commitDeletionEffectsOnFiber</c>) produces for the
    /// same tree: a component's insertion and layout destroys run, and an element's ref detaches, where its
    /// pre-order walk reaches them. The probes register no passive effect: a deleted component's <c>UseEffect</c>
    /// cleanup runs in that walk here, and after the commit in React. The case of Portals into a registered id's rows
    /// reads components React keeps: the children of Portals declared elsewhere into elements a deletion removes, which
    /// the walk reaches no place for and takes down after it, in the order it collected them.
    /// </summary>
    [TestFixture]
    internal sealed class DeletionCommitOrderEventLogTests
    {
        private sealed record Named(string Name);

        // EventLog.UseEffects without its passive effect.
        private static void UseInsertionAndLayoutEffects(string name, object[] deps)
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() => Record(log, "ins", name), deps);
            Hooks.UseLayoutEffect(() => Record(log, "lay", name), deps);
        }

        private static Action Record(EventLog log, string kind, string name)
        {
            log.Add(kind + ":" + name);
            return () => log.Add(kind + "-x:" + name);
        }

        [Component]
        private static VNode Probed(Named props)
        {
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode DeletedParent()
        {
            UseInsertionAndLayoutEffects("P", Array.Empty<object>());
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
            UseInsertionAndLayoutEffects("Root", Array.Empty<object>());
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
        private static VNode Stateful()
        {
            var (value, _) = EventLog.UseState("S", 0);
            UseInsertionAndLayoutEffects("S", new object[] { value });
            return V.Div();
        }

        [Component]
        private static VNode Dropper()
        {
            var (shown, _) = EventLog.UseState("M", true);
            return V.Fragment(shown ? V.Component(Probed, new Named("D"), key: "d") : null);
        }

        // The deleted child's parent is a component whose rows follow a sibling's.
        [Component]
        private static VNode SiblingBeforeTheParent()
            => V.Div(children: new VNode[] { V.Component(Stateful, key: "s"), V.Component(Dropper, key: "m") });

        [Component]
        private static VNode InnerEmpty()
        {
            UseInsertionAndLayoutEffects("Y", Array.Empty<object>());
            return V.Fragment(Array.Empty<VNode>());
        }

        [Component]
        private static VNode OuterEmpty()
        {
            UseInsertionAndLayoutEffects("X", Array.Empty<object>());
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

        private sealed record Targets(VisualElement Removed, VisualElement Kept);

        [Component]
        private static VNode PortalChild(Named props)
        {
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
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
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
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

        [Component]
        private static VNode ListRow(Named props)
        {
            var (value, _) = EventLog.UseState(props.Name, 0);
            UseInsertionAndLayoutEffects(props.Name, new object[] { value });
            return V.Label(text: props.Name);
        }

        [Component]
        private static VNode Unreffed(Named props)
        {
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Label(text: props.Name);
        }

        [Component]
        private static VNode UnreffedHolder(Named props)
        {
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
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
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
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
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
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

        [Component]
        private static VNode Wrapping(Named props)
        {
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Component(Probed, new Named(props.Name + "i"), key: "i");
        }

        [Component]
        private static VNode WrappingTwice(Named props)
        {
            UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Component(Wrapping, new Named(props.Name + "i"), key: "i");
        }

        // A component holding no row ahead of one that renders another with no element between them, so all three
        // start at the innermost one's element.
        [Component]
        private static VNode RowlessAheadOfAWrapper()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                shown ? V.Component(Rowless, new Named("X"), key: "x") : null,
                shown ? V.Component(Wrapping, new Named("O"), key: "o") : null);
        }

        // Keyed elements, each holding three components nested with no element between them, so all three start at
        // the innermost one's element.
        [Component]
        private static VNode KeyedWrappedRows()
        {
            var (keys, _) = EventLog.UseState("WrappedKeys", new[] { "A", "B" });
            var refA = EventLog.UseRefCallback("A");
            var refB = EventLog.UseRefCallback("B");
            var rows = new VNode[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                rows[i] = V.Div(key: keys[i], refCallback: keys[i] == "A" ? refA : refB, children: new VNode[]
                {
                    V.Component(WrappingTwice, new Named("W" + keys[i]), key: "w"),
                });
            }
            return V.Div(children: rows);
        }

        private sealed record Target(VisualElement Element);

        // Keyed Portals into one target, each of two elements with a ref and no component, as a list's own rows.
        [Component]
        private static VNode KeyedPortals(Target target)
        {
            var (keys, _) = EventLog.UseState("PortalKeys", new[] { "A", "B" });
            var refA1 = EventLog.UseRefCallback("A1");
            var refA2 = EventLog.UseRefCallback("A2");
            var refB1 = EventLog.UseRefCallback("B1");
            var refB2 = EventLog.UseRefCallback("B2");
            var portals = new VNode[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                var isA = keys[i] == "A";
                portals[i] = V.Portal(target.Element, new VNode[]
                {
                    V.Div(refCallback: isA ? refA1 : refB1),
                    V.Div(refCallback: isA ? refA2 : refB2),
                }, key: keys[i]);
            }
            return V.Div(children: portals);
        }

        // Keyed elements in a tree with no ref, each holding a component ahead of a Portal of another.
        [Component]
        private static VNode KeyedRowsWithAPortal(Target target)
        {
            var (keys, _) = EventLog.UseState("PortalRowKeys", new[] { "A", "B" });
            var rows = new VNode[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                rows[i] = V.Div(key: keys[i], children: new VNode[]
                {
                    V.Component(Unreffed, new Named("D" + keys[i]), key: "d"),
                    V.Portal(target.Element, new VNode[] { V.Component(Unreffed, new Named("C" + keys[i]), key: "c") },
                        key: "p"),
                });
            }
            return V.Div(children: rows);
        }

        private const string RegisteredId = "deletion-commit-order-target";

        [TearDown]
        public void TearDown() => RuntimeStateProbe.ClearPortalRegistry();

        // A Portal into a registered id, whose element holds a component.
        [Component]
        private static VNode RegisteredPortal()
        {
            var holderRef = EventLog.UseRefCallback("R");
            return V.Div(children: new VNode[]
            {
                V.Portal(RegisteredId, new VNode[]
                {
                    V.Div(refCallback: holderRef, children: new VNode[] { V.Component(Probed, new Named("C"), key: "c") }),
                }),
            });
        }

        private sealed class RowTargets
        {
            internal VisualElement First;
            internal VisualElement Second;
        }

        // A Portal into a registered id whose two rows each hold an element another Portal renders into.
        [Component]
        private static VNode RetargetedRows(RowTargets targets)
        {
            var keepFirst = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                targets.First = element;
                return null;
            }, Array.Empty<object>());
            var keepSecond = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                targets.Second = element;
                return null;
            }, Array.Empty<object>());
            return V.Portal(RegisteredId, new VNode[]
            {
                V.Div(key: "r1", children: new VNode[] { V.Div(refCallback: keepFirst) }),
                V.Div(key: "r2", children: new VNode[] { V.Div(refCallback: keepSecond) }),
            });
        }

        // Beside those rows, once aimed, a Portal into each row's element, of a component holding no row.
        [Component]
        private static VNode PortalsIntoRetargetedRows(RowTargets targets)
        {
            var (aimed, _) = EventLog.UseState("Aimed", false);
            return V.Fragment(
                V.Component(RetargetedRows, targets, key: "rows"),
                aimed
                    ? V.Portal(targets.First, new VNode[] { V.Component(Rowless, new Named("Q1"), key: "q1") }, key: "q1")
                    : null,
                aimed
                    ? V.Portal(targets.Second, new VNode[] { V.Component(Rowless, new Named("Q2"), key: "q2") }, key: "q2")
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
        public void Given_AComponentWithNestedComponentsAndRefs_When_ItsParentStopsRenderingIt_Then_EachIsTakenDownInPreOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ParentHost));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Root ins-x:P lay-x:P ref-x:P ins-x:C1 lay-x:C1 ref-x:C1 ins-x:C2 lay-x:C2 ref-x:C2"));
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
                "render:Root ref-x:H ins-x:C1 lay-x:C1 ref-x:C1 ins-x:C2 lay-x:C2 ref-x:C2"));
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
            Assert.That(log.ToString(), Is.EqualTo("render:Root ins-x:X lay-x:X ins-x:Y lay-x:Y"));
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
            Assert.That(log.ToString(), Is.EqualTo("render:L ref-x:A ins-x:CA lay-x:CA ref-x:CA"));
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
                "render:Root ref-x:O ins-x:PC lay-x:PC ref-x:PC ins-x:PCC lay-x:PCC ref-x:PCC"));
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
                "render:Root ref-x:O ins-x:PC lay-x:PC ref-x:PC ins-x:PCC lay-x:PCC ref-x:PCC"));
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
            Assert.That(log.ToString(), Is.EqualTo("ins-x:A lay-x:A ins-x:Ai lay-x:Ai ins-x:B lay-x:B ins-x:Bi lay-x:Bi"));
        }

        // GREEN_ON_BASE(characterization): the base runs an orphan's cleanups ahead of its container's removals.
        // The walk finds the orphan's row from its slot less the container's first (DeletionCommit.PlaceDeleted), which
        // is what this pins.
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
            Assert.That(log.ToString(), Is.EqualTo("ins-x:D lay-x:D ref-x:D"));
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
            Assert.That(log.ToString(), Is.EqualTo("ins-x:A lay-x:A ref-x:A ins-x:B lay-x:B ref-x:B"));
        }

        // GREEN_ON_BASE(characterization): the base runs an orphan's cleanups ahead of its container's removals.
        // The walk takes an orphan whose slot holds a row that stays down at that row (DeletionCommit.Run), which is
        // what this pins.
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
            Assert.That(log.ToString(), Is.EqualTo("ins-x:A lay-x:A ins-x:B lay-x:B ref-x:B"));
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
                "ref-x:O ins-x:P lay-x:P ref-x:P ins-x:R lay-x:R ins-x:Z lay-x:Z ref-x:Z"));
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
            Assert.That(log.ToString(), Is.EqualTo("ins-x:P lay-x:P ins-x:Pc lay-x:Pc"));
        }

        [Test]
        public void Given_AKeyedPortalOfTwoElementsWithRefs_When_ItLeavesAListOfPortals_Then_TheirRefsDetachInTheirOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(KeyedPortals, new Target(new VisualElement())));

            // Act
            log.Setter<string[]>("PortalKeys").Invoke(new[] { "B" });
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ref-x:A1 ref-x:A2"));
        }

        [Test]
        public void Given_APortalIntoARegisteredId_When_TheIdIsRegisteredToAnotherElement_Then_ItsChildrenAreTakenDownInPreOrderBeforeTheyMountAgain()
        {
            // Arrange
            var log = new EventLog();
            FiberPortalRegistry.Register(RegisteredId, new VisualElement());
            using var mounted = Mount(log, V.Component(RegisteredPortal));
            LogAssert.Expect(LogType.Warning, $"[FiberPortalRegistry] Id \"{RegisteredId}\" is already registered. Overwriting.");

            // Act
            FiberPortalRegistry.Register(RegisteredId, new VisualElement());
            mounted.FlushStateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ref-x:R ins-x:C lay-x:C ref-x:C ins:C ref:C lay:C ref:R"));
        }

        [Test]
        public void Given_AKeyedElementHoldingNestedComponentsWithNoElementBetween_When_ItLeavesAListOfElements_Then_TheOutermostIsTakenDownFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(KeyedWrappedRows));

            // Act
            log.Setter<string[]>("WrappedKeys").Invoke(new[] { "B" });
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "ref-x:A ins-x:WA lay-x:WA ins-x:WAi lay-x:WAi ins-x:WAii lay-x:WAii ref-x:WAii"));
        }

        [Test]
        public void Given_AKeyedElementHoldingAComponentAheadOfAPortalInATreeWithNoRef_When_ItLeavesAListOfElements_Then_TheComponentIsTakenDownFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(KeyedRowsWithAPortal, new Target(new VisualElement())));

            // Act
            log.Setter<string[]>("PortalRowKeys").Invoke(new[] { "B" });
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:DA lay-x:DA ins-x:CA lay-x:CA"));
        }

        [Test]
        public void Given_AComponentHoldingNoRowAheadOfNestedComponents_When_TheirParentStopsRenderingThem_Then_TheyAreTakenDownInTreeOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(RowlessAheadOfAWrapper));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ins-x:O lay-x:O ins-x:Oi lay-x:Oi ref-x:Oi"));
        }

        [Test]
        public void Given_PortalsIntoTheRowsOfAPortalIntoARegisteredId_When_TheIdIsRegisteredToAnotherElement_Then_TheirChildrenAreTakenDownInTheRowsOrder()
        {
            // Arrange
            var log = new EventLog();
            FiberPortalRegistry.Register(RegisteredId, new VisualElement());
            using var mounted = Mount(log, V.Component(PortalsIntoRetargetedRows, new RowTargets()));
            log.Setter<bool>("Aimed").Invoke(true);
            mounted.DrainForTest();
            log.Clear();
            LogAssert.Expect(LogType.Warning, $"[FiberPortalRegistry] Id \"{RegisteredId}\" is already registered. Overwriting.");

            // Act
            FiberPortalRegistry.Register(RegisteredId, new VisualElement());
            mounted.FlushStateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:Q1 lay-x:Q1 ins-x:Q2 lay-x:Q2"));
        }
    }
}
