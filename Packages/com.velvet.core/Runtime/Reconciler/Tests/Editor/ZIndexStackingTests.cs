using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the reconciler-side mechanics of the <c>z-*</c> stacking feature (FiberZLayerCoordinator /
    /// StyleZIndexClass): a z-marked <c>absolute</c> element's real content relocates into a lazily-created
    /// per-stacking-parent layer container (front for z &gt;= 0, back — the parent's own LEADING child, so it
    /// paints behind ordinary siblings — for negative z), while a hidden placeholder holds its logical slot for
    /// the reconciler, structural variants, and focus order. None of this needs a live panel: physical DOM
    /// structure, element identity across patches, and reconciler slot-range correctness are all observable on
    /// a bare (unattached) tree. GWT, one assert per case.
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
            // Defensive reset (mirrors TimeSlicedFiberTests' own SetUp/TearDown pair) so a tiny budget forced
            // by an earlier failing test can never leak into this one.
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
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
        }

        [TearDown]
        public void TearDown()
        {
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
        }

        // Finds the front (z >= 0) or back (negative z) layer container directly under parent, or null when
        // no z-managed child of that sign has mounted yet.
        private static VisualElement FindLayerContainer(VisualElement parent, bool front)
        {
            var marker = front ? FiberZLayerCoordinator.FrontMarkerClass : FiberZLayerCoordinator.BackMarkerClass;
            foreach (var child in parent.Children())
            {
                if (child.ClassListContains(marker))
                {
                    return child;
                }
            }
            return null;
        }

        #region Physical order

        [Test]
        public void Given_MultipleAbsoluteSiblingsWithDifferentPositiveZ_When_Mounted_Then_TheyArePhysicallySortedByZUnderTheFrontLayer()
        {
            // Arrange / Act — declared out of ascending order; the front layer must still sort them.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "ordinary"),
                V.Div(name: "z30", className: "absolute z-30"),
                V.Div(name: "z10", className: "absolute z-10"),
                V.Div(name: "z20", className: "absolute z-20"),
            }));
            var front = FindLayerContainer(root.Q<VisualElement>("parent"), front: true);
            Assume.That(front, Is.Not.Null, "Precondition: a front z-layer container was created");

            // Assert
            Assert.That((front.ElementAt(0).name, front.ElementAt(1).name, front.ElementAt(2).name),
                Is.EqualTo(("z10", "z20", "z30")));
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

            // Assert — the back layer container is parent's own first physical child.
            Assert.That(parent.ElementAt(0), Is.SameAs(FindLayerContainer(parent, front: false)));
        }

        [Test]
        public void Given_ANegativeZChild_When_Mounted_Then_ItNeverEscapesToBeforeItsOwnStackingParent()
        {
            // Arrange / Act — negative z is a genuine engine dead end (one paint traversal; a child can only
            // paint after its own parent's background): the back layer is always the stacking parent's OWN
            // leading child, never hoisted to become the parent's preceding sibling under the grandparent.
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
            Assume.That(FindLayerContainer(root.Q<VisualElement>("parent"), front: false), Is.Not.Null,
                "Precondition: a back container exists under parent");

            // Assert — "before" is still the grandparent's first child; "parent" was never reordered around it.
            Assert.That(root.Q<VisualElement>("grandparent").ElementAt(0).name, Is.EqualTo("before"));
        }

        [Test]
        public void Given_AnInFlowElementWithAZClassButNoAbsolute_When_Mounted_Then_NoLayerContainerIsEverCreated()
        {
            // Arrange / Act — z-10 without "absolute": the scope gate requires ALSO being out-of-flow, so this
            // is a documented no-op.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", children: new VNode[]
            {
                V.Div(name: "child", className: "z-10"),
            }));

            // Assert
            Assert.That(FindLayerContainer(root.Q<VisualElement>("parent"), front: true), Is.Null);
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
        public void Given_AParentWhoseOnlyChildIsANegativeZElement_When_TheNextRenderPrependsAnOrdinaryElement_Then_TheBackContainerStaysTheParentsFirstPhysicalChild()
        {
            // Arrange — the back container is parent's only physical child besides negz's own placeholder;
            // declaring a new leading Button forces a positional type-flip at slot 0 (Button replaces negz
            // there), transiently leaving the back container as parent's SOLE remaining child mid-diff.
            using var store = new ToggleStore<bool>(false);
            s_typeFlipStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(TypeFlipHost, key: "root"));
            var parent = root.Q<VisualElement>("parent");
            Assume.That(FindLayerContainer(parent, front: false), Is.Not.Null,
                "Precondition: the back layer container exists");

            // Act — declares [Button, negz]: a positional type-flip at slot 0.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the back container is still parent's first physical child, not displaced behind Button.
            Assert.That(parent.ElementAt(0), Is.SameAs(FindLayerContainer(parent, front: false)));
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
            // Arrange — a back-managed sibling (the leading-offset case) interspersed among keyed buttons.
            using var store = new ToggleStore<string>("abc");
            s_churnStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ChurnList, key: "root"));
            var list = root.Q<VisualElement>("list");
            Assume.That(FindLayerContainer(list, front: false), Is.Not.Null,
                "Precondition: the back layer container exists");

            // Act — reorder, then insert, then remove ordinary keyed siblings across successive re-renders
            // while the leading back-layer container stays present the whole time: an insertion/removal shifts
            // every subsequent index differently than a pure LIS reorder, so both need their own coverage here,
            // not just the reorder the original regression pinned.
            store.Set("cba");
            mounted.FlushStateForTest();
            store.Set("bac");
            mounted.FlushStateForTest();
            store.Set("bacd"); // insert "d" at the tail
            mounted.FlushStateForTest();
            store.Set("acd"); // remove "b" from the middle
            mounted.FlushStateForTest();

            // Assert — the ordinary siblings' physical order matches the latest declared order: their own
            // slot-range math was never corrupted by the interspersed leading container, across reorder,
            // insert, and remove alike.
            var names = list.Query<Button>().ToList().ConvertAll(b => b.name);
            Assert.That(names, Is.EqualTo(new[] { "item-a", "item-c", "item-d" }));
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
            store.Set(1);
            mounted.FlushStateForTest();
            Assume.That(ctx.ZLayerMembers.Count, Is.EqualTo(0),
                "Precondition: unmounting tore down the z-managed button's registration");

            // Act — a plain (non-z) button mounts, renting the same pooled Button instance back.
            store.Set(2);
            mounted.FlushStateForTest();
            var rented = root.Q<Button>("b");

            // Assert — folded into one tuple: the plain button actually rented the SAME pooled instance (not a
            // fresh one) AND that instance carries no leftover z-layer registration. A bare
            // ContainsKey(rented)==false assert alone would pass vacuously on a fresh (non-pooled) instance, and
            // a separate Assume for the pooling fact would report Inconclusive instead of Failed on a cleanup
            // regression.
            Assert.That((ReferenceEquals(rented, original), ctx.ZLayerMembers.ContainsKey(rented)),
                Is.EqualTo((true, false)));
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
        public void Given_TheLastZMarkedChildUnderAParent_When_ItUnmounts_Then_TheLayerContainerIsRemoved()
        {
            // Arrange
            using var store = new ToggleStore<bool>(true);
            s_teardownStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(TeardownHost, key: "root"));
            var parent = root.Q<VisualElement>("parent");
            Assume.That(FindLayerContainer(parent, front: true), Is.Not.Null,
                "Precondition: the front layer container exists");

            // Act
            store.Set(false);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(FindLayerContainer(parent, front: true), Is.Null);
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
            Assume.That(ctx.ZLayerMembers.ContainsKey(child), Is.True, "Precondition: the child is z-managed");

            // Act
            store.Set(false);
            mounted.FlushStateForTest();

            // Assert — the SAME instance now occupies the parent's ordinary slot directly.
            Assert.That(ReferenceEquals(root.Q<VisualElement>("parent").ElementAt(0), child), Is.True);
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
        public void Given_AnOrdinaryAbsoluteElement_When_ItGainsAZClass_Then_TheSameElementInstanceRelocatesIntoTheLayer()
        {
            // Arrange
            using var store = new ToggleStore<bool>(false);
            s_noneToZStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(NoneToZHost, key: "root"));
            var child = root.Q<VisualElement>("child");
            var ctx = mounted.Root.Reconciler.Context;
            Assume.That(ReferenceEquals(root.Q<VisualElement>("parent").ElementAt(0), child), Is.True,
                "Precondition: the child starts as an ordinary direct child");

            // Act
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the SAME instance is now registered as z-managed (relocated into the front layer).
            Assert.That(ctx.ZLayerMembers.ContainsKey(child), Is.True);
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
            var front = FindLayerContainer(root.Q<VisualElement>("parent"), front: true);
            Assume.That(ReferenceEquals(front.ElementAt(0), a) && ReferenceEquals(front.ElementAt(1), b), Is.True,
                "Precondition: a (z-10) sorts before b (z-30)");

            // Act — swap: a becomes z-30, b becomes z-10.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the front container's order flips, using the SAME two element instances.
            Assert.That(ReferenceEquals(front.ElementAt(0), b) && ReferenceEquals(front.ElementAt(1), a), Is.True);
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
        public void Given_AZManagedElement_When_ItsSignFlipsToAContainerThatDoesNotYetExist_Then_ItsMountOrderTiebreakCarriesForwardUnchanged()
        {
            // Arrange — z-10 only: the front container exists, no back container has ever been created.
            using var store = new ToggleStore<bool>(false);
            s_signFlipStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(SignFlipHost, key: "root"));
            var ctx = mounted.Root.Reconciler.Context;
            var child = root.Q<VisualElement>("child");
            Assume.That(ctx.ZLayerMembers.ContainsKey(child), Is.True,
                "Precondition: child mounted z-managed under z-10");
            var originalOrder = ctx.ZLayerMembers[child].Order;
            Assume.That(FindLayerContainer(root.Q<VisualElement>("parent"), front: false), Is.Null,
                "Precondition: no back container exists yet");

            // Act — flip to -z-10: Reposition finds no existing back container for this parent and defers
            // through the same enqueue/drain path a fresh mount uses, carrying the element's mount-order
            // tiebreak forward instead of reassigning a fresh one.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — folded into one tuple: the flip actually created a fresh back container (the deferred/
            // enqueue branch ran, not a same-container Reposition no-op — which would leave Order untouched and
            // pass vacuously) AND the SAME order value survived the deferred round-trip (a fresh assignment
            // would differ).
            Assert.That(
                (FindLayerContainer(root.Q<VisualElement>("parent"), front: false) != null, ctx.ZLayerMembers[child].Order),
                Is.EqualTo((true, originalOrder)));
        }

        #endregion

        #region Relational variants across the layer boundary

        [Test]
        public void Given_AZManagedConsumerAndAnOrdinaryPeerSource_When_ResolvingTheLogicalPeerSearchOrigin_Then_ItStillFindsTheOrdinarySource()
        {
            // Arrange — parent: [source(ordinary, "peer"), consumer(z-managed)]. The consumer's real element
            // physically lives in the front layer container, whose preceding siblings are unrelated same-layer
            // members, not "source" — the fix routes the search through the consumer's PLACEHOLDER position.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "source", className: "peer"),
                V.Div(name: "consumer", className: "absolute z-10"),
            }));
            var ctx = mounted.Root.Reconciler.Context;

            // Act
            var found = StyleRelationalVariantManipulator.FindPrevSiblingWithClass(
                root.Q<VisualElement>("consumer"), "peer", ctx);

            // Assert
            Assert.That(found, Is.SameAs(root.Q<VisualElement>("source")));
        }

        [Test]
        public void Given_AZManagedPeerSourceAndAnOrdinaryConsumer_When_ResolvingThePeerSearch_Then_TheRelocatedSourceIsNotFound()
        {
            // Arrange — parent: [source(z-managed, "peer"), consumer(ordinary)]. Documented gap: a relocated
            // SOURCE's placeholder carries none of its marker classes, and the search only ever inspects
            // physical siblings, so it does not resolve here (real CSS would; Velvet does not, for this
            // specific relocated-source shape).
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "source", className: "peer absolute z-10"),
                V.Div(name: "consumer"),
            }));
            var ctx = mounted.Root.Reconciler.Context;

            // Act
            var found = StyleRelationalVariantManipulator.FindPrevSiblingWithClass(
                root.Q<VisualElement>("consumer"), "peer", ctx);

            // Assert
            Assert.That(found, Is.Null);
        }

        #endregion

        #region Per-parent isolation

        [Test]
        public void Given_TwoSiblingStackingParentsEachWithZManagedChildren_When_Mounted_Then_TheirLayerContainersAndOrderingDoNotCrossTalk()
        {
            // Arrange / Act — two INDEPENDENT stacking parents, each growing its own container; per-parent
            // keying (ZLayerHosts is keyed by the stacking parent element, not a single global registry) must
            // keep them from cross-talking.
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
            var frontA = FindLayerContainer(root.Q<VisualElement>("parentA"), front: true);
            var frontB = FindLayerContainer(root.Q<VisualElement>("parentB"), front: true);
            Assume.That(frontA, Is.Not.SameAs(frontB), "Precondition: each stacking parent grew its own container");

            // Assert — parentA's container holds only its own two children, correctly sorted, unaffected by
            // parentB's own separate single member.
            Assert.That((frontA.childCount, frontA.ElementAt(0).name, frontA.ElementAt(1).name),
                Is.EqualTo((2, "a2", "a1")));
        }

        #endregion

        #region V.Anchored without the "absolute" class

        [Test]
        public void Given_AnAnchoredElementWithNoAbsoluteClass_When_ItCarriesAZClass_Then_ItStillRelocatesIntoTheLayerContainer()
        {
            // Arrange / Act — V.Anchored forces position:absolute via AnchoredDriver.Attach as a plain inline
            // style (never through the "absolute" utility class), so TryClassify's out-of-flow half must accept
            // Props.Anchored on its own. AnchoredDriver.Sync bails cleanly with no live panel (element.panel ==
            // null), so this needs no host panel, like every other test in this fixture.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Anchored(target: null, name: "child", className: "z-10"),
            }));
            var ctx = mounted.Root.Reconciler.Context;
            var child = root.Q<VisualElement>("child");
            Assume.That(child.ClassListContains("absolute"), Is.False,
                "Precondition: the child carries no \"absolute\" utility class");

            // Assert — TryClassify's Props.Anchored half classified it anyway.
            Assert.That(ctx.ZLayerMembers.ContainsKey(child), Is.True);
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
            Assume.That(ctx.ZLayerMembers.ContainsKey(zchild), Is.True,
                "Precondition: the z-managed child is registered before the move");

            // Act — the keyed reorder moves the z-managed item to the LAST logical position.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — the SAME real element instance is still registered, now with its placeholder physically
            // last among the LOGICAL (non-spacer) children — list's own trailing front layer container is not
            // itself a logical child, so the last logical slot is NonSpacerChildCount - 1, not childCount - 1;
            // default (false/null Placeholder) if the registration itself was lost, so this also proves the
            // "stays registered" half.
            var list = root.Q<VisualElement>("list");
            ctx.ZLayerMembers.TryGetValue(zchild, out var memberAfter);
            var lastLogicalIndex = SilhouetteBoundsSpacer.NonSpacerChildCount(list) - 1;
            var stillRegisteredAtTrackedPosition = memberAfter.Placeholder != null
                && ReferenceEquals(list.ElementAt(lastLogicalIndex), memberAfter.Placeholder);
            Assert.That(stillRegisteredAtTrackedPosition, Is.True);
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
            var outerFront = FindLayerContainer(root.Q<VisualElement>("outer"), front: true);
            Assume.That(outerFront, Is.Not.Null, "Precondition: outer's own front container exists");
            var innerParent = root.Q<VisualElement>("innerParent");
            var innerFront = FindLayerContainer(innerParent, front: true);

            // Assert — innerParent (reached inside mid's relocated real content) grew its OWN front container,
            // holding "inner", independent of mid's own relocation under outer.
            Assert.That(innerFront != null && ReferenceEquals(innerFront.ElementAt(0), root.Q<VisualElement>("inner")),
                Is.True);
        }

        #endregion

        #region Motion incompatibility

        [Test]
        public void Given_AMotionWithAnAbsoluteAndZClass_When_Mounted_Then_ItWarnsThatZIsIgnored()
        {
            // Arrange — the warning is expected (LogAssert fails the test if it never fires). A plain Regex
            // (no IgnoreCase) mirrors ShadowWrapTests / ClipPathWrapTests' own Motion-incompatibility pins.
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"z-\* utility on a Motion is ignored"));

            // Act — z-* + absolute would classify as z-managed on a plain element; on a Motion it must not (the
            // MotionNode create path never consults the z classifier at all).
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Motion(name: "m", className: "absolute z-10"));

            // Assert — the element mounted ordinary and unrelocated; the expected warning above is enforced by
            // LogAssert at test end (an Assert.Pass would bypass that unmatched-expectation check).
            Assert.That(mounted.Root.Reconciler.Context.ZLayerMembers.ContainsKey(root.Q<VisualElement>("m")), Is.False);
        }

        #endregion

        #region Time-sliced staleness of the baked leading offset

        // item0 alone toggles between plain and z-managed (negative, so a BACK container is at stake); item1
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
        // a back container (unnamed) and item0's own placeholder (also unnamed) are both naturally excluded, so
        // this reads purely as "where did item1..item9 end up, and under what name".
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
        public void Given_ATimeSlicedPassParksRightAfterQueuingTheFirstBackContainerMember_When_TheSameFinallyDrainCreatesTheContainer_Then_TheParkedSlotStartIsRebasedSoTrailingItemsResumeAtCorrectIndices()
        {
            // Arrange — mount fully ordinary: no z anywhere yet, so no back container exists.
            s_zParkZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ZParkHost, key: "root"));
            Assume.That(FindLayerContainer(root, front: false), Is.Null,
                "Precondition: no back container exists before the timed re-render");

            // Act — item0 turns z-managed under a tiny forced budget: the Common-phase loop patches item0's
            // class, relocates it out (enqueuing the deferred z-mount) since no back container exists yet, then
            // the tiny budget parks immediately after — all within this ONE top-level Reconcile() call, whose
            // own finally-drain then creates the FIRST back container, physically shifting every trailing item
            // (and item0's own placeholder) by +1.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_zParkZManaged = true;
            s_zParkFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_zParkFiber);
            Assume.That(s_zParkFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: the tiny budget parked the pass right after item0's own iteration");
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_zParkFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every trailing item resumed and patched at its correct (post-shift) physical index.
            // RED without the fix: the resumed slice keeps reading one slot short of where the container's
            // insertion actually left each row, so each iteration patches (and renames, via PatchCommon) the
            // WRONG physical element — a cascading off-by-one that leaves a stale/duplicated name in this join.
            Assert.That(TrailingItemOrder(root), Is.EqualTo(ExpectedTrailingItemOrder));
        }

        [Test]
        public void Given_ATimeSlicedPassParksRightAfterTheLastBackContainerMemberLeaves_When_TheSameFinallyDrainRemovesTheContainer_Then_TheParkedSlotStartIsRebasedSoTrailingItemsResumeAtCorrectIndices()
        {
            // Arrange — mount with item0 ALREADY z-managed (synchronous: budget=0 at mount regardless of the
            // override, which is only ever set below), so the back container exists cleanly beforehand.
            s_zParkZManaged = true;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ZParkHost, key: "root"));
            Assume.That(FindLayerContainer(root, front: false), Is.Not.Null,
                "Precondition: the back container exists before the timed re-render");

            // Act — item0 turns back to ordinary under a tiny forced budget: the Common-phase loop patches
            // item0's class, synchronously detaches it from the (now-empty) back container and marks the
            // container for a teardown check, then the tiny budget parks immediately after — all within this
            // ONE top-level Reconcile() call, whose own finally-drain then REMOVES the now-empty back
            // container, physically shifting every trailing item back by -1.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_zParkZManaged = false;
            s_zParkFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_zParkFiber);
            Assume.That(s_zParkFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: the tiny budget parked the pass right after item0's own iteration");
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_zParkFiber.DrainTimeSlicedReconcileForTest();

            // Assert — mirrors the creation-direction test above, in the opposite (-1) direction: every trailing
            // item resumed at its correct (post-shift) physical index instead of one slot past it.
            Assert.That(TrailingItemOrder(root), Is.EqualTo(ExpectedTrailingItemOrder));
        }

        #endregion

        #region Cross-fiber MountSlotStart contamination from container churn

        // A parent Div hosting two independent inline-mounted sibling fibers (mirrors TimeSlicedFiberTests'
        // own SiblingHostRender two-fiber shape). A's own re-render can turn its own first item z-managed,
        // creating (or, symmetrically, emptying) the shared parent's back container from INSIDE A's own
        // Reconcile() call; B's own INDEPENDENT later re-render must still patch its own items at their
        // correct physical slots. No time-slicing anywhere in this region — the leak this pins is a plain
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
            // Arrange — mount fully ordinary: no z anywhere yet, so A's transition below creates the
            // shared host's very first back container.
            s_crossFiberAZManaged = false;
            s_crossFiberBName = "b0";
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossFiberSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-fiber-host");
            Assume.That(FindLayerContainer(host, front: false), Is.Null,
                "Precondition: no back container exists before A's own transition");

            // Act — A turns its own first item z-managed: A's own Reconcile() call's top-level finally
            // creates the back container synchronously within that SAME call, before A's own before/after
            // childCount measurement (ReconcileIntoSlotRange) completes.
            s_crossFiberAZManaged = true;
            s_crossFiberAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossFiberAFiber);
            Assume.That(FindLayerContainer(host, front: false), Is.Not.Null,
                "Precondition: A's own transition actually created the shared host's first back container");

            // Act — B re-renders independently, renaming its own first item.
            s_crossFiberBName = "b0-renamed";
            s_crossFiberBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossFiberBFiber);

            // Assert — RED without the fix: A's own transition leaks the container's +1 into B's
            // MountSlotStart, so B's diff runs one physical slot high — it patches the element that is
            // actually its own "b1" with "b0"'s new name and creates a duplicate "b1" past the end,
            // leaving a stale, untouched "b0" behind instead of the single renamed item in order.
            Assert.That(CrossFiberBOrder(host), Is.EqualTo("b0-renamed,b1"));
        }

        [Test]
        public void Given_AnInlineSiblingFiberRemovesItsOwnZClass_When_AnIndependentSiblingFiberLaterReRendersItsOwnItems_Then_TheLaterSiblingsItemsStillPatchAtTheirCorrectPhysicalSlots()
        {
            // Arrange — mount with A's first item ALREADY z-managed, so the back container exists cleanly
            // from the synchronous mount, before either fiber's own contamination-prone re-render runs.
            s_crossFiberAZManaged = true;
            s_crossFiberBName = "b0";
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossFiberSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-fiber-host");
            Assume.That(FindLayerContainer(host, front: false), Is.Not.Null,
                "Precondition: the back container exists before A's own teardown");

            // Act — A removes its own item's z class: A's own Reconcile() call's top-level finally REMOVES
            // the now-empty back container synchronously within that SAME call — the teardown-direction
            // counterpart of the creation above.
            s_crossFiberAZManaged = false;
            s_crossFiberAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossFiberAFiber);
            Assume.That(FindLayerContainer(host, front: false), Is.Null,
                "Precondition: A's own teardown actually removed the now-empty back container");

            // Act — B re-renders independently, renaming its own first item.
            s_crossFiberBName = "b0-renamed";
            s_crossFiberBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossFiberBFiber);

            // Assert — the teardown (-1) direction stays consistent with the creation (+1) direction
            // above: nothing breaks after the un-contamination.
            Assert.That(CrossFiberBOrder(host), Is.EqualTo("b0-renamed,b1"));
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
        public void Given_AFiberParkedAndRegisteredAcrossPasses_When_AnIndependentSiblingCreatesTheSharedParentsFirstBackContainer_Then_TheParkedFiberResumesAtRebasedPhysicalIndices()
        {
            // Arrange — A mounts with a large flat list; B mounts ordinary (no z yet), so no back
            // container exists when A's own park (below) begins.
            s_crossParkACount = 30;
            s_crossParkBZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossParkSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-park-host");
            Assume.That(FindLayerContainer(host, front: false), Is.Null,
                "Precondition: no back container exists before B's own z-transition");
            var ctx = mounted.Root.Reconciler.Context;

            // Act — A re-renders under a tiny Transition budget and parks mid-commit; the pass is left
            // parked (registered in ParkedBaselineFibers) here, deliberately NOT drained yet, while B's
            // own render below runs.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_crossParkACount = 60;
            s_crossParkAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_crossParkAFiber);
            Assume.That(s_crossParkAFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: A's own tiny-budget pass parked mid-commit");
            Assume.That(ctx.ParkedBaselineFibers.Contains(s_crossParkAFiber), Is.True,
                "Precondition: A is registered as a cross-fiber parked baseline while still parked");

            // Act — B re-renders synchronously (Normal lane), turning its own first item z-managed: the
            // shared host's FIRST negative-z child ever, so B's own top-level drain creates the back
            // container while A is STILL parked. RebaseParkedSlotsForContainerChange's ParkedBaselineFibers
            // loop — not A's own self-park rebase, which only ever fires from A's OWN drain — must be what
            // rebases A here.
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_crossParkBZManaged = true;
            s_crossParkBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossParkBFiber);
            Assume.That(FindLayerContainer(host, front: false), Is.Not.Null,
                "Precondition: B's own render actually created the shared host's first back container");

            // Act — drive A's still-parked pass to completion.
            s_crossParkAFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every one of A's 60 items landed at its correct (post-shift) physical slot in
            // order. RED without the ParkedBaselineFibers rebase branch: A's resume writes one slot short
            // of where the container's insertion actually left each row, scrambling the tail it still had
            // to create. A's own MountSlotStart is never touched by B's commit (A is declared BEFORE B, so
            // PropagateInlineSlotShift's forward-only sibling walk never reaches it) — this pins the
            // parked PendingIndexedState rebase specifically, independent of ComponentFiber.MountSlotStart
            // propagation.
            var expected = new List<string>();
            for (var i = 0; i < 60; i++) { expected.Add("cpa" + i); }
            Assert.That(CrossParkAOrder(host), Is.EqualTo(string.Join(",", expected)));
        }

        [Test]
        public void Given_AFiberParkedAndRegisteredAcrossPasses_When_AnIndependentSiblingRemovesTheSharedParentsOnlyBackContainer_Then_TheParkedFiberResumesAtRebasedPhysicalIndices()
        {
            // Arrange — the teardown-direction mirror of the creation test above: B mounts ALREADY
            // z-managed, so the shared host's back container exists cleanly from the synchronous initial
            // mount, before A's own park (below) begins.
            s_crossParkACount = 3;
            s_crossParkBZManaged = true;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossParkSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-park-host");
            Assume.That(FindLayerContainer(host, front: false), Is.Not.Null,
                "Precondition: the back container exists before B's own teardown");
            var ctx = mounted.Root.Reconciler.Context;

            // Act — A re-renders under a tiny Transition budget and parks mid-commit; the pass is left
            // parked (registered in ParkedBaselineFibers) here, deliberately NOT drained yet, while B's
            // own render below runs.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_crossParkACount = 60;
            s_crossParkAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_crossParkAFiber);
            Assume.That(s_crossParkAFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: A's own tiny-budget pass parked mid-commit");
            Assume.That(ctx.ParkedBaselineFibers.Contains(s_crossParkAFiber), Is.True,
                "Precondition: A is registered as a cross-fiber parked baseline while still parked");

            // Act — B re-renders synchronously (Normal lane), flipping its own first item back to
            // ordinary: the shared host's ONLY back-container member leaves, so B's own top-level drain
            // REMOVES the now-empty container while A is STILL parked — the teardown-direction
            // counterpart of the creation test above.
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_crossParkBZManaged = false;
            s_crossParkBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossParkBFiber);
            Assume.That(FindLayerContainer(host, front: false), Is.Null,
                "Precondition: B's own render actually removed the shared host's now-empty back container");

            // Act — drive A's still-parked pass to completion.
            s_crossParkAFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every one of A's 60 items landed at its correct (post-shift-back) physical slot in
            // order — the -1 teardown direction stays consistent with the +1 creation direction above.
            var expected = new List<string>();
            for (var i = 0; i < 60; i++) { expected.Add("cpa" + i); }
            Assert.That(CrossParkAOrder(host), Is.EqualTo(string.Join(",", expected)));
        }

        // Keyed mirror of CrossParkARender/CrossParkSiblingHost: every one of A's own children carries an
        // explicit key, so its park goes through PendingKeyedState (ContinueKeyed) instead of
        // PendingIndexedState (ContinueIndexed) — RebasePendingSlotStartIfTargeting's OTHER branch, never
        // exercised by the positional cross-fiber tests above.
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
        public void Given_AKeyedFiberParkedAndRegisteredAcrossPasses_When_AnIndependentSiblingCreatesTheSharedParentsFirstBackContainer_Then_TheParkedFiberResumesAtRebasedPhysicalIndices()
        {
            // Arrange — A mounts with a large KEYED flat list, so its own park exercises
            // RebasePendingSlotStart's PendingKeyedState branch (via ContinueKeyed) instead of the
            // positional test's PendingIndexedState/ContinueIndexed; B mounts ordinary (no z yet).
            s_crossParkKeyedACount = 30;
            s_crossParkKeyedBZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossParkKeyedSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-park-keyed-host");
            Assume.That(FindLayerContainer(host, front: false), Is.Null,
                "Precondition: no back container exists before B's own z-transition");
            var ctx = mounted.Root.Reconciler.Context;

            // Act — A re-renders under a tiny Transition budget and parks mid-commit; the pass is left
            // parked (registered in ParkedBaselineFibers) here, deliberately NOT drained yet, while B's
            // own render below runs.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_crossParkKeyedACount = 60;
            s_crossParkKeyedAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_crossParkKeyedAFiber);
            Assume.That(s_crossParkKeyedAFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: A's own tiny-budget pass parked mid-commit");
            Assume.That(ctx.ParkedBaselineFibers.Contains(s_crossParkKeyedAFiber), Is.True,
                "Precondition: A is registered as a cross-fiber parked baseline while still parked");

            // Act — B re-renders synchronously (Normal lane), turning its own first item z-managed: the
            // shared host's FIRST negative-z child ever, so B's own top-level drain creates the back
            // container while A is STILL parked.
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_crossParkKeyedBZManaged = true;
            s_crossParkKeyedBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossParkKeyedBFiber);
            Assume.That(FindLayerContainer(host, front: false), Is.Not.Null,
                "Precondition: B's own render actually created the shared host's first back container");

            // Act — drive A's still-parked pass to completion.
            s_crossParkKeyedAFiber.DrainTimeSlicedReconcileForTest();

            // Assert — every one of A's 60 items landed at its correct (post-shift) physical slot in
            // order. RED without the PendingKeyedState branch of the ParkedBaselineFibers rebase: A's
            // resume writes one slot short of where the container's insertion actually left each row.
            var expected = new List<string>();
            for (var i = 0; i < 60; i++) { expected.Add("cpka" + i); }
            Assert.That(CrossParkKeyedAOrder(host), Is.EqualTo(string.Join(",", expected)));
        }

        [Test]
        public void Given_AKeyedFiberParkedAndRegisteredAcrossPasses_When_AnIndependentSiblingRemovesTheSharedParentsOnlyBackContainer_Then_TheParkedFiberResumesAtRebasedPhysicalIndices()
        {
            // Arrange — the teardown-direction mirror of the keyed creation test above, completing the
            // {positional, keyed} x {creation, teardown} matrix: B mounts ALREADY z-managed so the back
            // container exists before A's keyed park begins.
            s_crossParkKeyedACount = 30;
            s_crossParkKeyedBZManaged = true;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CrossParkKeyedSiblingHost, key: "root"));
            var host = root.Q<VisualElement>("cross-park-keyed-host");
            Assume.That(FindLayerContainer(host, front: false), Is.Not.Null,
                "Precondition: the back container exists before B's own teardown");
            var ctx = mounted.Root.Reconciler.Context;

            // Act — A re-renders under a tiny Transition budget and parks mid-commit; left parked while
            // B's own render below runs.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_crossParkKeyedACount = 60;
            s_crossParkKeyedAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_crossParkKeyedAFiber);
            Assume.That(s_crossParkKeyedAFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: A's own tiny-budget pass parked mid-commit");
            Assume.That(ctx.ParkedBaselineFibers.Contains(s_crossParkKeyedAFiber), Is.True,
                "Precondition: A is registered as a cross-fiber parked baseline while still parked");

            // Act — B re-renders synchronously (Normal lane), flipping its own first item back to
            // ordinary: the shared host's ONLY back-container member leaves, so B's own top-level drain
            // REMOVES the now-empty container while A is STILL parked.
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_crossParkKeyedBZManaged = false;
            s_crossParkKeyedBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_crossParkKeyedBFiber);
            Assume.That(FindLayerContainer(host, front: false), Is.Null,
                "Precondition: B's own render actually removed the shared host's now-empty back container");

            // Act — drive A's still-parked pass to completion.
            s_crossParkKeyedAFiber.DrainTimeSlicedReconcileForTest();

            // Assert — the -1 teardown direction stays consistent with the +1 creation direction for the
            // keyed branch too: every one of A's 60 items landed at its correct (post-shift-back) slot.
            var expected = new List<string>();
            for (var i = 0; i < 60; i++) { expected.Add("cpka" + i); }
            Assert.That(CrossParkKeyedAOrder(host), Is.EqualTo(string.Join(",", expected)));
        }

        #endregion

        #region Draining a resumed slice's own deferred z-layer mount

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
        public void Given_ATransitionPassParksBeforeReachingANewZManagedItem_When_TheZTransitionRunsEntirelyInTheResumedSlice_Then_ItsRealContentIsAttachedInTheBackContainerAfterThePassCompletes()
        {
            // Arrange — mount fully ordinary.
            s_drainZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(DrainZLayerHost, key: "root"));
            var item1 = root.Q<VisualElement>("item1");
            Assume.That(FindLayerContainer(root, front: false), Is.Null,
                "Precondition: no back container exists before the timed re-render");

            // Act — item1 turns z-managed under a tiny forced budget: the first slice parks right after
            // item0's own iteration, BEFORE the diff even reaches item1 — so item1's own transition (and
            // its deferred z-mount enqueue, since no back container exists yet) runs entirely inside a
            // RESUMED slice, never inside the one Reconciler.Reconcile call whose own top-level finally
            // already drains.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_drainZManaged = true;
            s_drainFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_drainFiber);
            Assume.That(s_drainFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: the tiny budget parked the pass right after item0's own iteration");
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_drainFiber.DrainTimeSlicedReconcileForTest();

            // Assert — RED without draining at a resumed slice's own completion: nothing ever creates the
            // back container (creation itself lives behind the drain) and item1's real content stays
            // orphaned (parent null), so BOTH sides must be pinned non-null — a bare reference comparison
            // would let null == null pass for exactly the broken state.
            Assert.That(item1.parent, Is.Not.Null.And.SameAs(FindLayerContainer(root, front: false)));
        }

        #endregion

        #region Self-park double-rebase on a container-creating resume tick

        [Test]
        public void Given_AParkedFibersOwnResumeTickCreatesItsContainerAndReParks_When_ItFinishes_Then_TrailingItemsLandAtCorrectIndices()
        {
            // Arrange — mount fully ordinary (reuses DrainZLayerHost: item1 is the one the toggle below
            // turns z-managed).
            s_drainZManaged = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(DrainZLayerHost, key: "root"));
            var ctx = mounted.Root.Reconciler.Context;
            Assume.That(FindLayerContainer(root, front: false), Is.Null,
                "Precondition: no back container exists before the timed re-render");

            // Act — tick 1 (via FlushState): item0 parks first, which is what registers the fiber in
            // ParkedBaselineFibers BEFORE its own later container-creating tick runs.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_drainZManaged = true;
            s_drainFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_drainFiber);
            Assume.That(s_drainFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: the tiny budget parked the pass right after item0's own iteration");
            Assume.That(ctx.ParkedBaselineFibers.Contains(s_drainFiber), Is.True,
                "Precondition: the fiber is registered as a parked baseline before its own container-creating tick");

            // Act — tick 2 (a single manual resume; the budget stays deliberately tiny THROUGH this tick —
            // reset only below, after it, unlike the drain test above's own immediate reset): processes
            // item1, creating the shared parent's first back container INSIDE this same tick's own drain,
            // then re-parks immediately after (item2..item9 remain) — so the fiber is STILL registered in
            // ParkedBaselineFibers at the exact moment RebaseParkedSlotsForContainerChange runs, alongside
            // its own current.RebasePendingSlotStartIfTargeting call on the very same PendingIndexedState.
            FiberWorkLoop.ContinueReconcile(s_drainFiber);
            Assume.That(FindLayerContainer(root, front: false), Is.Not.Null,
                "Precondition: this single resume tick actually created the shared parent's first back container");
            Assume.That(s_drainFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: the same tick re-parked (item2..item9 still pending) while still registered");

            // Act — drain the remainder; the remaining ticks' own budget has no bearing on the double-rebase
            // this test targets, which is already fully determined by tick 2 above.
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_drainFiber.DrainTimeSlicedReconcileForTest();

            // Assert — RED without the fix: the container-creating tick's own +1 delta is applied twice to
            // the very same PendingIndexedState (current's own self-rebase, then the SAME fiber matched
            // again in the ParkedBaselineFibers loop, since nothing yet removed it from that registry), so
            // every trailing item resumes two physical slots ahead of where the container's single
            // insertion actually left it.
            Assert.That(TrailingItemOrder(root), Is.EqualTo("item2,item3,item4,item5,item6,item7,item8,item9"));
        }

        #endregion

        #region Structural variants across a stacking parent

        // negz is declared LAST (not first) so "ordinary"/"second" are genuinely the 1st/2nd LOGICAL siblings
        // — GetOrCreateContainer always inserts a back container at the stacking parent's own PHYSICAL index 0
        // regardless of where among the siblings the negative-z element itself was declared, so this still
        // makes the back container physically precede both of them. appendThird changes ONLY the child COUNT
        // (a brand-new, structural-class-free sibling), never ordinary/second's own className — so their own
        // ApplyStructuralVariantConfig never re-fires; only "parent"'s own post-children container sweep
        // (ApplyStructuralVariants) re-derives their positions when this toggles.
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
        public void Given_OrdinarySiblingsCarryingFirstAndNthChildVariants_When_ANewSiblingIsAppendedAfterABackContainerAlreadyExists_Then_TheContainerSweepStillExcludesTheContainerFromTheirPositions()
        {
            // Arrange — mount with the back container already created. A FRESH mount's own post-children sweep
            // runs BEFORE the finally-drain that creates the container (FiberZLayerCoordinator.
            // GetOrCreateContainer), so this first sweep computes ordinary/second's positions with no container
            // to subtract yet — correct at that instant, but not a pin of the subtraction itself; only a LATER
            // sweep, re-triggered with the container already physically present, exercises that.
            using var store = new ToggleStore<bool>(false);
            s_structuralSweepStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(StructuralSweepHost, key: "root"));
            Assume.That(
                (FindLayerContainer(root.Q<VisualElement>("parent"), front: false) != null,
                    root.Q<VisualElement>("ordinary").ClassListContains("bg-mark")),
                Is.EqualTo((true, true)),
                "Precondition: the back container exists and ordinary already carries its mount-time payload");

            // Act — append an unrelated new sibling: "parent" itself re-patches (a genuine child-count change),
            // so its own post-children pass re-derives every position via the container sweep
            // (FiberNodePatcher.ApplyStructuralVariants) — this time with the back container already present.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — RED without subtracting LeadingOffset: the re-derived sweep would read "ordinary" as
            // sibling index 1 (the back container counted as index 0) and "second" as index 2, matching neither
            // first: nor nth-child(2) (which expects 0-based index 1) — un-applying the payload each correctly
            // held after the mount.
            Assert.That(
                (root.Q<VisualElement>("ordinary").ClassListContains("bg-mark"),
                    root.Q<VisualElement>("second").ClassListContains("bg-mark")),
                Is.EqualTo((true, true)));
        }

        // "ordinary" is its OWN inline-mounted child component (declared before negz, so it is genuinely
        // logical sibling 0), so its LATER re-render (triggered by its own store subscription) reconciles
        // directly against "parent" (its shared MountPoint, via ChildReconciler.Reconcile's slot-range
        // addressing) without StructuralPatchParentHost itself ever re-rendering. That is deliberate: "parent"
        // itself getting patched would ALSO re-run its own post-children container sweep (ApplyStructuralVariants
        // — the same hunk StructuralSweepHost above exercises), which would re-derive (and so silently mask a
        // broken) LeadingOffset subtraction in ApplyStructuralVariantConfig moments later in the very same
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
        public void Given_AnOrdinarySiblingPatchedWithAFirstVariant_When_ABackContainerAlreadyExists_Then_ItsImmediateEvaluationExcludesTheContainer()
        {
            // Arrange — mount with the back container already created; "ordinary" has no structural class yet.
            using var store = new ToggleStore<bool>(false);
            s_structuralPatchStore = store;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(StructuralPatchParentHost, key: "root"));
            Assume.That(FindLayerContainer(root.Q<VisualElement>("parent"), front: false), Is.Not.Null,
                "Precondition: the back container already exists before the patch");

            // Act — "ordinary"'s own fiber re-renders (StructuralPatchParentHost's own fiber is never marked
            // dirty and stays untouched), patching its className to add first: while it is already parented —
            // routing through ApplyStructuralVariantConfig's immediate-evaluation branch alone.
            store.Set(true);
            mounted.FlushStateForTest();

            // Assert — RED without subtracting LeadingOffset in ApplyStructuralVariantConfig specifically: the
            // immediate evaluation would read "ordinary"'s raw physical index (1, the back container counted as
            // index 0) against the raw count, never matching first: (which expects index 0).
            Assert.That(root.Q<VisualElement>("ordinary").ClassListContains("bg-mark"), Is.True);
        }

        [Test]
        public void Given_AZManagedElementCarryingLastVariant_When_ItIsLogicallyTheLastDeclaredChild_Then_ItsRealElementGetsThePayload()
        {
            // Arrange/Act — "zlast"'s placeholder sits at parent's own last ordinary slot (its real content
            // lives in the trailing front layer container instead); last: must resolve against the
            // PLACEHOLDER's logical position (FiberNodePatcher.ApplyStructuralVariants' ZLayerPlaceholders
            // resolution), not the real element's physical position inside the layer container.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "parent", className: "relative", children: new VNode[]
            {
                V.Div(name: "ordinary"),
                V.Div(name: "zlast", className: "absolute z-10 last:bg-mark"),
            }));
            var ctx = mounted.Root.Reconciler.Context;
            Assume.That(ctx.ZLayerMembers.ContainsKey(root.Q<VisualElement>("zlast")), Is.True,
                "Precondition: zlast actually relocated into the front layer container");

            // Assert
            Assert.That(root.Q<VisualElement>("zlast").ClassListContains("bg-mark"), Is.True);
        }

        #endregion
    }
}
