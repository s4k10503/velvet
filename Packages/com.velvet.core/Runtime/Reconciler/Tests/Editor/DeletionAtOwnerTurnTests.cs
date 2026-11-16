using System;
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

        [Component]
        private static VNode XThenAPresence()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsX, shown, key: "l"),
                V.AnimatePresence(children: shown ? new VNode[] { V.Component(Probed, new Named("P"), key: "p") } : null));
        }

        // GREEN_ON_BASE(characterization): the base's one plan takes the container in tree order, as an AnimatePresence
        // deletes what it drops at its own turn, after the component before it.
        [Test]
        public void Given_AnAnimatePresenceDroppingAComponentWithNoExit_When_AComponentBeforeItDeletesToo_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(XThenAPresence)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ins-x:P lay-x:P"));
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
    }
}
