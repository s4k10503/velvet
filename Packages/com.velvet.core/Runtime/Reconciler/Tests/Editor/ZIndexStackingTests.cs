using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the reconciler-side mechanics of the <c>z-*</c> stacking feature (FiberZLayerCoordinator /
    /// StyleZIndexClass): a z-marked element is relocated within its own parent into a band — the front band after
    /// the declared slots for z &gt;= 0, the back band ahead of them for negative z — while a hidden placeholder
    /// holds its logical slot for the reconciler, structural variants, and focus order. None of this needs a live
    /// panel: physical DOM structure, element identity across patches, and reconciler slot-range correctness are
    /// all observable on a bare (unattached) tree. GWT, one assert per case.
    /// <para>
    /// Every fact about the coordinator itself that a case depends on — a band holds an element, an element is
    /// z-managed, the transition under test actually ran — is a term inside that case's single assertion rather
    /// than an <c>Assume</c>. A coordinator that relocates nothing still satisfies most of these assertions on
    /// their own, so stating those facts as preconditions reports Inconclusive, which no reporter counts as a
    /// failure. The reads below tolerate a null element so that a missing one reports which term went wrong
    /// instead of a thrown lookup.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class ZIndexStackingTests
    {
        // A minimal reusable Store for tests that only need to flip one value and re-render.
        private sealed class ToggleStore<T> : Store<T>
        {
            private readonly T _initial;
            public ToggleStore(T initial) : base(initial) => _initial = initial;
            public void Set(T value) => SetState(_ => value);
            protected override void ResetCore() => SetState(_ => _initial);
        }

        private static ToggleStore<string> s_churnStore;
        private static ToggleStore<int> s_reuseStore;
        private static ToggleStore<bool> s_teardownStore;
        private static ToggleStore<bool> s_zToNoneStore;
        private static ToggleStore<bool> s_noneToZStore;
        private static ToggleStore<bool> s_resortStore;
        private static ToggleStore<bool> s_typeFlipStore;
        private static ToggleStore<bool> s_signFlipStore;
        private static ToggleStore<bool> s_moveStore;
        private static bool s_zParkZManaged;
        private static ComponentFiber s_zParkFiber;
        private static ToggleStore<bool> s_structuralSweepStore;
        private static ToggleStore<bool> s_structuralPatchStore;
        private static bool s_crossFiberAZManaged;
        private static string s_crossFiberBName;
        private static ComponentFiber s_crossFiberAFiber;
        private static ComponentFiber s_crossFiberBFiber;
        private static int s_crossParkACount;
        private static bool s_crossParkBZManaged;
        private static ComponentFiber s_crossParkAFiber;
        private static ComponentFiber s_crossParkBFiber;
        private static int s_crossParkKeyedACount;
        private static bool s_crossParkKeyedBZManaged;
        private static ComponentFiber s_crossParkKeyedAFiber;
        private static ComponentFiber s_crossParkKeyedBFiber;
        private static bool s_drainZManaged;
        private static ComponentFiber s_drainFiber;

        [SetUp]
        public void SetUp()
        {
            s_churnStore = null;
            s_reuseStore = null;
            s_teardownStore = null;
            s_zToNoneStore = null;
            s_noneToZStore = null;
            s_resortStore = null;
            s_typeFlipStore = null;
            s_signFlipStore = null;
            s_moveStore = null;
            s_zParkZManaged = false;
            s_zParkFiber = null;
            s_structuralSweepStore = null;
            s_structuralPatchStore = null;
            s_crossFiberAZManaged = false;
            s_crossFiberBName = null;
            s_crossFiberAFiber = null;
            s_crossFiberBFiber = null;
            s_crossParkACount = 0;
            s_crossParkBZManaged = false;
            s_crossParkAFiber = null;
            s_crossParkBFiber = null;
            s_crossParkKeyedACount = 0;
            s_crossParkKeyedBZManaged = false;
            s_crossParkKeyedAFiber = null;
            s_crossParkKeyedBFiber = null;
            s_drainZManaged = false;
            s_drainFiber = null;
            s_motionStore = null;
            s_lateEarlierStore = null;
            s_equalZMoveStore = null;
            s_sameLayerStore = null;
            s_tickStore = null;
            s_tickRenders = 0;
            s_sliceFiber = null;
            s_sliceAClass = null;
            s_sliceBClass = null;
            s_sliceKeyed = false;
            s_transitionStore = null;
        }

        // A live view of parent's front (z >= 0) or back (negative z) band: the relocated elements painting after
        // or ahead of its declared slots, each with the ring band hosted after it, read afresh on every access.
        private sealed class BandView
        {
            private readonly VisualElement _parent;
            private readonly bool _front;

            public BandView(VisualElement parent, bool front)
            {
                _parent = parent;
                _front = front;
            }

            private List<VisualElement> Members => ZBandProbe.Band(_parent, _front);

            public bool Any => Members.Count > 0;

            public int childCount => Members.Count;

            public VisualElement ElementAt(int index) => index >= 0 && index < Members.Count ? Members[index] : null;

            public bool Contains(VisualElement element) => element != null && Members.Contains(element);

            public string Names => ZBandProbe.Names(Members);
        }

        private static BandView Band(VisualElement parent, bool front) => new(parent, front);

        private static bool IsZManaged(ReconcilerContext ctx, VisualElement element)
            => element != null && ctx.ZLayerMembers.ContainsKey(element);

        private static string Names(BandView band) => band.Names;

        #region Physical order

        [Test]
        public void Given_MultipleAbsoluteSiblingsWithDifferentPositiveZ_When_Mounted_Then_TheyArePhysicallySortedByZInTheFrontBand()
        {
            // Arrange / Act — declared out of ascending order; the front band must still sort them.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "ordinary"),
                V.Div(name: "z30", className: "absolute z-30"),
                V.Div(name: "z10", className: "absolute z-10"),
                V.Div(name: "z20", className: "absolute z-20"),
            }));
            var front = Band(root.Q<VisualElement>("parent"), front: true);

            // Assert
            Assert.That(
                (front.Any, front.ElementAt(0)?.name, front.ElementAt(1)?.name, front.ElementAt(2)?.name),
                Is.EqualTo((true, "z10", "z20", "z30")));
        }

        [Test]
        public void Given_ANegativeZAbsoluteSibling_When_Mounted_Then_ItPhysicallyPrecedesTheOrdinaryChildren()
        {
            // Arrange / Act
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "ordinary1"),
                V.Div(name: "negz", className: "absolute -z-10"),
                V.Div(name: "ordinary2"),
            }));
            var parent = root.Q<VisualElement>("parent");

            // Assert — negz, in the back band, is parent's own first physical child.
            Assert.That(parent.ElementAt(0), Is.SameAs(Band(parent, front: false).ElementAt(0)).And.Not.Null);
        }

        [Test]
        public void Given_ANegativeZChild_When_Mounted_Then_ItNeverEscapesToBeforeItsOwnStackingParent()
        {
            // Arrange / Act — the back band is always the stacking parent's OWN leading children, never hoisted to
            // become the parent's preceding sibling under the grandparent.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "grandparent", children: new VNode[]
            {
                V.Div(name: "before"),
                V.Div(name: "parent", className: "relative", children: new VNode[]
                {
                    V.Div(name: "ordinary"),
                    V.Div(name: "negz", className: "absolute -z-10"),
                }),
            }));

            // Assert — "before" is still the grandparent's first child; "parent" was never reordered around it.
            Assert.That(
                (Band(root.Q<VisualElement>("parent"), front: false).Any,
                    root.Q<VisualElement>("grandparent").ElementAt(0).name),
                Is.EqualTo((true, "before")));
        }

        [Test]
        public void Given_AZClassOnAnInFlowChild_When_Mounted_Then_ItPaintsInTheFrontBandAfterItsLaterSibling()
        {
            // Arrange / Act — CSS stacks a flex item by its z-index whether or not it is positioned, so an
            // in-flow z-10 paints over the sibling declared after it.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", children: new VNode[]
            {
                V.Div(name: "child", className: "z-10"),
                V.Div(name: "sibling"),
            }));

            // Assert
            Assert.That(ZBandProbe.PaintOrder(root.Q<VisualElement>("parent")), Is.EqualTo("sibling,child"));
        }

        [Component]
        private static VNode TypeFlipHost()
        {
            var showButton = Hooks.UseStore(s_typeFlipStore, x => x);
            var children = showButton
                ? new VNode[] { V.Button(name: "button"), V.Div(name: "negz", className: "absolute -z-10") }
                : new VNode[] { V.Div(name: "negz", className: "absolute -z-10") };
            return V.Div(name: "parent", className: "relative", children: children);
        }

        [Test]
        public void Given_AParentWhoseOnlyChildIsANegativeZElement_When_TheNextRenderPrependsAnOrdinaryElement_Then_TheBackBandStaysTheParentsFirstPhysicalChild()
        {
            // Arrange — negz is parent's only physical child besides its own placeholder; declaring a new leading
            // Button forces a positional type-flip at slot 0 (Button replaces negz there), so negz is torn down
            // and created again, declared after the Button.
            using var store = new ToggleStore<bool>(false);
            s_typeFlipStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(TypeFlipHost, key: "root"));
            var parent = root.Q<VisualElement>("parent");
            var backBandHeldNegzBeforeTheFlip = Band(parent, front: false).Names == "negz";

            // Act — declares [Button, negz]: a positional type-flip at slot 0.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the back band is still parent's first physical child, not displaced behind Button.
            Assert.That(
                (backBandHeldNegzBeforeTheFlip, parent.ElementAt(0).name, Band(parent, front: false).Names),
                Is.EqualTo((true, "negz", "negz")));
        }

        #endregion

        #region CSS paint order among siblings

        [Test]
        public void Given_AnAbsoluteChildDeclaredBeforeAnUnpositionedSibling_When_Mounted_Then_ItPaintsAfterIt()
        {
            // Arrange / Act — CSS paints every positioned child after the unpositioned ones, z-index auto or not.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "overlay", className: "absolute inset-0"),
                V.Div(name: "content"),
            }));

            // Assert
            Assert.That(ZBandProbe.PaintOrder(root.Q<VisualElement>("parent")), Is.EqualTo("content,overlay"));
        }

        [Test]
        public void Given_AZ0ChildBeforeAPositionedAutoSibling_When_Mounted_Then_TheyPaintInTreeOrderAfterTheUnpositioned()
        {
            // Arrange / Act — z-index 0 and auto paint in one layer in tree order; neither outranks the other.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "zero", className: "absolute z-0"),
                V.Div(name: "auto", className: "absolute"),
                V.Div(name: "plain"),
            }));

            // Assert
            Assert.That(ZBandProbe.PaintOrder(root.Q<VisualElement>("parent")), Is.EqualTo("plain,zero,auto"));
        }

        [Test]
        public void Given_FlexOrderClasses_When_Mounted_Then_TheChildrenPaintInFlexOrder()
        {
            // Arrange / Act — flex items paint in order-modified document order.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "flex flex-row", children: new VNode[]
            {
                V.Div(name: "a", className: "order-2"),
                V.Div(name: "b"),
                V.Div(name: "c", className: "order-1"),
            }));

            // Assert
            Assert.That(ZBandProbe.PaintOrder(root.Q<VisualElement>("parent")), Is.EqualTo("b,c,a"));
        }

        [Test]
        public void Given_AHoverZClass_When_HoverStartsAndEnds_Then_TheElementRestacksBothWays()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(name: "parent", className: "relative", children: new VNode[]
                {
                    V.Div(name: "a", className: "absolute hover:z-10"),
                    V.Div(name: "b", className: "absolute"),
                }),
            });
            var parent = scope.Root.Q<VisualElement>("parent");
            var a = scope.Root.Q<VisualElement>("a");

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                a.SimulateEvent(over);
            }
            var whileHovered = ZBandProbe.PaintOrder(parent);
            using (var leave = PointerOutEvent.GetPooled())
            {
                a.SimulateEvent(leave);
            }

            // Assert
            Assert.That((whileHovered, ZBandProbe.PaintOrder(parent)), Is.EqualTo(("b,a", "a,b")));
        }

        private static ToggleStore<string> s_transitionStore;

        [Component]
        private static VNode TransitionHost()
        {
            var className = Hooks.UseStore(s_transitionStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "child", className: className),
                V.Div(name: "sibling", className: "absolute"),
            });
        }

        [Test]
        public void Given_AnElementOnAPanel_When_ItsZAppearsFlipsSignAndLeaves_Then_ItNeverLeavesThePanel()
        {
            // Arrange
            using var store = new ToggleStore<string>("absolute");
            s_transitionStore = store;
            using var host = new HeadlessEditorPanelHost();
            var root = new VisualElement();
            host.Root.Add(root);
            using var mounted = V.Mount(root, V.Component(TransitionHost, key: "root"));
            var child = root.Q<VisualElement>("child");
            var detaches = 0;
            child.RegisterCallback<DetachFromPanelEvent>(_ => detaches++);

            // Act
            store.Set("absolute z-10");
            mounted.FlushStateForTest();
            var relocated = IsZManaged(mounted.Root.Reconciler.Context, child);
            store.Set("absolute -z-10");
            mounted.FlushStateForTest();
            store.Set("absolute");
            mounted.FlushStateForTest();

            // Assert — the relocation is a term: an element never relocated never leaves the panel either.
            Assert.That((relocated, detaches), Is.EqualTo((true, 0)));
        }

        #endregion

        #region Reconciler slot-range integrity

        [Component]
        private static VNode ChurnList()
        {
            var keys = Hooks.UseStore(s_churnStore, x => x);
            var children = new List<VNode> { V.Div(name: "zchild", key: "z", className: "absolute -z-10") };
            foreach (var key in keys)
            {
                children.Add(V.Button(name: "item-" + key, key: key.ToString(), text: key.ToString()));
            }
            return V.Div(name: "list", className: "relative", children: children.ToArray());
        }

        [Test]
        public void Given_ABackZMarkedChildAmongKeyedSiblings_When_TheSiblingsReorderAddAndRemoveAcrossMultipleRerenders_Then_TheyStillCommitTheLatestDeclaredOrder()
        {
            // Arrange — a back-band sibling (the leading-offset case) interspersed among keyed buttons.
            using var store = new ToggleStore<string>("abc");
            s_churnStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ChurnList, key: "root"));
            var list = root.Q<VisualElement>("list");
            var backBandHeldTheChildBeforeTheChurn = Band(list, front: false).Names == "zchild";

            // Act — reorder, then insert, then remove ordinary keyed siblings across successive re-renders
            // while the leading back band stays present the whole time: an insertion/removal shifts every
            // subsequent index differently than a pure LIS reorder, so both need their own coverage here, not
            // just the reorder the original regression pinned.
            store.Set("cba");
            mounted.FlushStateForTest();
            store.Set("bac");
            mounted.FlushStateForTest();
            store.Set("bacd"); // insert "d" at the tail
            mounted.FlushStateForTest();
            store.Set("acd"); // remove "b" from the middle
            mounted.FlushStateForTest();

            // Assert — the ordinary siblings' physical order matches the latest declared order: their own
            // slot-range math was never corrupted by the leading band, across reorder, insert, and remove alike.
            var names = list.Query<Button>().ToList().ConvertAll(b => b.name);
            Assert.That((backBandHeldTheChildBeforeTheChurn, string.Join(",", names)),
                Is.EqualTo((true, "item-a,item-c,item-d")));
        }

        #endregion

        #region Cleanup

        // 0 = z-managed button, 1 = empty (unmounted, pool-returned), 2 = plain button (rents the pool).
        [Component]
        private static VNode ReuseHost()
        {
            var phase = Hooks.UseStore(s_reuseStore, x => x);
            var children = phase switch
            {
                0 => new VNode[] { V.Button(name: "b", className: "absolute z-10") },
                1 => System.Array.Empty<VNode>(),
                _ => new VNode[] { V.Button(name: "b") },
            };
            return V.Div(name: "parent", className: "relative", children: children);
        }

        [Test]
        public void Given_AZManagedButtonWasPooled_When_APlainButtonRentsTheSamePooledInstance_Then_ItCarriesNoZLayerMembership()
        {
            // Arrange — a z-managed button unmounts (fully removed, pool-returned).
            using var store = new ToggleStore<int>(0);
            s_reuseStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ReuseHost, key: "root"));
            var ctx = mounted.Root.Reconciler.Context;
            var original = root.Q<Button>("b");
            var originalMountedZManaged = IsZManaged(ctx, original);
            store.Set(1);
            mounted.FlushStateForTest();

            // Act — a plain (non-z) button mounts, renting the same pooled Button instance back.
            store.Set(2);
            mounted.FlushStateForTest();
            var rented = root.Q<Button>("b");

            // Assert — the button really was z-managed before it was pooled, the plain button actually rented
            // the SAME instance back (not a fresh one), and that instance carries no leftover registration.
            // Every one of the last two terms is satisfied by a coordinator that classifies nothing at all —
            // ordinary pooling plus a permanently empty registry — so the first term is what makes this a
            // z-layer case. The unmount teardown the arrange step depends on needs no term of its own: the
            // same instance still registered after it would fail the third.
            Assert.That(
                (originalMountedZManaged, ReferenceEquals(rented, original), ctx.ZLayerMembers.ContainsKey(rented)),
                Is.EqualTo((true, true, false)));
        }

        [Component]
        private static VNode TeardownHost()
        {
            var show = Hooks.UseStore(s_teardownStore, x => x);
            return V.Div(name: "parent", className: "relative", children: show
                ? new VNode[] { V.Div(name: "zchild", className: "absolute z-10") }
                : System.Array.Empty<VNode>());
        }

        [Test]
        public void Given_TheLastZMarkedChildUnderAParent_When_ItUnmounts_Then_NeitherItNorItsPlaceholderRemains()
        {
            // Arrange
            using var store = new ToggleStore<bool>(true);
            s_teardownStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(TeardownHost, key: "root"));
            var parent = root.Q<VisualElement>("parent");
            var relocatedBeforeTheUnmount = Band(parent, front: true).Names == "zchild";

            // Act
            store.Set(false);
            mounted.FlushStateForTest();

            // Assert — the element stays in its parent past the removal of its placeholder, until the pass ends.
            Assert.That((relocatedBeforeTheUnmount, parent.childCount), Is.EqualTo((true, 0)));
        }

        #endregion

        #region Patch transitions preserve element identity

        [Component]
        private static VNode ZToNoneHost()
        {
            var withZ = Hooks.UseStore(s_zToNoneStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "child", className: withZ ? "absolute z-10" : "absolute"),
            });
        }

        [Test]
        public void Given_AZManagedElement_When_ItsClassChangesToPlainAbsolute_Then_TheSameElementInstanceReturnsToItsOrdinarySlot()
        {
            // Arrange
            using var store = new ToggleStore<bool>(true);
            s_zToNoneStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ZToNoneHost, key: "root"));
            var child = root.Q<VisualElement>("child");
            var ctx = mounted.Root.Reconciler.Context;
            var zManagedBeforeTheChange = IsZManaged(ctx, child);

            // Act
            store.Set(false);
            mounted.FlushStateForTest();

            // Assert — the SAME instance now occupies the parent's ordinary slot directly.
            Assert.That(
                (zManagedBeforeTheChange,
                    ReferenceEquals(root.Q<VisualElement>("parent").ElementAt(0), child)),
                Is.EqualTo((true, true)));
        }

        [Component]
        private static VNode NoneToZHost()
        {
            var withZ = Hooks.UseStore(s_noneToZStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "child", className: withZ ? "absolute z-10" : "absolute"),
            });
        }

        [Test]
        public void Given_AnOrdinaryAbsoluteElement_When_ItGainsAZClass_Then_TheSameElementInstanceRelocatesIntoTheFrontBand()
        {
            // Arrange
            using var store = new ToggleStore<bool>(false);
            s_noneToZStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(NoneToZHost, key: "root"));
            var child = root.Q<VisualElement>("child");
            var ctx = mounted.Root.Reconciler.Context;
            var startedAsAnOrdinaryDirectChild = ReferenceEquals(root.Q<VisualElement>("parent").ElementAt(0), child);

            // Act
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the SAME instance is now registered as z-managed AND physically sits in the front band.
            // Registration alone survives a coordinator that never moves the element, which would leave the
            // relocation this case is named for unmeasured.
            Assert.That(
                (startedAsAnOrdinaryDirectChild, IsZManaged(ctx, child),
                    Band(root.Q<VisualElement>("parent"), front: true).Contains(child)),
                Is.EqualTo((true, true, true)));
        }

        [Component]
        private static VNode ResortHost()
        {
            var swapped = Hooks.UseStore(s_resortStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "a", className: "absolute " + (swapped ? "z-30" : "z-10")),
                V.Div(name: "b", className: "absolute " + (swapped ? "z-10" : "z-30")),
            });
        }

        [Test]
        public void Given_TwoZManagedSiblings_When_TheirRelativeZOrderSwaps_Then_TheSameElementInstancesResortWithoutRemounting()
        {
            // Arrange — a (z-10) sorts before b (z-30).
            using var store = new ToggleStore<bool>(false);
            s_resortStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ResortHost, key: "root"));
            var a = root.Q<VisualElement>("a");
            var b = root.Q<VisualElement>("b");
            var front = Band(root.Q<VisualElement>("parent"), front: true);
            var sortedAThenB = front is { childCount: 2 }
                && ReferenceEquals(front.ElementAt(0), a) && ReferenceEquals(front.ElementAt(1), b);

            // Act — swap: a becomes z-30, b becomes z-10.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the front band's order flips, using the SAME two element instances.
            var sortedBThenA = front is { childCount: 2 }
                && ReferenceEquals(front.ElementAt(0), b) && ReferenceEquals(front.ElementAt(1), a);
            Assert.That((sortedAThenB, sortedBThenA), Is.EqualTo((true, true)));
        }

        [Component]
        private static VNode SignFlipHost()
        {
            var flipped = Hooks.UseStore(s_signFlipStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "child", className: "absolute " + (flipped ? "-z-10" : "z-10")),
            });
        }

        [Test]
        public void Given_AZManagedElement_When_ItsSignFlips_Then_TheSameElementMovesFromTheFrontBandToTheBackBand()
        {
            // Arrange — z-10: the element paints in the front band.
            using var store = new ToggleStore<bool>(false);
            s_signFlipStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(SignFlipHost, key: "root"));
            var child = root.Q<VisualElement>("child");
            var parent = root.Q<VisualElement>("parent");
            var inFrontBefore = Band(parent, front: true).Contains(child);

            // Act — flip to -z-10.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((inFrontBefore, Band(parent, front: false).Contains(child)), Is.EqualTo((true, true)));
        }

        #endregion

        #region Equal z stacks in tree order

        private static ToggleStore<bool> s_lateEarlierStore;
        private static ToggleStore<bool> s_equalZMoveStore;
        private static ToggleStore<bool> s_sameLayerStore;
        private static ToggleStore<int> s_tickStore;
        private static int s_tickRenders;

        [Component]
        private static VNode LateEarlierSiblingHost()
        {
            var showFirst = Hooks.UseStore(s_lateEarlierStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                showFirst ? V.Div(name: "first", className: "absolute z-10") : null,
                V.Div(name: "second", className: "absolute z-10"),
            });
        }

        [Test]
        public void Given_AnEqualZSiblingMountedAfterwardsAheadOfAnExistingOne_When_Mounted_Then_TheLaterDeclaredOneStaysOnTop()
        {
            // Arrange — "second" mounts first; "first" arrives on the next render, declared ahead of it.
            using var store = new ToggleStore<bool>(false);
            s_lateEarlierStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(LateEarlierSiblingHost, key: "root"));

            // Act
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — CSS paints equal z in tree order, so the later-declared "second" is the later child.
            Assert.That(Names(Band(root.Q<VisualElement>("parent"), front: true)),
                Is.EqualTo("first,second"));
        }

        [Component]
        private static VNode EqualZMoveHost()
        {
            var swapped = Hooks.UseStore(s_equalZMoveStore, x => x);
            var a = V.Div(name: "a", key: "a", className: "absolute z-10");
            var b = V.Div(name: "b", key: "b", className: "absolute z-10");
            return V.Div(name: "parent", className: "relative", children: swapped ? new VNode[] { b, a } : new VNode[] { a, b });
        }

        [Test]
        public void Given_TwoEqualZKeyedSiblings_When_TheyReorder_Then_TheLayerFollowsTheNewTreeOrder()
        {
            // Arrange
            using var store = new ToggleStore<bool>(false);
            s_equalZMoveStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(EqualZMoveHost, key: "root"));
            var front = Band(root.Q<VisualElement>("parent"), front: true);
            Assume.That(Names(front), Is.EqualTo("a,b"), "Precondition: mounted in declared order");

            // Act — a keyed move of the placeholders; neither element's z changes.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(front), Is.EqualTo("b,a"));
        }

        [Component]
        private static VNode RingedEqualZMoveHost()
        {
            var swapped = Hooks.UseStore(s_equalZMoveStore, x => x);
            var a = V.Div(name: "a", key: "a", className: "absolute z-10");
            var b = V.Div(name: "b", key: "b", className: "absolute z-10 ring-2");
            return V.Div(name: "parent", className: "relative", children: swapped ? new VNode[] { b, a } : new VNode[] { a, b });
        }

        [Test]
        public void Given_ARingedMemberOfALayer_When_ItsLayerResorts_Then_ItsBandStaysDirectlyAfterIt()
        {
            // Arrange
            using var store = new ToggleStore<bool>(false);
            s_equalZMoveStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(RingedEqualZMoveHost, key: "root"));
            var front = Band(root.Q<VisualElement>("parent"), front: true);
            Assume.That(Names(front), Is.EqualTo("a,b,band"), "Precondition: mounted in declared order");

            // Act
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the ringed "b" moves ahead of "a" and its band moves with it rather than staying last.
            Assert.That(Names(front), Is.EqualTo("b,band,a"));
        }

        [Component]
        private static VNode SameLayerHost()
        {
            var raised = Hooks.UseStore(s_sameLayerStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "a", className: "absolute " + (raised ? "z-30" : "z-10")),
                V.Div(name: "b", className: "absolute z-20"),
            });
        }

        [Test]
        public void Given_AZManagedElement_When_ItsZChangesWithinItsLayer_Then_TheLayerIsResorted()
        {
            // Arrange
            using var store = new ToggleStore<bool>(false);
            s_sameLayerStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(SameLayerHost, key: "root"));
            var front = Band(root.Q<VisualElement>("parent"), front: true);
            Assume.That(Names(front), Is.EqualTo("a,b"), "Precondition: mounted in z order");

            // Act
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(front), Is.EqualTo("b,a"));
        }

        [Test]
        public void Given_AZManagedElementOnAPanel_When_ItsZChangesWithinItsLayer_Then_ItNeverLeavesThePanel()
        {
            // Arrange
            using var store = new ToggleStore<bool>(false);
            s_sameLayerStore = store;
            using var host = new HeadlessEditorPanelHost();
            var root = new VisualElement();
            host.Root.Add(root);
            using var mounted = V.Mount(root, V.Component(SameLayerHost, key: "root"));
            var a = root.Q<VisualElement>("a");
            var detaches = 0;
            a.RegisterCallback<DetachFromPanelEvent>(_ => detaches++);

            // Act
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(detaches, Is.EqualTo(0));
        }

        [Component]
        private static VNode Ticker()
        {
            Hooks.UseStore(s_tickStore, x => x);
            s_tickRenders++;
            return V.Div(name: "ticker");
        }

        [Component]
        private static VNode UnrelatedRenderHost()
        {
            return V.Div(children: new VNode[]
            {
                V.Component(Ticker, key: "ticker"),
                V.Div(name: "parent", className: "relative", children: new VNode[]
                {
                    V.Div(name: "a", className: "absolute z-10"),
                    V.Div(name: "b", className: "absolute z-10"),
                }),
            });
        }

        // The arrangement at a pass end is scoped to a stacking parent whose children the pass reconciled.
        [Test]
        public void Given_ALayerPutOutOfOrderOutsideARender_When_AnUnrelatedComponentReRenders_Then_TheLayerIsNotResorted()
        {
            // Arrange
            using var store = new ToggleStore<int>(0);
            s_tickStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(UnrelatedRenderHost, key: "root"));
            var front = Band(root.Q<VisualElement>("parent"), front: true);
            root.Q<VisualElement>("a").PlaceInFront(root.Q<VisualElement>("b"));
            Assume.That(Names(front), Is.EqualTo("b,a"), "Precondition: the band is out of declared order");

            // Act — only the ticker re-renders; "parent"'s children are not reconciled.
            store.Set(1);
            mounted.FlushStateForTest();

            Assume.That(s_tickRenders, Is.EqualTo(2), "Precondition: the ticker re-rendered");

            // Assert
            Assert.That(Names(front), Is.EqualTo("b,a"));
        }

        [Test]
        public void Given_AnInOrderBandPendingAnArrangement_When_ThePostPassArrangementRuns_Then_ItAllocatesNothing()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "a", className: "absolute z-10"),
                V.Div(name: "b", className: "absolute z-10"),
            }));
            var ctx = mounted.Root.Reconciler.Context;
            var parent = root.Q<VisualElement>("parent");
            void MarkPending() => FiberZLayerCoordinator.NoteChildrenReconciled(ctx, parent);
            MarkPending();
            FiberZLayerCoordinator.ResortLayers(ctx);
            var canary = GCAllocationProbe.MedianBlocksDuring(() => GC.KeepAlive(new byte[16]));
            Assume.That(canary, Is.GreaterThan(0), "Precondition: the probe counts an allocation at all");

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(MarkPending, () => FiberZLayerCoordinator.ResortLayers(ctx));

            // Assert
            Assert.That(blocks, Is.EqualTo(0));
        }

        [Test]
        public void Given_TwoZManagedSiblings_When_TheirMountPassEnds_Then_TheArrangementsBuffersHoldNoElement()
        {
            // Arrange
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "a", className: "absolute z-10"),
                V.Div(name: "b", className: "absolute z-10"),
            }));

            // Assert — the buffers outlive the call, so an element left in one outlives its tree.
            Assert.That(ElementsHeldByArrangementBuffers(), Is.EqualTo(0));
        }

        // The elements FiberZLayerCoordinator's static arrangement buffers still reference.
        private static int ElementsHeldByArrangementBuffers()
        {
            const System.Reflection.BindingFlags statics =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var held = 0;
            foreach (var name in new[] { "s_entries", "s_back", "s_front", "s_middle", "s_followers", "s_desired",
                         "s_placed", "s_rank", "s_layout", "s_retiredHere" })
            {
                var buffer = typeof(FiberZLayerCoordinator).GetField(name, statics)?.GetValue(null)
                    ?? throw new MissingFieldException(nameof(FiberZLayerCoordinator), name);
                held += (int)buffer.GetType().GetProperty("Count")!.GetValue(buffer)!;
            }
            var pool = (System.Collections.IEnumerable)typeof(FiberZLayerCoordinator).GetField("s_entryPool", statics)!
                .GetValue(null);
            foreach (var entry in pool)
            {
                foreach (var field in entry.GetType().GetFields())
                {
                    if (field.FieldType == typeof(VisualElement) && field.GetValue(entry) != null)
                    {
                        held++;
                    }
                }
            }
            return held;
        }

        // Restack is reached with no child reconcile over the parent to mark it pending, so only the
        // restack itself can arrange the parent.
        [TestCase("a", 30, "b,a", TestName = "AZChangeWithinTheBandIsResorted")]
        [TestCase("c", 20, "a,b,c", TestName = "AnArrivalFromTheBackBandGoesAfterItsEqualZ")]
        public void Given_AMountedBand_When_AMemberIsRestackedOutsideAPass_Then_TheFrontBandHoldsZThenTreeOrder(
            string moved, int newZ, string expected)
        {
            // Arrange — c is declared last, so tree order puts it after the z-20 b it joins.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "a", className: "absolute z-10"),
                V.Div(name: "b", className: "absolute z-20"),
                V.Div(name: "c", className: "absolute -z-10"),
            }));
            var ctx = mounted.Root.Reconciler.Context;

            // Act
            FiberZLayerCoordinator.Restack(ctx, root.Q<VisualElement>(moved), newZ);

            // Assert
            Assert.That(Names(Band(root.Q<VisualElement>("parent"), front: true)), Is.EqualTo(expected));
        }

        #endregion

        #region Relational variants across a relocation

        [Test]
        public void Given_AZManagedConsumerBetweenTwoPeerSources_When_ResolvingThePeerSearch_Then_ItFindsOnlyTheOneDeclaredBeforeIt()
        {
            // Arrange — parent: [source(ordinary, "peer"), consumer(z-managed), later(ordinary, "peer")]. The
            // consumer's real element physically paints in the front band, after both sources — the search
            // routes through the consumer's PLACEHOLDER position instead, which only "source" precedes.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "source", className: "peer"),
                V.Div(name: "consumer", className: "absolute z-10"),
                V.Div(name: "later", className: "peer"),
            }));
            var ctx = mounted.Root.Reconciler.Context;

            // Act
            var found = new List<VisualElement>();
            StyleRelationalVariantManipulator.FindSources(root.Q<VisualElement>("consumer"), isPeer: true, "peer", ctx, found);

            // Assert
            Assert.That(string.Join(",", found.ConvertAll(e => e.name)), Is.EqualTo("source"));
        }

        [Test]
        public void Given_AZManagedPeerSourceAndAnOrdinaryConsumer_When_ResolvingThePeerSearch_Then_ItFindsTheRelocatedSource()
        {
            // Arrange — parent: [source(z-managed, "peer"), consumer(ordinary)]. Only the source's placeholder
            // sits among the consumer's preceding siblings; the source itself paints after the consumer.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "source", className: "peer absolute z-10"),
                V.Div(name: "consumer"),
            }));
            var ctx = mounted.Root.Reconciler.Context;
            var source = root.Q<VisualElement>("source");

            // Act
            var found = new List<VisualElement>();
            StyleRelationalVariantManipulator.FindSources(root.Q<VisualElement>("consumer"), isPeer: true, "peer", ctx, found);

            // Assert — the source's relocation is a term: left at its ordinary slot it is the consumer's own
            // physical preceding sibling, and the search finds it without ever reading a placeholder.
            Assert.That(
                (IsZManaged(ctx, source), found.Count == 1 && ReferenceEquals(found[0], source)),
                Is.EqualTo((true, true)));
        }

        #endregion

        #region Per-parent isolation

        [Test]
        public void Given_TwoSiblingStackingParentsEachWithZManagedChildren_When_Mounted_Then_TheirBandsAndOrderingDoNotCrossTalk()
        {
            // Arrange / Act — two INDEPENDENT stacking parents, each arranging its own band; per-parent keying
            // (ZLayerHosts is keyed by the stacking parent element, not a single global registry) must keep them
            // from cross-talking.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "wrapper", children: new VNode[]
            {
                V.Div(name: "parentA", className: "relative", children: new VNode[]
                {
                    V.Div(name: "a1", className: "absolute z-30"),
                    V.Div(name: "a2", className: "absolute z-10"),
                }),
                V.Div(name: "parentB", className: "relative", children: new VNode[]
                {
                    V.Div(name: "b1", className: "absolute z-20"),
                }),
            }));
            var frontA = Band(root.Q<VisualElement>("parentA"), front: true);
            var frontB = Band(root.Q<VisualElement>("parentB"), front: true);

            // Assert — parentA's band holds only its own two children, correctly sorted, unaffected by parentB's own
            // separate single member.
            Assert.That((frontA.Names, frontB.Names), Is.EqualTo(("a2,a1", "b1")));
        }

        #endregion

        #region V.Anchored without the "absolute" class

        [Test]
        public void Given_AnAnchoredElementWithNoAbsoluteClass_When_ItCarriesAZClass_Then_ItStillRelocatesIntoTheFrontBand()
        {
            // Arrange / Act — V.Anchored forces position:absolute via AnchoredDriver.Attach as a plain inline
            // style (never through the "absolute" utility class), so the coordinator reads Props.Anchored as a
            // position of its own. AnchoredDriver.Sync bails cleanly with no live panel (element.panel == null),
            // so this needs no host panel, like every other test in this fixture.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Anchored(target: null, name: "child", className: "z-10"),
            }));
            var ctx = mounted.Root.Reconciler.Context;
            var child = root.Q<VisualElement>("child");
            var carriesNoAbsoluteClass = child != null && !child.ClassListContains("absolute");

            // Assert — the anchoring classified it anyway. The absent utility class is a term rather than a
            // precondition because the class would classify the child on its own, so gaining it would make this
            // pass for the wrong reason.
            Assert.That((carriesNoAbsoluteClass, IsZManaged(ctx, child)), Is.EqualTo((true, true)));
        }

        #endregion

        #region Keyed reorder moves the z-managed item itself

        [Component]
        private static VNode MoveZManagedHost()
        {
            var moved = Hooks.UseStore(s_moveStore, x => x);
            var zChild = V.Div(name: "zchild", key: "z", className: "absolute z-10");
            var a = V.Button(name: "a", key: "a");
            var b = V.Button(name: "b", key: "b");
            var children = moved ? new VNode[] { a, b, zChild } : new VNode[] { zChild, a, b };
            return V.Div(name: "list", className: "relative", children: children);
        }

        [Test]
        public void Given_AKeyedReorder_When_TheZManagedItemMovesToADifferentLogicalIndex_Then_ThePlaceholderTracksItAndTheRealElementStaysRegistered()
        {
            // Arrange — the z-managed child starts FIRST among three keyed siblings.
            using var store = new ToggleStore<bool>(false);
            s_moveStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(MoveZManagedHost, key: "root"));
            var ctx = mounted.Root.Reconciler.Context;
            var zchild = root.Q<VisualElement>("zchild");
            var registeredBeforeTheMove = IsZManaged(ctx, zchild);

            // Act — the keyed reorder moves the z-managed item to the LAST logical position.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the SAME real element instance is still registered, now with its placeholder in the LAST
            // logical slot — zchild itself, painting in the front band, is not a logical child; default (null
            // Placeholder) if the registration itself was lost, so this also proves the "stays registered" half.
            var list = root.Q<VisualElement>("list");
            var memberAfter = default(ZLayerMember);
            if (zchild != null) { ctx.ZLayerMembers.TryGetValue(zchild, out memberAfter); }
            var lastLogicalIndex = LogicalChildSlots.ToPhysical(list, LogicalChildSlots.Count(list) - 1);
            var stillRegisteredAtTrackedPosition = memberAfter.Placeholder != null
                && ReferenceEquals(list.ElementAt(lastLogicalIndex), memberAfter.Placeholder);
            Assert.That((registeredBeforeTheMove, stillRegisteredAtTrackedPosition), Is.EqualTo((true, true)));
        }

        #endregion

        #region Nested stacking parents

        [Test]
        public void Given_AZManagedElementWhoseRealContentContainsItsOwnZManagedDescendant_When_Mounted_Then_BothLayersResolveIndependently()
        {
            // Arrange / Act — "mid" is z-managed under "outer"; its own child "innerParent" is a SEPARATE
            // stacking parent for "inner" — an independent nested layer, unrelated to "mid"'s own relocation.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "outer", className: "relative", children: new VNode[]
            {
                V.Div(name: "mid", className: "absolute z-10", children: new VNode[]
                {
                    V.Div(name: "innerParent", className: "relative", children: new VNode[]
                    {
                        V.Div(name: "inner", className: "absolute z-20"),
                    }),
                }),
            }));
            var outerFront = Band(root.Q<VisualElement>("outer"), front: true);
            var innerParent = root.Q<VisualElement>("innerParent");
            var innerFront = innerParent == null ? null : Band(innerParent, front: true);

            // Assert — innerParent (reached inside mid's relocated real content) arranged its OWN front band,
            // holding "inner", independent of mid's own relocation under outer. Both bands are terms of the same
            // comparison: "independently" is a claim about the pair, so outer's own band cannot be a
            // precondition to it.
            Assert.That((outerFront.Names, innerFront?.Names), Is.EqualTo(("mid", "inner")));
        }

        #endregion

        #region Motion

        private static ToggleStore<string> s_motionStore;

        [Component]
        private static VNode MotionHost()
        {
            var className = Hooks.UseStore(s_motionStore, x => x);
            return V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Motion(name: "m", className: className),
                V.Div(name: "peer20", className: "absolute z-20"),
            });
        }

        private (MountedTree Mounted, VisualElement Root) MountMotion(ToggleStore<string> store)
        {
            s_motionStore = store;
            var root = new VisualElement();
            return (V.Mount(root, V.Component(MotionHost, key: "root")), root);
        }

        [Test]
        public void Given_AMotionWithAnAbsoluteAndZClass_When_Mounted_Then_ItIsRelocatedIntoTheFrontBand()
        {
            // Arrange
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Motion(name: "m", className: "absolute z-10"),
            }));

            // Assert
            var m = root.Q<VisualElement>("m");
            Assert.That(
                (IsZManaged(mounted.Root.Reconciler.Context, m),
                    Band(root.Q<VisualElement>("parent"), front: true).Contains(m)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AnOrdinaryAbsoluteMotion_When_ItGainsAZClass_Then_TheSameElementRelocatesIntoTheFrontBand()
        {
            // Arrange
            using var store = new ToggleStore<string>("absolute");
            var (mounted, root) = MountMotion(store);
            using var _ = mounted;
            var m = root.Q<VisualElement>("m");
            var startedAsAnOrdinaryDirectChild = ReferenceEquals(root.Q<VisualElement>("parent").ElementAt(0), m);

            // Act
            store.Set("absolute z-10");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (startedAsAnOrdinaryDirectChild, IsZManaged(mounted.Root.Reconciler.Context, m)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AZManagedMotion_When_ItsZClassIsDropped_Then_TheSameElementReturnsToItsOrdinarySlot()
        {
            // Arrange
            using var store = new ToggleStore<string>("absolute z-10");
            var (mounted, root) = MountMotion(store);
            using var _ = mounted;
            var m = root.Q<VisualElement>("m");
            var zManagedBeforeTheChange = IsZManaged(mounted.Root.Reconciler.Context, m);

            // Act
            store.Set("absolute");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (zManagedBeforeTheChange, ReferenceEquals(root.Q<VisualElement>("parent").ElementAt(0), m)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AZManagedMotionBelowAZ20Sibling_When_ItsZRisesAboveIt_Then_ItIsResortedAfterTheSibling()
        {
            // Arrange
            using var store = new ToggleStore<string>("absolute z-10");
            var (mounted, root) = MountMotion(store);
            using var _ = mounted;
            var m = root.Q<VisualElement>("m");
            var front = Band(root.Q<VisualElement>("parent"), front: true);
            var firstBeforeTheChange = ReferenceEquals(front.ElementAt(0), m);

            // Act
            store.Set("absolute z-30");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (firstBeforeTheChange, ReferenceEquals(front.ElementAt(front.childCount - 1), m)),
                Is.EqualTo((true, true)));
        }

        #endregion

        #region Time-sliced slot integrity across a relocation

        // item0 alone toggles between plain and z-managed (negative, so the BACK band is at stake); item1
        // through item9 stay ordinary. Every child is a fresh VNode each render (no ILPP memo, no shared
        // instances), so no Common-phase iteration ever takes the ReferenceEquals fast path — matching
        // TimeSlicedFiberTests' own FlatListRender shape, which is what makes "a tiny budget parks after
        // exactly one node" a deterministic guarantee rather than a timing race. The fiber's own top-level
        // output is the Fragment directly (no wrapping Div) so root itself — not some nested element — is the
        // stacking parent and the reconcile the tiny budget parks takes the time-sliceable fast path at all
        // (a nested ReconcileChildren call, the shape an intervening wrapper Div would force, always runs at
        // frameBudgetMs=0 regardless of the caller's own budget).
        [Component]
        private static VNode ZParkHost()
        {
            s_zParkFiber = FiberAmbientStack.Current;
            var zManaged = s_zParkZManaged;
            var children = new VNode[10];
            children[0] = V.Div(name: "item0", className: zManaged ? "absolute -z-10" : null);
            for (var i = 1; i < 10; i++)
            {
                children[i] = V.Div(name: "item" + i);
            }
            return V.Fragment(children: children);
        }

        // The "itemN" (N >= 1) direct children of root, in physical order, joined into one comparable string —
        // item0 and its own placeholder (unnamed) are both excluded, so this reads purely as "where did
        // item1..item9 end up, and under what name".
        private static string TrailingItemOrder(VisualElement root)
        {
            var names = new List<string>();
            foreach (var child in root.Children())
            {
                if (child.name is { Length: > 0 } n && n != "item0")
                {
                    names.Add(n);
                }
            }
            return string.Join(",", names);
        }

        private const string ExpectedTrailingItemOrder =
            "item1,item2,item3,item4,item5,item6,item7,item8,item9";

        [Test]
        public void Given_ATimeSlicedPassParksRightAfterRelocatingAnElementIntoTheBackBand_When_ThePassResumes_Then_TrailingItemsResumeAtCorrectIndices()
        {
            // Arrange — mount fully ordinary: no z anywhere yet, so the back band is empty.
            s_zParkZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ZParkHost, key: "root"));
            var noBackBandBeforeTheReRender = !Band(root, front: false).Any;

            // Act — item0 turns z-managed under a tiny forced budget: the Common-phase loop patches item0's
            // class, which puts a placeholder at its slot, then the tiny budget parks immediately after — all
            // within this ONE top-level Reconcile() call, whose own end then sorts item0 into the back band,
            // physically ahead of every trailing item.
            s_zParkZManaged = true;
            s_zParkFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_zParkFiber.FlushStateWithTinyBudgetForTest();
            var parkedAfterItem0 = s_zParkFiber.HasPendingReconcileWorkForTest();
            s_zParkFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every trailing item resumed and patched at its correct physical index: an off-by-one
            // resume patches (and renames, via PatchCommon) the WRONG physical element, leaving a stale or
            // duplicated name in this join. The relocation itself is a term too: with item0 left in its slot
            // nothing moved for the resume to miss, and the order alone reads correct.
            Assert.That(
                (noBackBandBeforeTheReRender, parkedAfterItem0,
                    Band(root, front: false).Names, TrailingItemOrder(root)),
                Is.EqualTo((true, true, "item0", ExpectedTrailingItemOrder)));
        }

        [Test]
        public void Given_ATimeSlicedPassParksRightAfterTheBackBandsOnlyElementReturnsToItsSlot_When_ThePassResumes_Then_TrailingItemsResumeAtCorrectIndices()
        {
            // Arrange — mount with item0 ALREADY z-managed (synchronous: the mount runs on a zero budget, which
            // the tiny-budget flush below does not reach), so the back band holds it cleanly beforehand.
            s_zParkZManaged = true;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ZParkHost, key: "root"));
            var backBandHeldItem0BeforeTheReRender = Band(root, front: false).Names == "item0";

            // Act — item0 turns back to ordinary under a tiny forced budget: the Common-phase loop patches
            // item0's class, which exchanges it with its placeholder, then the tiny budget parks immediately
            // after — all within this ONE top-level Reconcile() call, whose own end then removes the retired
            // placeholder, physically shifting every trailing item back by -1.
            s_zParkZManaged = false;
            s_zParkFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_zParkFiber.FlushStateWithTinyBudgetForTest();
            var parkedAfterItem0 = s_zParkFiber.HasPendingReconcileWorkForTest();
            s_zParkFiber.DrainTimeSlicedReconcileForTest();

            // Assert — mirrors the relocation-direction test above, in the opposite direction: every trailing
            // item resumed at its correct physical index instead of one slot past it.
            Assert.That(
                (backBandHeldItem0BeforeTheReRender, parkedAfterItem0,
                    !Band(root, front: false).Any, TrailingItemOrder(root)),
                Is.EqualTo((true, true, true, ExpectedTrailingItemOrder)));
        }

        #endregion

        #region Cross-fiber MountSlotStart contamination from a relocation

        // A parent Div hosting two independent inline-mounted sibling fibers (mirrors TimeSlicedFiberTests'
        // own SiblingHostRender two-fiber shape). A's own re-render can turn its own first item z-managed,
        // relocating it into (or, symmetrically, out of) the shared parent's back band from INSIDE A's own
        // Reconcile() call; B's own INDEPENDENT later re-render must still patch its own items at their correct
        // physical slots. No time-slicing anywhere in this region — the leak this pins is a plain
        // childCount measurement around a synchronous Reconcile call, not a parked-state concern.
        [Component]
        private static VNode CrossFiberSiblingHost() => V.Div(
            name: "cross-fiber-host", className: "relative", children: new VNode[]
            {
                V.Component(CrossFiberARender, key: "a"),
                V.Component(CrossFiberBRender, key: "b"),
            });

        [Component]
        private static VNode CrossFiberARender()
        {
            s_crossFiberAFiber = FiberAmbientStack.Current;
            return V.Fragment(children: new VNode[]
            {
                V.Div(name: "a0", className: s_crossFiberAZManaged ? "absolute -z-10" : null),
                V.Div(name: "a1"),
            });
        }

        [Component]
        private static VNode CrossFiberBRender()
        {
            s_crossFiberBFiber = FiberAmbientStack.Current;
            return V.Fragment(children: new VNode[]
            {
                V.Div(name: s_crossFiberBName),
                V.Div(name: "b1"),
            });
        }

        // Every "b*"-named child under host, in physical order, joined into one comparable string — mirrors
        // TrailingItemOrder's own name-filtered join. A's own item names are excluded so a stale, untouched
        // "a0"/"a1" a mis-slotted patch left behind cannot masquerade as one of B's own entries.
        private static string CrossFiberBOrder(VisualElement host)
        {
            var names = new List<string>();
            foreach (var child in host.Children())
            {
                if (child.name is { Length: > 0 } n && n != "a0" && n != "a1")
                {
                    names.Add(n);
                }
            }
            return string.Join(",", names);
        }

        [Test]
        public void Given_AnInlineSiblingFiberTurnsItsOwnFirstItemZManaged_When_AnIndependentSiblingFiberLaterReRendersItsOwnItems_Then_TheLaterSiblingsItemsPatchAtTheirCorrectPhysicalSlots()
        {
            // Arrange — mount fully ordinary: no z anywhere yet, so A's transition below puts the shared host's
            // first element into its back band.
            s_crossFiberAZManaged = false;
            s_crossFiberBName = "b0";
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossFiberSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-fiber-host");
            var noBackBandBeforeATransition = !Band(host, front: false).Any;

            // Act — A turns its own first item z-managed: A's patch puts a placeholder at a0's slot within that
            // SAME call, before A's own before/after measurement (ReconcileIntoSlotRange) completes, and the
            // call's end sorts a0 into the back band.
            s_crossFiberAZManaged = true;
            s_crossFiberAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossFiberAFiber);
            var aFilledTheBackBand = Band(host, front: false).Names == "a0";

            // Act — B re-renders independently, renaming its own first item.
            s_crossFiberBName = "b0-renamed";
            s_crossFiberBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossFiberBFiber);

            // Assert — a +1 from A's relocation leaking into B's MountSlotStart runs B's diff one physical slot
            // high: it patches the element that is actually its own "b1" with "b0"'s new name and creates a
            // duplicate "b1" past the end, leaving a stale, untouched "b0" behind instead of the single renamed
            // item in order.
            Assert.That(
                (noBackBandBeforeATransition, aFilledTheBackBand, CrossFiberBOrder(host)),
                Is.EqualTo((true, true, "b0-renamed,b1")));
        }

        [Test]
        public void Given_AnInlineSiblingFiberRemovesItsOwnZClass_When_AnIndependentSiblingFiberLaterReRendersItsOwnItems_Then_TheLaterSiblingsItemsStillPatchAtTheirCorrectPhysicalSlots()
        {
            // Arrange — mount with A's first item ALREADY z-managed, so the back band holds it cleanly from the
            // synchronous mount, before either fiber's own contamination-prone re-render runs.
            s_crossFiberAZManaged = true;
            s_crossFiberBName = "b0";
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossFiberSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-fiber-host");
            var backBandHeldA0BeforeATeardown = Band(host, front: false).Names == "a0";

            // Act — A removes its own item's z class: A's patch returns a0 to its slot within that SAME call,
            // and the call's end removes the retired placeholder — the counterpart of the relocation above.
            s_crossFiberAZManaged = false;
            s_crossFiberAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossFiberAFiber);
            var aEmptiedTheBackBand = !Band(host, front: false).Any;

            // Act — B re-renders independently, renaming its own first item.
            s_crossFiberBName = "b0-renamed";
            s_crossFiberBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossFiberBFiber);

            // Assert — the return direction stays consistent with the relocation direction above.
            Assert.That(
                (backBandHeldA0BeforeATeardown, aEmptiedTheBackBand, CrossFiberBOrder(host)),
                Is.EqualTo((true, true, "b0-renamed,b1")));
        }

        #endregion

        #region Cross-fiber parked-baseline rebase

        // A large flat list of fresh VNodes each render (no ILPP memo, no shared instances — mirrors
        // ZParkHost / TimeSlicedFiberTests' own FlatLeafFragment), so a tiny forced budget deterministically
        // parks after only the first node or two rather than racing host speed.
        [Component]
        private static VNode CrossParkSiblingHost() => V.Div(
            name: "cross-park-host", className: "relative", children: new VNode[]
            {
                V.Component(CrossParkARender, key: "a"),
                V.Component(CrossParkBRender, key: "b"),
            });

        [Component]
        private static VNode CrossParkARender()
        {
            s_crossParkAFiber = FiberAmbientStack.Current;
            var children = new VNode[s_crossParkACount];
            for (var i = 0; i < s_crossParkACount; i++)
            {
                children[i] = V.Div(name: "cpa" + i);
            }
            return V.Fragment(children: children);
        }

        [Component]
        private static VNode CrossParkBRender()
        {
            s_crossParkBFiber = FiberAmbientStack.Current;
            return V.Fragment(children: new VNode[]
            {
                V.Div(name: "cpb0", className: s_crossParkBZManaged ? "absolute -z-10" : null),
                V.Div(name: "cpb1"),
            });
        }

        // Every "cpa*"-named child under host, in physical order, joined into one comparable string.
        private static string CrossParkAOrder(VisualElement host)
        {
            var names = new List<string>();
            foreach (var child in host.Children())
            {
                if (child.name is { Length: > 0 } n && n.StartsWith("cpa", System.StringComparison.Ordinal))
                {
                    names.Add(n);
                }
            }
            return string.Join(",", names);
        }

        [Test]
        public void Given_AFiberParkedAndRegisteredAcrossPasses_When_AnIndependentSiblingRelocatesTheSharedParentsFirstElementIntoItsBackBand_Then_TheParkedFiberResumesAtItsSlots()
        {
            // Arrange — A mounts with a large flat list; B mounts ordinary (no z yet), so the back band is empty
            // when A's own park (below) begins.
            s_crossParkACount = 30;
            s_crossParkBZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossParkSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-park-host");
            var noBackBandBeforeBTransition = !Band(host, front: false).Any;
            var ctx = mounted.Root.Reconciler.Context;

            // Act — A re-renders under a tiny Transition budget and parks mid-commit; the pass is left
            // parked (registered in ParkedBaselineFibers) here, deliberately NOT drained yet, while B's
            // own render below runs.
            s_crossParkACount = 60;
            s_crossParkAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_crossParkAFiber.FlushStateWithTinyBudgetForTest();
            var aParked = s_crossParkAFiber.HasPendingReconcileWorkForTest();
            var aRegisteredAsParkedBaseline = ctx.ParkedBaselineFibers.Contains(s_crossParkAFiber);

            // Act — B re-renders synchronously (Normal lane), turning its own first item z-managed: the
            // shared host's FIRST negative-z child, which B's pass relocates into the back band while A is
            // STILL parked.
            s_crossParkBZManaged = true;
            s_crossParkBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossParkBFiber);
            var bFilledTheBackBand = Band(host, front: false).Any;

            // Act — drive A's still-parked pass to completion.
            s_crossParkAFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every one of A's 60 items landed at its slot in order: a resume reading B's relocation
            // as a shift of its own slots scrambles the tail it still had to create. A's own MountSlotStart is
            // never touched by B's commit — this pins the parked PendingIndexedState specifically, independent
            // of ComponentFiber.MountSlotStart propagation.
            var expected = new List<string>();
            for (var i = 0; i < 60; i++) { expected.Add("cpa" + i); }
            Assert.That(
                (noBackBandBeforeBTransition, aParked, aRegisteredAsParkedBaseline,
                    bFilledTheBackBand, CrossParkAOrder(host)),
                Is.EqualTo((true, true, true, true, string.Join(",", expected))));
        }

        [Test]
        public void Given_AFiberParkedAndRegisteredAcrossPasses_When_AnIndependentSiblingReturnsTheSharedParentsOnlyBackBandElement_Then_TheParkedFiberResumesAtItsSlots()
        {
            // Arrange — the return-direction mirror of the relocation test above: B mounts ALREADY z-managed, so
            // the shared host's back band holds it cleanly from the synchronous initial mount, before A's own
            // park (below) begins.
            s_crossParkACount = 3;
            s_crossParkBZManaged = true;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossParkSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-park-host");
            var backBandHeldBBeforeBTeardown = Band(host, front: false).Any;
            var ctx = mounted.Root.Reconciler.Context;

            // Act — A re-renders under a tiny Transition budget and parks mid-commit; the pass is left
            // parked (registered in ParkedBaselineFibers) here, deliberately NOT drained yet, while B's
            // own render below runs.
            s_crossParkACount = 60;
            s_crossParkAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_crossParkAFiber.FlushStateWithTinyBudgetForTest();
            var aParked = s_crossParkAFiber.HasPendingReconcileWorkForTest();
            var aRegisteredAsParkedBaseline = ctx.ParkedBaselineFibers.Contains(s_crossParkAFiber);

            // Act — B re-renders synchronously (Normal lane), flipping its own first item back to ordinary:
            // the shared host's ONLY back-band element returns to its slot while A is STILL parked — the
            // counterpart of the relocation test above.
            s_crossParkBZManaged = false;
            s_crossParkBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossParkBFiber);
            var bEmptiedTheBackBand = !Band(host, front: false).Any;

            // Act — drive A's still-parked pass to completion.
            s_crossParkAFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every one of A's 60 items landed at its slot in order — the return direction stays
            // consistent with the relocation direction above.
            var expected = new List<string>();
            for (var i = 0; i < 60; i++) { expected.Add("cpa" + i); }
            Assert.That(
                (backBandHeldBBeforeBTeardown, aParked, aRegisteredAsParkedBaseline,
                    bEmptiedTheBackBand, CrossParkAOrder(host)),
                Is.EqualTo((true, true, true, true, string.Join(",", expected))));
        }

        // Keyed mirror of CrossParkARender/CrossParkSiblingHost: every one of A's own children carries an
        // explicit key, so its park goes through PendingKeyedState (ContinueKeyed) instead of
        // PendingIndexedState (ContinueIndexed). The keyed branch is the one the positional cross-fiber
        // tests above never reach.
        [Component]
        private static VNode CrossParkKeyedSiblingHost() => V.Div(
            name: "cross-park-keyed-host", className: "relative", children: new VNode[]
            {
                V.Component(CrossParkKeyedARender, key: "a"),
                V.Component(CrossParkKeyedBRender, key: "b"),
            });

        [Component]
        private static VNode CrossParkKeyedARender()
        {
            s_crossParkKeyedAFiber = FiberAmbientStack.Current;
            var children = new VNode[s_crossParkKeyedACount];
            for (var i = 0; i < s_crossParkKeyedACount; i++)
            {
                children[i] = V.Div(name: "cpka" + i, key: "cpka" + i);
            }
            return V.Fragment(children: children);
        }

        [Component]
        private static VNode CrossParkKeyedBRender()
        {
            s_crossParkKeyedBFiber = FiberAmbientStack.Current;
            return V.Fragment(children: new VNode[]
            {
                V.Div(name: "cpkb0", className: s_crossParkKeyedBZManaged ? "absolute -z-10" : null),
                V.Div(name: "cpkb1"),
            });
        }

        // Every "cpka*"-named child under host, in physical order, joined into one comparable string.
        private static string CrossParkKeyedAOrder(VisualElement host)
        {
            var names = new List<string>();
            foreach (var child in host.Children())
            {
                if (child.name is { Length: > 0 } n && n.StartsWith("cpka", System.StringComparison.Ordinal))
                {
                    names.Add(n);
                }
            }
            return string.Join(",", names);
        }

        [Test]
        public void Given_AKeyedFiberParkedAndRegisteredAcrossPasses_When_AnIndependentSiblingRelocatesTheSharedParentsFirstElementIntoItsBackBand_Then_TheParkedFiberResumesAtItsSlots()
        {
            // Arrange — A mounts with a large KEYED flat list, so its own park exercises
            // RebasePendingSlotStart's PendingKeyedState branch (via ContinueKeyed) instead of the
            // positional test's PendingIndexedState/ContinueIndexed; B mounts ordinary (no z yet).
            s_crossParkKeyedACount = 30;
            s_crossParkKeyedBZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossParkKeyedSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-park-keyed-host");
            var noBackBandBeforeBTransition = !Band(host, front: false).Any;
            var ctx = mounted.Root.Reconciler.Context;

            // Act — A re-renders under a tiny Transition budget and parks mid-commit; the pass is left
            // parked (registered in ParkedBaselineFibers) here, deliberately NOT drained yet, while B's
            // own render below runs.
            s_crossParkKeyedACount = 60;
            s_crossParkKeyedAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_crossParkKeyedAFiber.FlushStateWithTinyBudgetForTest();
            var aParked = s_crossParkKeyedAFiber.HasPendingReconcileWorkForTest();
            var aRegisteredAsParkedBaseline = ctx.ParkedBaselineFibers.Contains(s_crossParkKeyedAFiber);

            // Act — B re-renders synchronously (Normal lane), turning its own first item z-managed: the
            // shared host's FIRST negative-z child, which B's pass relocates into the back band while A is
            // STILL parked.
            s_crossParkKeyedBZManaged = true;
            s_crossParkKeyedBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossParkKeyedBFiber);
            var bFilledTheBackBand = Band(host, front: false).Any;

            // Act — drive A's still-parked pass to completion.
            s_crossParkKeyedAFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every one of A's 60 items landed at its slot in order, through the keyed resume.
            var expected = new List<string>();
            for (var i = 0; i < 60; i++) { expected.Add("cpka" + i); }
            Assert.That(
                (noBackBandBeforeBTransition, aParked, aRegisteredAsParkedBaseline,
                    bFilledTheBackBand, CrossParkKeyedAOrder(host)),
                Is.EqualTo((true, true, true, true, string.Join(",", expected))));
        }

        [Test]
        public void Given_AKeyedFiberParkedAndRegisteredAcrossPasses_When_AnIndependentSiblingReturnsTheSharedParentsOnlyBackBandElement_Then_TheParkedFiberResumesAtItsSlots()
        {
            // Arrange — the return-direction mirror of the keyed relocation test above, completing the
            // {positional, keyed} x {relocation, return} matrix: B mounts ALREADY z-managed so the back band
            // holds it before A's keyed park begins.
            s_crossParkKeyedACount = 30;
            s_crossParkKeyedBZManaged = true;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossParkKeyedSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-park-keyed-host");
            var backBandHeldBBeforeBTeardown = Band(host, front: false).Any;
            var ctx = mounted.Root.Reconciler.Context;

            // Act — A re-renders under a tiny Transition budget and parks mid-commit; left parked while
            // B's own render below runs.
            s_crossParkKeyedACount = 60;
            s_crossParkKeyedAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_crossParkKeyedAFiber.FlushStateWithTinyBudgetForTest();
            var aParked = s_crossParkKeyedAFiber.HasPendingReconcileWorkForTest();
            var aRegisteredAsParkedBaseline = ctx.ParkedBaselineFibers.Contains(s_crossParkKeyedAFiber);

            // Act — B re-renders synchronously (Normal lane), flipping its own first item back to ordinary:
            // the shared host's ONLY back-band element returns to its slot while A is STILL parked.
            s_crossParkKeyedBZManaged = false;
            s_crossParkKeyedBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossParkKeyedBFiber);
            var bEmptiedTheBackBand = !Band(host, front: false).Any;

            // Act — drive A's still-parked pass to completion.
            s_crossParkKeyedAFiber.DrainTimeSlicedReconcileForTest();

            // Assert — the return direction stays consistent with the relocation direction for the keyed
            // branch too: every one of A's 60 items landed at its slot.
            var expected = new List<string>();
            for (var i = 0; i < 60; i++) { expected.Add("cpka" + i); }
            Assert.That(
                (backBandHeldBBeforeBTeardown, aParked, aRegisteredAsParkedBaseline,
                    bEmptiedTheBackBand, CrossParkKeyedAOrder(host)),
                Is.EqualTo((true, true, true, true, string.Join(",", expected))));
        }

        #endregion

        #region Arranging at a resumed slice's own completion

        [Component]
        private static VNode DrainZLayerHost()
        {
            s_drainFiber = FiberAmbientStack.Current;
            var zManaged = s_drainZManaged;
            var children = new VNode[10];
            children[0] = V.Div(name: "item0");
            children[1] = V.Div(name: "item1", className: zManaged ? "absolute -z-10" : null);
            for (var i = 2; i < 10; i++)
            {
                children[i] = V.Div(name: "item" + i);
            }
            return V.Fragment(children: children);
        }

        [Test]
        public void Given_ATransitionPassParksBeforeReachingANewZManagedItem_When_TheZTransitionRunsEntirelyInTheResumedSlice_Then_ItPaintsInTheBackBandAfterThePassCompletes()
        {
            // Arrange — mount fully ordinary.
            s_drainZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(DrainZLayerHost, key: "root"));
            var item1 = root.Q<VisualElement>("item1");
            var noBackBandBeforeTheReRender = !Band(root, front: false).Any;

            // Act — item1 turns z-managed under a tiny forced budget: the first slice parks right after
            // item0's own iteration, BEFORE the diff even reaches item1 — so item1's own transition runs
            // entirely inside a RESUMED slice, never inside the one Reconciler.Reconcile call whose own end
            // arranges the parent.
            s_drainZManaged = true;
            s_drainFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_drainFiber.FlushStateWithTinyBudgetForTest();
            var parkedBeforeItem1 = s_drainFiber.HasPendingReconcileWorkForTest();
            s_drainFiber.DrainTimeSlicedReconcileForTest();

            // Assert — the resumed slice's own completion arranges the parent too, or item1 stays where its
            // placeholder put it, right after its slot.
            Assert.That(
                (noBackBandBeforeTheReRender, parkedBeforeItem1, Band(root, front: false).Names),
                Is.EqualTo((true, true, "item1")));
        }

        #endregion

        #region A resume tick that relocates an element and re-parks

        [Test]
        public void Given_AParkedFibersOwnResumeTickRelocatesAnElementAndReParks_When_ItFinishes_Then_TrailingItemsLandAtCorrectIndices()
        {
            // Arrange — mount fully ordinary (reuses DrainZLayerHost: item1 is the one the toggle below
            // turns z-managed).
            s_drainZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(DrainZLayerHost, key: "root"));
            var ctx = mounted.Root.Reconciler.Context;
            var noBackBandBeforeTheReRender = !Band(root, front: false).Any;

            // Act — tick 1 (via FlushState): item0 parks first, which is what registers the fiber in
            // ParkedBaselineFibers BEFORE its own later relocating tick runs.
            s_drainZManaged = true;
            s_drainFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_drainFiber.FlushStateWithTinyBudgetForTest();
            var parkedAfterItem0 = s_drainFiber.HasPendingReconcileWorkForTest();
            var registeredAsParkedBaseline = ctx.ParkedBaselineFibers.Contains(s_drainFiber);

            // Act — tick 2 (a single manual resume, which continues at the tiny budget tick 1 captured on the
            // fiber): processes item1, relocating it into the shared parent's back band INSIDE this tick, then
            // re-parks immediately after (item2..item9 remain), still registered in ParkedBaselineFibers.
            FiberWorkLoop.ContinueReconcile(s_drainFiber);
            var resumeTickFilledTheBackBand = Band(root, front: false).Names == "item1";
            var reParkedWhileStillRegistered = s_drainFiber.HasPendingReconcileWorkForTest();

            // Act — drain the remainder; what this case reads is already fully determined by tick 2 above.
            s_drainFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every trailing item lands at its slot.
            Assert.That(
                (noBackBandBeforeTheReRender, parkedAfterItem0, registeredAsParkedBaseline,
                    resumeTickFilledTheBackBand, reParkedWhileStillRegistered, TrailingItemOrder(root)),
                Is.EqualTo((true, true, true, true, true, "item2,item3,item4,item5,item6,item7,item8,item9")));
        }

        #endregion

        #region A z change inside a time-sliced pass

        private static ComponentFiber s_sliceFiber;
        private static string s_sliceAClass;
        private static string s_sliceBClass;
        private static bool s_sliceKeyed;

        [Component]
        private static VNode SlicedLayerHost()
        {
            s_sliceFiber = FiberAmbientStack.Current;
            var children = new VNode[10];
            children[0] = V.Div(name: "a", className: "absolute " + s_sliceAClass, key: s_sliceKeyed ? "a" : null);
            children[1] = V.Div(name: "b", className: "absolute " + s_sliceBClass, key: s_sliceKeyed ? "b" : null);
            for (var i = 2; i < 10; i++)
            {
                children[i] = V.Div(name: "item" + i, key: s_sliceKeyed ? "item" + i : null);
            }
            return V.Fragment(children: children);
        }

        // The arrangement at the end of a slice that parks the pass sorts the band, though the parked diff keeps
        // every slot occupant where it is.
        [TestCase("z-30", "z-20", 0, TestName = "TheChangedElementIsPatchedInTheFirstSlice")]
        [TestCase("z-10", "z-5", 1, TestName = "TheChangedElementIsPatchedInAResumedSlice")]
        public void Given_AParkedTimeSlicedPass_When_AZChangeWithinTheBandWasPatchedInASlice_Then_TheBandIsInOrderBeforeThePassFinishes(
            string aAfter, string bAfter, int resumes)
        {
            // Arrange — a at z-10 and b at z-20 mount in that order; the tiny budget parks after one node.
            s_sliceAClass = "z-10";
            s_sliceBClass = "z-20";
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(SlicedLayerHost, key: "root"));
            var front = Band(root, front: true);
            Assume.That(Names(front), Is.EqualTo("a,b"), "Precondition: mounted in z order");

            // Act
            s_sliceAClass = aAfter;
            s_sliceBClass = bAfter;
            s_sliceFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_sliceFiber.FlushStateWithTinyBudgetForTest();
            for (var i = 0; i < resumes; i++)
            {
                FiberWorkLoop.ContinueReconcile(s_sliceFiber);
            }
            Assume.That(s_sliceFiber.HasPendingReconcileWorkForTest(), Is.True, "Precondition: the pass is still parked");

            // Assert
            Assert.That(Names(front), Is.EqualTo("b,a"));
        }

        [TestCase(false, TestName = "AnUnkeyedDiffResumes")]
        [TestCase(true, TestName = "AKeyedDiffResumes")]
        public void Given_ABandPutOutOfOrderWhileAPassIsParked_When_ThePassResumes_Then_TheBandIsBackInZOrder(bool keyed)
        {
            // Arrange — a at z-10 and b at z-20, neither of which the re-render changes, so no slice places a
            // member; the tiny budget parks the re-render after its first node.
            s_sliceAClass = "z-10";
            s_sliceBClass = "z-20";
            s_sliceKeyed = keyed;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(SlicedLayerHost, key: "root"));
            var front = Band(root, front: true);
            s_sliceFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_sliceFiber.FlushStateWithTinyBudgetForTest();
            root.Q<VisualElement>("a").PlaceInFront(root.Q<VisualElement>("b"));
            Assume.That(Names(front), Is.EqualTo("b,a"), "Precondition: the band is out of z order");

            // Act
            s_sliceFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(Names(front), Is.EqualTo("a,b"));
        }

        #endregion

        #region Structural variants across a stacking parent

        // negz is declared LAST (not first) so "ordinary"/"second" are genuinely the 1st/2nd LOGICAL siblings
        // — the back band sits at the stacking parent's own PHYSICAL front regardless of where among the
        // siblings the negative-z element itself was declared, so negz still physically precedes both of them.
        // appendThird changes ONLY the child COUNT (a brand-new, structural-class-free sibling), never
        // ordinary/second's own className — so their own ApplyStructuralVariantConfig never re-fires; only
        // "parent"'s own post-children sweep (ApplyStructuralVariants) re-derives their positions when this
        // toggles.
        [Component]
        private static VNode StructuralSweepHost()
        {
            var appendThird = Hooks.UseStore(s_structuralSweepStore, x => x);
            var children = new List<VNode>
            {
                V.Div(name: "ordinary", className: "first:bg-mark"),
                V.Div(name: "second", className: "[&:nth-child(2)]:bg-mark"),
                V.Div(name: "negz", className: "absolute -z-10"),
            };
            if (appendThird)
            {
                children.Add(V.Div(name: "third"));
            }
            return V.Div(name: "parent", className: "relative", children: children.ToArray());
        }

        [Test]
        public void Given_OrdinarySiblingsCarryingFirstAndNthChildVariants_When_ANewSiblingIsAppendedAfterTheBackBandHoldsAnElement_Then_TheSweepStillExcludesItFromTheirPositions()
        {
            // Arrange — mount with negz already in the back band. A FRESH mount's own post-children sweep runs
            // BEFORE the pass end that sorts negz there, so this first sweep computes ordinary/second's positions
            // with nothing ahead of them yet — correct at that instant, but not a pin of the exclusion itself;
            // only a LATER sweep, re-triggered with negz already physically ahead of them, exercises that.
            using var store = new ToggleStore<bool>(false);
            s_structuralSweepStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(StructuralSweepHost, key: "root"));
            var backBandHeldNegzAtMount = Band(root.Q<VisualElement>("parent"), front: false).Names == "negz";
            var ordinaryCarriedItsMountTimePayload = root.Q<VisualElement>("ordinary").ClassListContains("bg-mark");

            // Act — append an unrelated new sibling: "parent" itself re-patches (a genuine child-count change),
            // so its own post-children pass re-derives every position via the sweep
            // (FiberNodePatcher.ApplyStructuralVariants) — this time with negz already physically first.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — RED if the re-derived sweep counts negz as a sibling at its band position: it would read
            // "ordinary" as sibling index 1 (negz counted as index 0) and "second" as index 2, matching neither
            // first: nor nth-child(2) (which expects 0-based index 1) — un-applying the payload each correctly
            // held after the mount.
            Assert.That(
                (backBandHeldNegzAtMount, ordinaryCarriedItsMountTimePayload,
                    root.Q<VisualElement>("ordinary").ClassListContains("bg-mark"),
                    root.Q<VisualElement>("second").ClassListContains("bg-mark")),
                Is.EqualTo((true, true, true, true)));
        }

        // "ordinary" is its OWN inline-mounted child component (declared before negz, so it is genuinely
        // logical sibling 0), so its LATER re-render (triggered by its own store subscription) reconciles
        // directly against "parent" (its shared MountPoint, via ChildReconciler.Reconcile's slot-range
        // addressing) without StructuralPatchParentHost itself ever re-rendering. That is deliberate: "parent"
        // itself getting patched would ALSO re-run its own post-children sweep (ApplyStructuralVariants
        // — the same hunk StructuralSweepHost above exercises), which would re-derive (and so silently mask a
        // broken) logical-index conversion in ApplyStructuralVariantConfig moments later in the very same
        // patch. Only isolating "ordinary"'s own re-render like this exercises ApplyStructuralVariantConfig's
        // immediate-evaluation branch on its own.
        [Component]
        private static VNode StructuralPatchParentHost() => V.Div(name: "parent", className: "relative", children: new VNode[]
        {
            V.Component(StructuralPatchOrdinaryRender, key: "ordinary"),
            V.Div(name: "negz", className: "absolute -z-10"),
        });

        [Component]
        private static VNode StructuralPatchOrdinaryRender()
        {
            var apply = Hooks.UseStore(s_structuralPatchStore, x => x);
            return V.Div(name: "ordinary", className: apply ? "first:bg-mark" : null);
        }

        [Test]
        public void Given_AnOrdinarySiblingPatchedWithAFirstVariant_When_TheBackBandAlreadyHoldsAnElement_Then_ItsImmediateEvaluationExcludesIt()
        {
            // Arrange — mount with negz already in the back band; "ordinary" has no structural class yet.
            using var store = new ToggleStore<bool>(false);
            s_structuralPatchStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(StructuralPatchParentHost, key: "root"));
            var backBandHeldNegzBeforeThePatch = Band(root.Q<VisualElement>("parent"), front: false).Names == "negz";

            // Act — "ordinary"'s own fiber re-renders (StructuralPatchParentHost's own fiber is never marked
            // dirty and stays untouched), patching its className to add first: while it is already parented —
            // routing through ApplyStructuralVariantConfig's immediate-evaluation branch alone.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — RED if ApplyStructuralVariantConfig evaluates on physical indices: the
            // immediate evaluation would read "ordinary"'s raw physical index (1, negz counted as index 0) against
            // the raw count, never matching first: (which expects index 0).
            Assert.That(
                (backBandHeldNegzBeforeThePatch, root.Q<VisualElement>("ordinary").ClassListContains("bg-mark")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AZManagedElementCarryingLastVariant_When_ItIsLogicallyTheLastDeclaredChild_Then_ItsRealElementGetsThePayload()
        {
            // Arrange/Act — "zlast"'s placeholder sits at parent's own last ordinary slot (its real content
            // paints in the trailing front band instead); last: must resolve against the PLACEHOLDER's logical
            // position (FiberNodePatcher.ApplyStructuralVariants' ZLayerPlaceholders resolution), not the real
            // element's physical position in the band.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "ordinary"),
                V.Div(name: "zlast", className: "absolute z-10 last:bg-mark"),
            }));
            var ctx = mounted.Root.Reconciler.Context;
            var zlast = root.Q<VisualElement>("zlast");

            // Assert — the relocation is a term: left at its ordinary slot, zlast is parent's own last child
            // and last: resolves against the physical position anyway, so the payload alone proves nothing.
            Assert.That(
                (IsZManaged(ctx, zlast), zlast != null && zlast.ClassListContains("bg-mark")),
                Is.EqualTo((true, true)));
        }

        #endregion
    }
}
