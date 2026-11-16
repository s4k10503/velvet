using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where in the commit a deletion's take-downs run: at the turn of the fiber that kept the parent of what was
    /// deleted, as react-reconciler 19.1's <c>commitMutationEffectsOnFiber</c> runs a fiber's deletions ahead of its
    /// children, and not where the container that holds both is done reconciling. The order each case expects is that
    /// function's for the same trees, read from its source rather than run, except in the two that read the label's text
    /// (<c>@b2</c>): they read it after the flush of a patch applied at once wrote it, where React runs the deletion
    /// ahead of the label's update and reads <c>b</c>.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class DeletionAtOwnerTurnTests
    {
        private readonly EventLogRenderer _renderer;

        public DeletionAtOwnerTurnTests(EventLogRenderer renderer) => _renderer = renderer;

        private sealed record Named(string Name);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div();
        }

        // Deletes a component of its own output, whose rows follow a label of its own.
        [Component]
        private static VNode DropsItsLeaf(bool shown)
            => V.Fragment(V.Label(text: "b"), shown ? V.Component(Probed, new Named("E"), key: "e") : null);

        // Holds a component that an element of its own deletes.
        [Component]
        private static VNode HoldsAnElementDroppingOne(bool shown)
            => V.Div(children: new VNode[] { shown ? V.Component(Probed, new Named("D"), key: "d") : null });

        [Component]
        private static VNode OwnerThenElement()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsItsLeaf, shown, key: "b"),
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(Probed, new Named("D"), key: "d") : null }));
        }

        [Component]
        private static VNode ElementThenOwner()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(HoldsAnElementDroppingOne, shown, key: "u"),
                V.Component(DropsItsLeaf, shown, key: "b"));
        }

        // Deletes a component of its own output and one of the container's, which sits behind the owner's rows.
        [Component]
        private static VNode OwnerThenAComponentOfTheContainer()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsItsLeaf, shown, key: "b"),
                shown ? V.Component(Probed, new Named("F"), key: "f") : null);
        }

        private static readonly ComponentContext<int> s_context = ComponentContext<int>.Create(0);

        // Deletes a component of its own output, ahead of whatever follows it in the container.
        [Component]
        private static VNode DropsX(bool shown)
            => V.Fragment(shown ? V.Component(Probed, new Named("X"), key: "x") : null);

        [Component]
        private static VNode XThenAProvider()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsX, shown, key: "l"),
                V.Provider(s_context, 1, new VNode[] { shown ? V.Component(Probed, new Named("E"), key: "e") : null }));
        }

        [Component]
        private static VNode XThenASuspense()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsX, shown, key: "l"),
                V.Suspense(fallback: V.Div(name: "loading"), children: new VNode[]
                {
                    shown ? V.Component(Probed, new Named("E"), key: "e") : null,
                }));
        }

        // GREEN_ON_BASE(characterization): the base takes one plan over the container in tree order, which is React's: the
        // Provider is a fiber of its own there, and its deletion runs at its turn, after the component's before it.
        [Test]
        public void Given_AComponentDeletingBesideAProviderDeletingAComponent_When_BothAreDeleted_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(XThenAProvider)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ins-x:E lay-x:E"));
        }

        // GREEN_ON_BASE(characterization): the base's one plan takes the container in tree order, as React reaches the
        // Suspense fiber after the component before it.
        [Test]
        public void Given_AComponentDeletingBesideASuspenseDeletingAComponent_When_BothAreDeleted_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(XThenASuspense)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ins-x:E lay-x:E"));
        }

        // Deletes two components of its own output.
        [Component]
        private static VNode DropsTwo(bool shown)
            => V.Fragment(
                V.Label(text: "b"),
                shown ? V.Component(Probed, new Named("E1"), key: "e1") : null,
                shown ? V.Component(Probed, new Named("E2"), key: "e2") : null);

        [Component]
        private static VNode TwoDroppedByOneOwner()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(V.Component(DropsTwo, shown, key: "b"));
        }

        // GREEN_ON_BASE(characterization): the base's one plan takes the owner's rows in order.
        [Test]
        public void Given_AnOwnerDeletingTwoComponents_When_BothAreDeleted_Then_TheyAreTakenDownInTheirOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(TwoDroppedByOneOwner)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:E1 lay-x:E1 ins-x:E2 lay-x:E2"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardDeletingItsLeaf(bool shown)
        {
            Hooks.UseFallback(_ => V.Div(name: "fallback"));
            return V.Fragment(V.Label(text: "g"), shown ? V.Component(Probed, new Named("T"), key: "t") : null);
        }

        [Component]
        private static VNode BoundaryThenElement()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(GuardDeletingItsLeaf, shown, key: "g"),
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(Probed, new Named("D"), key: "d") : null }));
        }

        [Test]
        public void Given_AnErrorBoundaryDeletingItsLeafAheadOfAnElementDeletingAComponent_When_BothAreDeleted_Then_TheBoundarysDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(BoundaryThenElement)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:T lay-x:T ins-x:D lay-x:D"));
        }

        private static Label s_label;

        // Logs the text of the label beside it where its layout cleanup runs, which the label's own write has reached only
        // where the pass applied it before then.
        [Component]
        private static VNode ProbedAtTheLabel(Named props)
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() => () => log.Add("ins-x:" + props.Name), Array.Empty<object>());
            Hooks.UseLayoutEffect(() => () => log.Add("lay-x:" + props.Name + "@" + s_label.text), Array.Empty<object>());
            return V.Div();
        }

        // Its second row is a delayed field, which the walk patches at once, applying the writes recorded before it, the
        // label's among them.
        [Component]
        private static VNode DropsBesideADelayedField(bool shown)
            => V.Fragment(
                V.Label(text: shown ? "b" : "b2", refCallback: element =>
                {
                    s_label = (Label)element;
                    return () => { };
                }),
                V.TextField(value: shown ? "1" : "2", isDelayed: true),
                shown ? V.Component(ProbedAtTheLabel, new Named("E"), key: "e") : null);

        [Component]
        private static VNode DelayedFieldBeforeTheDeletions()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsBesideADelayedField, shown, key: "b"),
                shown ? V.Component(Probed, new Named("F"), key: "f") : null);
        }

        [Test]
        public void Given_APatchAppliedAtOnceInTheWalkBeforeTwoDeletions_When_BothAreDeleted_Then_TheContainersDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(DelayedFieldBeforeTheDeletions)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert — the label's text is what the flush that applied its write ahead of the walk's end leaves for E's cleanup
            Assert.That(log.ToString(), Is.EqualTo("ins-x:F lay-x:F ins-x:E lay-x:E@b2"));
        }

        [Component]
        private static VNode ListRow(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div();
        }

        // Holds a list whose row is a component that mounts its own wrapper element, which a deletion takes down where it is
        // decided rather than where the pass applies its writes.
        [Component]
        private static VNode HoldsAList(bool shown)
            => V.Fragment(shown
                ? V.Div(children: new VNode[]
                {
                    V.VirtualList(
                        items: new[] { "R" },
                        keySelector: item => item,
                        itemHeight: 50f,
                        renderer: item => V.Component(ListRow, new Named(item), key: item),
                        overscan: 0),
                })
                : null);

        [Component]
        private static VNode AComponentThenAList()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                shown ? V.Component(Probed, new Named("A"), key: "a") : null,
                V.Component(HoldsAList, shown, key: "b"));
        }

        // GREEN_ON_BASE(characterization): the base decides whether to defer once for the container, and a list's row keeps
        // the whole deletion in the walk, in tree order.
        [Test]
        public void Given_AComponentDeletedAheadOfAListWhoseRowCannotWait_When_BothAreDeleted_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            var root = new VisualElement();
            using var mounted = V.Mount(root, log.Provide(V.Component(AComponentThenAList)));
            mounted.DrainForTest();
            mounted.Root.Reconciler!.Context.VirtualListControllers[root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:A lay-x:A ins-x:R lay-x:R"));
        }

        [Test]
        public void Given_AnOwnerDeletingItsLeafAheadOfAComponentTheContainerDeletes_When_BothAreDeleted_Then_TheContainersDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenAComponentOfTheContainer)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:F lay-x:F ins-x:E lay-x:E"));
        }

        [Test]
        public void Given_AnOwnerDeletingItsLeafAheadOfAnElementThatDeletesAComponent_When_BothAreDeleted_Then_TheOwnersDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenElement)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:E lay-x:E ins-x:D lay-x:D"));
        }

        // GREEN_ON_BASE(characterization): the base queues the deletion of the nested container ahead of the outer one, which is
        // React's order for a nested container that comes first. A deletion hoisted to the start of its container instead of
        // to its owner's turn fails this case.
        [Test]
        public void Given_AnElementDeletingAComponentAheadOfAnOwnerDeletingItsLeaf_When_BothAreDeleted_Then_TheElementsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(ElementThenOwner)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:D lay-x:D ins-x:E lay-x:E"));
        }

        // Renders nothing, so its deletion holds no row of the container's.
        [Component]
        private static VNode Rowless(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Fragment(Array.Empty<VNode>());
        }

        [Component]
        private static VNode DropsARowless(bool shown)
            => V.Fragment(shown ? V.Component(Rowless, new Named("A"), key: "a") : null);

        [Component]
        private static VNode RowlessThenAList()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsARowless, shown, key: "l"),
                V.Component(HoldsAList, shown, key: "b"));
        }

        // GREEN_ON_BASE(characterization): the base's one plan places a component that renders nothing at the row its
        // slot names, so it is taken down in tree order.
        [Test]
        public void Given_AComponentRenderingNothingDeletedAheadOfAListWhoseRowCannotWait_When_BothAreDeleted_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            var root = new VisualElement();
            using var mounted = V.Mount(root, log.Provide(V.Component(RowlessThenAList)));
            mounted.DrainForTest();
            mounted.Root.Reconciler!.Context.VirtualListControllers[root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:A lay-x:A ins-x:R lay-x:R"));
        }

        [Component]
        private static VNode RowlessThenADelayedField()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsARowless, shown, key: "l"),
                V.Component(DropsBesideADelayedField, shown, key: "b"));
        }

        // GREEN_ON_BASE(characterization): the base's one plan takes a component that renders nothing down in tree order.
        [Test]
        public void Given_AComponentRenderingNothingDeletedAheadOfAnOwnerWhoseDelayedFieldFlushesTheWalk_When_BothAreDeleted_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(RowlessThenADelayedField)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert — the label's text is what the flush that applied its write ahead of the walk's end leaves for E's cleanup
            Assert.That(log.ToString(), Is.EqualTo("ins-x:A lay-x:A ins-x:E lay-x:E@b2"));
        }

        [Component]
        private static VNode DivWithAFragmentThenAFragment()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Div(key: "x", children: new VNode[]
                {
                    V.Label(text: "l"),
                    V.Fragment(shown ? V.Component(Probed, new Named("D"), key: "d") : null),
                }),
                V.Fragment(shown ? V.Component(Probed, new Named("E"), key: "e") : null));
        }

        // GREEN_ON_BASE(characterization): the base queues the element's container ahead of the outer one, which is React's
        // order for a Fragment of an element's children before the Fragment after the element. The two Fragments sit at the
        // same position of two walks, which a turn named by position alone cannot tell apart.
        [Test]
        public void Given_AFragmentInAnElementAheadOfAFragmentOfItsOwner_When_BothDeleteAComponent_Then_TheElementsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(DivWithAFragmentThenAFragment)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:D lay-x:D ins-x:E lay-x:E"));
        }

        [Component]
        private static VNode XThenAProviderDroppingAFragment()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsX, shown, key: "l"),
                V.Provider(s_context, 1, new VNode[]
                {
                    shown ? V.Fragment(new VNode[] { V.Component(Probed, new Named("E"), key: "e") }, key: "k") : null,
                }));
        }

        // GREEN_ON_BASE(characterization): the base's one plan takes the container in tree order, as React reaches the
        // Provider after the component before it and deletes the Fragment under it there.
        [Test]
        public void Given_AProviderDroppingAFragmentHoldingAComponent_When_AComponentBeforeItDeletesToo_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(XThenAProviderDroppingAFragment)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ins-x:E lay-x:E"));
        }

        [Component]
        private static VNode DropsCAndX()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsX, shown, key: "l"),
                V.Memoized(() => shown ? V.Component(Probed, new Named("C"), key: "c") : V.Label(text: "none"), shown));
        }

        // The component the memoized element holds is a child of the component that rendered it, whose deletions run ahead of
        // those of its children.
        [Test]
        public void Given_AMemoizedElementDroppingAComponentAfterAComponentThatDeletesToo_When_BothAreDeleted_Then_TheMemoizedOnesDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(DropsCAndX)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:C lay-x:C ins-x:X lay-x:X"));
        }

        private sealed class Gate
        {
            internal VelvetTaskCompletionSource<int> Source;
        }

        private sealed record ReaderProps(int Blocked, Gate Gate);

        [Component]
        private static VNode Reader(ReaderProps props)
        {
            var value = Hooks.Use(() => props.Blocked > 0
                ? (props.Gate.Source = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), props.Blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode XThenASuspenseShowingItsFallback(Gate gate)
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            var (blocked, _) = EventLog.UseState("Block", 0);
            return V.Fragment(
                V.Component(DropsX, shown, key: "l"),
                V.Suspense(
                    fallback: V.Component(Probed, new Named("F"), key: "f"),
                    children: new VNode[] { V.Component(Reader, new ReaderProps(blocked, gate), key: "reader") }));
        }

        // GREEN_ON_BASE(characterization): the base's one plan takes the container in tree order, as React deletes the
        // fallback at the Suspense's turn, after the component before it.
        [Test]
        public void Given_ASuspenseRevealingItsPrimary_When_AComponentBeforeItDeletesInTheSamePass_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            using var mounted = _renderer.Mount(log.Provide(V.Component(XThenASuspenseShowingItsFallback, gate)),
                new MountOptions { OnCaughtError = (_, _) => { } });
            mounted.DrainForTest();
            log.Setter<int>("Block").Invoke(1);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Block").Invoke(0);
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ins-x:F lay-x:F"));
        }

        [Test]
        public void Given_APassThatDeletedAtTwoOwnersTurns_When_ItHasEnded_Then_ItHoldsNoAnchor()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(OwnerThenAComponentOfTheContainer)));
            mounted.DrainForTest();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert — a pass with no such list reads as a count the expected one is not
            Assert.That(ReconcilePassProbe.AnchorsInUse(mounted), Is.EqualTo(0));
        }

        [Test]
        public void Given_AnAnchorThatHeldTeardownsInAnEarlierPass_When_ALaterPassRentsItAgain_Then_ItsDeletionsRunTheirCleanupsOnce()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenAComponentOfTheContainer)));
            mounted.DrainForTest();
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();
            log.Setter<bool>("Shown").Invoke(true);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:F lay-x:F ins-x:E lay-x:E"));
        }

        [Test]
        public void Given_AContainerDeletingAtTwoOwnersTurns_When_BothHaveEnded_Then_BothWalksAreBackInThePool()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenAComponentOfTheContainer)));
            mounted.DrainForTest();
            DeletionCommitPool.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(DeletionCommitPool.Walks().Count, Is.EqualTo(2));
        }

        [TestCase("_anchor")]
        [TestCase("_oldFibers")]
        [TestCase("NextGroup")]
        public void Given_AContainerDeletingAtTwoOwnersTurns_When_BothHaveEnded_Then_NoPooledWalkKeepsWhatItNamed(string field)
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenAComponentOfTheContainer)));
            mounted.DrainForTest();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert — a walk with no such field reads as a count the expected one is not
            Assert.That(DeletionCommitPool.WalksHolding(field), Is.EqualTo(0));
        }

        [Test]
        public void Given_AContainerDeletingAtTwoOwnersTurns_When_BothHaveEnded_Then_NoPooledWalkKeepsAnOrphanItTookDown()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenAComponentOfTheContainer)));
            mounted.DrainForTest();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert — a walk with no such field reads as a count the expected one is not
            Assert.That(DeletionCommitPool.EntriesInWalks("OwnOldFibers"), Is.EqualTo(0));
        }

        private static ComponentFiber s_flushedOwner;

        [Component]
        private static VNode DropsAProbeOfTheFlushGate()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            EventLog.UseState("Bumped", 0);
            s_flushedOwner = FiberAmbientStack.Current;
            return V.Fragment(shown ? V.Component(ProbesTheFlushGate, key: "p") : null);
        }

        // Where its layout cleanup runs, asks the root for an update and a synchronous flush, and records how often its
        // owner rendered inside that flush.
        [Component]
        private static VNode ProbesTheFlushGate()
        {
            var log = EventLog.UseLog();
            var scheduler = FiberAmbientStack.Current!.Reconciler!.Context.BatchScheduler;
            Hooks.UseLayoutEffect(() => () =>
            {
                var before = s_flushedOwner.RenderCount;
                log.Setter<int>("Bumped").Invoke(1);
                scheduler.FlushImmediate();
                log.Add("rendered inside:" + (s_flushedOwner.RenderCount - before));
            }, Array.Empty<object>());
            return V.Div();
        }

        [Test]
        public void Given_ADeletionRenderedOutsideADrain_When_ItsCleanupAsksForAFlushWhereThePassAppliesItsWrites_Then_TheRootDoesNotRenderInsideIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(DropsAProbeOfTheFlushGate)));
            mounted.DrainForTest();
            log.Clear();
            log.Setter<bool>("Shown").Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_flushedOwner);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("rendered inside:0"));
        }

        private static Button s_otherButton;
        private static int s_otherRenders;
        private static int s_otherBefore;
        private static string s_seenInside;
        private static string s_shared;
        private static string s_ref;
        private static string s_otherSawRef;
        private static string s_otherSaw;
        private static int s_otherPassive;
        private static int s_otherPassiveBefore;
        private static bool s_otherClickQueues;
        private static bool s_clickOnCleanup;
        private static StateUpdater<int> s_setOther;
        private static ComponentFiber s_clickerOwner;
        private static StateUpdater<bool> s_showClicker;

        [Component]
        private static VNode AnotherRoot()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setOther = setCount;
            s_otherRenders++;
            s_otherSaw = s_shared;
            s_otherSawRef = s_ref;
            s_otherSawPassive = s_passive;
            Hooks.UseEffect(() =>
            {
                s_otherPassive++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Button(text: count.ToString(), onClick: () =>
            {
                if (s_otherClickQueues) setCount.Invoke(count + 1);
            }, refCallback: element =>
            {
                s_otherButton = (Button)element;
                return () => { };
            });
        }

        // Clicks the button of another root where its layout cleanup runs, and records how often that root has rendered.
        [Component]
        private static VNode ClicksAnotherRoot()
        {
            Hooks.UseLayoutEffect(() => () =>
            {
                if (!s_clickOnCleanup) return;
                s_otherButton.SimulateClick();
                s_seenInside = "inside:" + (s_otherRenders - s_otherBefore);
            }, Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode OwnerOfTheClicker()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_clickerOwner = FiberAmbientStack.Current;
            s_showClicker = setShown;
            Hooks.UseLayoutEffect(() =>
            {
                s_shared = "after the owner";
                return (Action)null;
            }, new object[] { shown });
            return V.Fragment(
                shown ? V.Component(ClicksAnotherRoot, key: "c") : null,
                // Captures shown, so that each render makes a new callback and the ref is attached again; a callback that
                // captured nothing would be one cached delegate, which a patch carrying it leaves installed.
                V.Div(refCallback: _ =>
                {
                    s_ref = shown ? "attached by the owner, shown" : "attached by the owner";
                    return () => { };
                }));
        }

        private static (MountedTree Owner, MountedTree Other) MountTheTwoRoots()
        {
            FiberStrictMode.Enabled = false;
            s_otherClickQueues = true;
            s_clickOnCleanup = true;
            var owner = V.Mount(new VisualElement(), V.Component(OwnerOfTheClicker));
            var other = V.Mount(new VisualElement(), V.Component(AnotherRoot));
            s_shared = "before the owner";
            s_ref = "before the owner";
            s_otherBefore = s_otherRenders;
            s_otherPassiveBefore = s_otherPassive;
            return (owner, other);
        }

        [Test]
        public void Given_AnApplyThatFiresAHandlerOfAnotherRoot_When_ThatHandlerAsksForAFlush_Then_TheOtherRootHasNotRenderedInsideTheApply()
        {
            // Arrange
            var (owner, other) = MountTheTwoRoots();
            using var ownerTree = owner;
            using var otherTree = other;
            s_showClicker.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_clickerOwner);

            // Assert
            Assert.That(s_seenInside, Is.EqualTo("inside:0"));
        }

        // GREEN_ON_BASE(characterization): the base rendered the other root inside the apply, so it has rendered by the
        // time control returns; the case pins that the hold does not leave it a frame behind.
        [Test]
        public void Given_AnApplyThatFiresAHandlerOfAnotherRoot_When_TheEntryEnds_Then_TheOtherRootHasRenderedBeforeControlReturns()
        {
            // Arrange
            var (owner, other) = MountTheTwoRoots();
            using var ownerTree = owner;
            using var otherTree = other;
            s_showClicker.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_clickerOwner);

            // Assert
            Assert.That(s_otherRenders - s_otherBefore, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnApplyThatFiresAHandlerOfAnotherRoot_When_TheOtherRootRendersAfterIt_Then_ItSeesTheOwnersLayoutEffectWrite()
        {
            // Arrange
            var (owner, other) = MountTheTwoRoots();
            using var ownerTree = owner;
            using var otherTree = other;
            s_showClicker.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_clickerOwner);

            // Assert
            Assert.That(s_otherSaw, Is.EqualTo("after the owner"));
        }

        // GREEN_ON_BASE(characterization): the base never held the flush, so a later pass of the owner has nothing to make.
        [Test]
        public void Given_AFlushOfAnotherRootThatWasHeldAndMade_When_ItIsAskedToRenderLaterOutsideAnyPass_Then_ALaterPassOfTheOwnerDoesNotRenderIt()
        {
            // Arrange
            var (owner, other) = MountTheTwoRoots();
            using var ownerTree = owner;
            using var otherTree = other;
            s_showClicker.Invoke(false);
            FiberWorkLoop.FlushState(s_clickerOwner);
            s_setOther.Invoke(previous => previous + 1);
            var before = s_otherRenders;
            s_showClicker.Invoke(true);

            // Act
            FiberWorkLoop.FlushState(s_clickerOwner);
            s_clickOnCleanup = false;

            // Assert
            Assert.That(s_otherRenders, Is.EqualTo(before));
        }

        [Test]
        public void Given_AnApplyThatFiresAHandlerOfAnotherRoot_When_TheOtherRootRendersAfterIt_Then_ItSeesTheRefTheOwnerAttached()
        {
            // Arrange
            var (owner, other) = MountTheTwoRoots();
            using var ownerTree = owner;
            using var otherTree = other;
            s_showClicker.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_clickerOwner);

            // Assert
            Assert.That(s_otherSawRef, Is.EqualTo("attached by the owner"));
        }

        // GREEN_ON_BASE(characterization): the base ran the pending effects where the event flushed them.
        [Test]
        public void Given_AnApplyThatFiresAHandlerOfAnotherRootWithPassiveEffectsPending_When_TheHandlerQueuesNothing_Then_TheOtherRootRanThemBeforeControlReturns()
        {
            // Arrange
            var (owner, other) = MountTheTwoRoots();
            using var ownerTree = owner;
            using var otherTree = other;
            s_otherClickQueues = false;
            s_showClicker.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_clickerOwner);

            // Assert
            Assert.That(s_otherPassive - s_otherPassiveBefore, Is.EqualTo(1));
        }

        private static readonly List<MountedTree> s_popups = new();
        private static int s_popupRenders;
        private static string s_popupInside;
        private static string s_seenInLayout;
        private static Action s_layoutAction;
        private static Action s_refAction;
        private static ComponentFiber s_layoutOwnerFiber;
        private static StateUpdater<bool> s_showLayout;
        private static string s_passive;
        private static string s_otherSawPassive;
        private static VisualElement s_cleanupHost;
        private static MountedTree s_cleanupRoot;
        private static int s_hostChildrenInsideCleanup;
        private static StateUpdater<bool> s_showDisposer;
        private static ComponentFiber s_disposerOwnerFiber;

        [TearDown]
        public void DisposeThePopups()
        {
            foreach (var popup in s_popups) popup.Dispose();
            s_popups.Clear();
            s_layoutAction = null;
            s_refAction = null;
        }

        private static string s_popupSaw;

        [Component]
        private static VNode CountsPopupRenders()
        {
            s_popupRenders++;
            s_popupSaw = s_shared;
            return V.Div(name: "popup");
        }

        // Runs what a case set for the layout phase and for the ref setups of its commit, each render.
        [Component]
        private static VNode LayoutChild(bool shown)
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_layoutAction?.Invoke();
                return (Action)null;
            }, new object[] { shown });
            var captured = shown;
            return V.Div(refCallback: _ =>
            {
                GC.KeepAlive(captured);
                s_refAction?.Invoke();
                return () => { };
            });
        }

        [Component]
        private static VNode LayoutOwner()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_layoutOwnerFiber = FiberAmbientStack.Current;
            s_showLayout = setShown;
            var (layoutTick, setLayoutTick) = Hooks.UseState(0);
            Hooks.UseLayoutEffect(() =>
            {
                s_shared = "after the owner";
                if (!shown && layoutTick == 0) setLayoutTick.Invoke(1);
                return (Action)null;
            }, new object[] { shown });
            Hooks.UseEffect(() =>
            {
                s_passive = "passive effect of tick " + layoutTick;
                return (Action)null;
            }, new object[] { layoutTick });
            return V.Fragment(
                shown ? V.Component(ClicksAnotherRoot, key: "c") : null,
                V.Component(LayoutChild, shown, key: "q"));
        }

        private static (MountedTree Owner, MountedTree Other) MountTheLayoutOwnerAndAnotherRoot()
        {
            FiberStrictMode.Enabled = false;
            s_otherClickQueues = true;
            s_clickOnCleanup = true;
            s_popupRenders = 0;
            var owner = V.Mount(new VisualElement(), V.Component(LayoutOwner));
            var other = V.Mount(new VisualElement(), V.Component(AnotherRoot));
            s_shared = "before the owner";
            s_passive = "before the owner";
            s_otherBefore = s_otherRenders;
            s_otherPassiveBefore = s_otherPassive;
            s_popupRenders = 0;
            return (owner, other);
        }

        private static void FlushTheLayoutOwner()
        {
            s_showLayout.Invoke(false);
            FiberWorkLoop.FlushState(s_layoutOwnerFiber);
        }

        // The child's layout effect runs ahead of its parent's, which writes what the root it mounts reads as it renders.
        [Test]
        public void Given_AChildsLayoutEffectThatMountsAnotherRoot_When_TheCommitEnds_Then_ThatRootSeesTheParentsLayoutEffectWrite()
        {
            // Arrange
            var (owner, other) = MountTheLayoutOwnerAndAnotherRoot();
            using var ownerTree = owner;
            using var otherTree = other;
            s_popupSaw = null;
            s_layoutAction = () => s_popups.Add(V.Mount(new VisualElement(), V.Component(CountsPopupRenders)));

            // Act
            FlushTheLayoutOwner();

            // Assert — null where the root never rendered
            Assert.That(s_popupSaw, Is.EqualTo("after the owner"));
        }

        [Test]
        public void Given_AMountFromALayoutEffect_When_TheLayoutEffectReturns_Then_TheRootHasNotRendered()
        {
            // Arrange
            var (owner, other) = MountTheLayoutOwnerAndAnotherRoot();
            using var ownerTree = owner;
            using var otherTree = other;
            s_layoutAction = () =>
            {
                s_popups.Add(V.Mount(new VisualElement(), V.Component(CountsPopupRenders)));
                s_popupInside = "popup:" + s_popupRenders;
            };

            // Act
            FlushTheLayoutOwner();

            // Assert
            Assert.That(s_popupInside, Is.EqualTo("popup:0"));
        }

        // GREEN_ON_BASE(characterization): the base rendered the root where it was mounted, so it has rendered by the time control returns.
        [Test]
        public void Given_AMountFromALayoutEffect_When_TheCommitEnds_Then_TheRootHasRenderedBeforeControlReturns()
        {
            // Arrange
            var (owner, other) = MountTheLayoutOwnerAndAnotherRoot();
            using var ownerTree = owner;
            using var otherTree = other;
            s_layoutAction = () => s_popups.Add(V.Mount(new VisualElement(), V.Component(CountsPopupRenders)));

            // Act
            FlushTheLayoutOwner();

            // Assert
            Assert.That(s_popupRenders, Is.EqualTo(1));
        }

        [Test]
        public void Given_AMountFromACallbackRef_When_TheRefSetsUp_Then_TheRootHasNotRendered()
        {
            // Arrange
            var (owner, other) = MountTheLayoutOwnerAndAnotherRoot();
            using var ownerTree = owner;
            using var otherTree = other;
            s_refAction = () =>
            {
                s_popups.Add(V.Mount(new VisualElement(), V.Component(CountsPopupRenders)));
                s_popupInside = "popup:" + s_popupRenders;
            };

            // Act
            FlushTheLayoutOwner();

            // Assert
            Assert.That(s_popupInside, Is.EqualTo("popup:0"));
        }

        [Test]
        public void Given_ADiscreteHandlerFiredByALayoutEffect_When_TheLayoutEffectReturns_Then_TheOtherRootHasNotRendered()
        {
            // Arrange
            var (owner, other) = MountTheLayoutOwnerAndAnotherRoot();
            using var ownerTree = owner;
            using var otherTree = other;
            s_clickOnCleanup = false;
            s_layoutAction = () =>
            {
                s_otherButton.SimulateClick();
                s_seenInLayout = "inside:" + (s_otherRenders - s_otherBefore);
            };

            // Act
            FlushTheLayoutOwner();

            // Assert
            Assert.That(s_seenInLayout, Is.EqualTo("inside:0"));
        }

        // GREEN_ON_BASE(characterization): the base rendered the other root inside the layout effect, so it has rendered by the time control returns.
        [Test]
        public void Given_ADiscreteHandlerFiredByALayoutEffect_When_TheCommitEnds_Then_TheOtherRootHasRenderedBeforeControlReturns()
        {
            // Arrange
            var (owner, other) = MountTheLayoutOwnerAndAnotherRoot();
            using var ownerTree = owner;
            using var otherTree = other;
            s_clickOnCleanup = false;
            s_layoutAction = () => s_otherButton.SimulateClick();

            // Act
            FlushTheLayoutOwner();

            // Assert
            Assert.That(s_otherRenders - s_otherBefore, Is.EqualTo(1));
        }

        [Component]
        private static VNode DisposesARootInItsCleanup()
        {
            Hooks.UseLayoutEffect(() => () =>
            {
                s_cleanupRoot.Dispose();
                s_hostChildrenInsideCleanup = s_cleanupHost.childCount;
            }, Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode OwnerOfTheDisposer()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_disposerOwnerFiber = FiberAmbientStack.Current;
            s_showDisposer = setShown;
            return V.Fragment(shown ? V.Component(DisposesARootInItsCleanup, key: "d") : null);
        }

        private static MountedTree MountTheDisposerOwner()
        {
            FiberStrictMode.Enabled = false;
            s_cleanupHost = new VisualElement();
            s_cleanupRoot = V.Mount(s_cleanupHost, V.Component(CountsPopupRenders));
            return V.Mount(new VisualElement(), V.Component(OwnerOfTheDisposer));
        }

        [Test]
        public void Given_ALayoutCleanupThatDisposesARootItMounted_When_TheCleanupReturns_Then_TheRootIsStillMounted()
        {
            // Arrange
            using var owner = MountTheDisposerOwner();
            s_showDisposer.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_disposerOwnerFiber);

            // Assert
            Assert.That(s_hostChildrenInsideCleanup, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base unmounted the root where the cleanup disposed it.
        [Test]
        public void Given_ALayoutCleanupThatDisposesARootItMounted_When_TheEntryEnds_Then_TheRootIsUnmounted()
        {
            // Arrange
            using var owner = MountTheDisposerOwner();
            s_showDisposer.Invoke(false);

            // Act
            FiberWorkLoop.FlushState(s_disposerOwnerFiber);

            // Assert
            Assert.That(s_cleanupHost.childCount, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base rendered the other root inside the unmount.
        [Test]
        public void Given_ARootWhoseDeletedComponentsCleanupFiresAHandlerOfAnotherRoot_When_ItIsDisposed_Then_TheOtherRootHasRenderedBeforeDisposeReturns()
        {
            // Arrange
            var (owner, other) = MountTheTwoRoots();
            using var otherTree = other;

            // Act
            owner.Dispose();

            // Assert
            Assert.That(s_otherRenders - s_otherBefore, Is.EqualTo(1));
        }

        [Test]
        public void Given_AFlushOfARootWhoseLayoutEffectOwesASyncLane_When_ARootIsHeldBehindIt_Then_TheCommittingRootsEffectsRanBeforeTheHeldRootRendered()
        {
            // Arrange
            var (owner, other) = MountTheLayoutOwnerAndAnotherRoot();
            using var ownerTree = owner;
            using var otherTree = other;

            // Act
            FlushTheLayoutOwner();

            // Assert
            Assert.That(s_otherSawPassive, Is.EqualTo("passive effect of tick 1"));
        }

        [Component]
        private static VNode ThrowsFromItsLayoutCleanup()
        {
            Hooks.UseLayoutEffect(() => () => throw new InvalidOperationException("cleanup boom"), Array.Empty<object>());
            return V.Div();
        }

        [Test]
        public void Given_ARootWhoseUncaughtErrorHandlerMountsAnotherRoot_When_ItIsDisposedWithACleanupThatThrows_Then_TheOtherRootHasNotRenderedInsideTheHandler()
        {
            // Arrange
            FiberStrictMode.Enabled = false;
            s_popupRenders = 0;
            string seen = null;
            var owner = V.Mount(new VisualElement(), V.Component(ThrowsFromItsLayoutCleanup), new MountOptions
            {
                OnUncaughtError = (_, _) =>
                {
                    s_popups.Add(V.Mount(new VisualElement(), V.Component(CountsPopupRenders)));
                    seen = "popup:" + s_popupRenders;
                },
            });

            // Act
            owner.Dispose();

            // Assert
            Assert.That(seen, Is.EqualTo("popup:0"));
        }

        [Test]
        public void Given_ACommitWhoseSyncLanePassiveEffectIsPending_When_ARootIsHeldBehindIt_Then_TheCommittingRootsEffectRanBeforeTheHeldRootRendered()
        {
            // Arrange
            var (owner, other) = MountTheLayoutOwnerAndAnotherRoot();
            using var ownerTree = owner;
            using var otherTree = other;
            s_showLayout.Invoke(false);

            // Act
            owner.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_otherSawPassive, Is.EqualTo("passive effect of tick 1"));
        }
    }
}
