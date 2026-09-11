using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Velvet;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="V.VirtualList{T}"/>, which renders a large fixed-height collection
    /// into a ScrollView while keeping only the visible range in the DOM.
    /// <list type="bullet">
    /// <item>Reconciling a VirtualList produces a single ScrollView host.</item>
    /// <item>The host's contentContainer holds exactly two children: a total-height spacer sized to
    /// itemHeight times the item count, and a visible-items container.</item>
    /// <item>Changing the item count rescales the spacer to the new itemHeight times count.</item>
    /// <item>Reconciling the VirtualList away removes the host and disposes its controller.</item>
    /// <item>The visible range is firstVisible..lastVisible derived from scroll offset, viewport height, and
    /// itemHeight, widened by overscan on both sides and clamped to the collection bounds.</item>
    /// <item>An empty collection renders no visible items and tolerates range updates without throwing.</item>
    /// <item>An item whose <c>keySelector</c> returns a key holding the reconciler's scope delimiter is left
    /// out of the rendered range under a warning naming it, the rest of the range rendering as it would
    /// without it, and a key the renderer set on its own node does not put that item back in.</item>
    /// <item>A row still inside the range after a range change keeps the state on its fiber, and a key
    /// the renderer set on that row's own node does not change that: the selector's key is the identity
    /// the range diff runs on.</item>
    /// <item>An item's <c>refCallback</c> has run by the time a range update returns, though the update is
    /// driven from a scroll rather than from a reconcile pass.</item>
    /// <item>The DSL rejects a null items / keySelector / renderer with <see cref="ArgumentNullException"/>, and a
    /// non-positive itemHeight with <see cref="ArgumentOutOfRangeException"/>.</item>
    /// <item>The type-erased item list admits a null element, since the source element type may itself.</item>
    /// <item>A null key is no key, as it is for <see cref="V.List{T}(IReadOnlyList{T}, Func{T, string},
    /// Func{T, VNode})"/>: the row renders alongside the rest, a range change or an update that keeps it
    /// in range reuses it by its item index so its fiber survives, one that scrolls it out runs its
    /// cleanup, and one scrolled away and back returns as a remount rather than as the element that was
    /// disposed.</item>
    /// <item>Of two items sharing a key, the second renders when the first renders nothing.</item>
    /// <item>An item renderer's throw reaches the caller, and the range update it ends releases the rows
    /// it had placed and, separately, the prior rows it had not reached, empties the visible container and
    /// leaves the tracked range naming nothing.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListTests : ReconcilerTestFixture
    {
        #region Host structure

        [Test]
        public void Given_VirtualList_When_Reconciled_Then_HostIsScrollView()
        {
            // Arrange
            var tree = Tree(CreateItems(10), itemHeight: 30f);

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Root.ElementAt(0), Is.InstanceOf<ScrollView>());
        }

        [Test]
        public void Given_VirtualList_When_Reconciled_Then_ContentContainerHoldsSpacerAndVisibleContainer()
        {
            // Arrange
            var tree = Tree(CreateItems(50), itemHeight: 25f);

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var scrollView = (ScrollView)Root.ElementAt(0);
            Assert.That(scrollView.contentContainer.childCount, Is.EqualTo(2),
                "contentContainer holds the total-height spacer and the visible-items container");
        }

        [Test]
        public void Given_VirtualList_When_Reconciled_Then_SpacerHeightIsItemHeightTimesCount()
        {
            // Arrange
            const float itemHeight = 40f;
            const int itemCount = 100;
            var tree = Tree(CreateItems(itemCount), itemHeight);

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var spacer = ((ScrollView)Root.ElementAt(0)).contentContainer.ElementAt(0);
            Assert.That(spacer.style.height.value.value, Is.EqualTo(itemHeight * itemCount).Within(0.1f));
        }

        [Test]
        public void Given_MountedVirtualList_When_ItemCountChanges_Then_SpacerHeightRescales()
        {
            // Arrange
            var tree1 = Tree(CreateItems(10), itemHeight: 30f);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var spacer = ((ScrollView)Root.ElementAt(0)).contentContainer.ElementAt(0);
            Assume.That(spacer.style.height.value.value, Is.EqualTo(300f).Within(0.1f),
                "Precondition: the spacer initially reflects 10 items");

            // Act
            var tree2 = Tree(CreateItems(20), itemHeight: 30f);
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(spacer.style.height.value.value, Is.EqualTo(600f).Within(0.1f));
        }

        [Test]
        public void Given_MountedVirtualList_When_ReconciledAway_Then_HostIsRemoved()
        {
            // Arrange
            var tree = Tree(CreateItems(10), itemHeight: 30f);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            Assume.That(Root.childCount, Is.EqualTo(1), "Precondition: the host mounted");

            // Act
            Reconciler.Reconcile(Root, tree, Array.Empty<VNode>());

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(0));
        }

        #endregion

        #region Visible range

        [Test]
        public void Given_ViewportAtTop_When_RangeUpdated_Then_RendersVisibleWindowPlusOverscan()
        {
            // Arrange — viewport 200 / itemHeight 50 spans items 0..4; overscan 2 extends the tail to item 6.
            var node = V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id),
                overscan: 2);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            Assert.That(visibleContainer.childCount, Is.EqualTo(7),
                "Items 0..4 are visible and overscan 2 adds items 5..6, for 7 rendered items");
        }

        [Test]
        public void Given_ScrolledDown_When_RangeUpdated_Then_StillRendersANonEmptyWindow()
        {
            // Arrange
            var node = V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id),
                overscan: 2);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            Assume.That(visibleContainer.childCount, Is.GreaterThan(0), "Precondition: the top window rendered items");

            // Act
            controller.UpdateVisibleRange(scrollY: 500f, viewportHeight: 200f);

            // Assert
            Assert.That(visibleContainer.childCount, Is.GreaterThan(0),
                "A scrolled-down window still renders the items now in view");
        }

        // GREEN_ON_BASE(characterization): an item's ref attaches, which the base does at the moment it
        // creates the item. Moving ref setups to the end of a reconcile pass has to keep it, and this
        // range update is driven straight from a scroll, where there is no such pass to end.
        [Test]
        public void Given_AnItemCarryingARef_When_TheRangeIsRendered_Then_TheRefHoldsTheRenderedItem()
        {
            // Arrange
            VisualElement captured = null;
            var node = V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id,
                    refCallback: element => { captured = element; return null; }),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);

            // Assert — the last item the window rendered is the one the shared capture holds.
            Assert.That(captured, Is.SameAs(visibleContainer.ElementAt(visibleContainer.childCount - 1)));
        }

        [Test]
        public void Given_AKeySelectorReturningTheDelimiter_When_TheRangeIsRendered_Then_TheOtherItemsStillRender()
        {
            // Arrange — item 1's key holds the delimiter; the renderer sets no key of its own, so the
            // selector's is the one that would go on the node.
            var node = V.VirtualList(
                items: CreateItems(3),
                keySelector: item => item.Id == "item-1" ? "a\0b" : item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(
                string.Join(",", visibleContainer.Children().Cast<Label>().Select(label => label.text)),
                Is.EqualTo("Item 0,Item 2"));
        }

        [Test]
        public void Given_ARendererKeyingItsOwnNode_When_TheSelectorReturnsTheDelimiter_Then_TheItemIsStillLeftOut()
        {
            // Arrange — the same, with the renderer keying its own node, which is what decides whether the
            // selector's key would ever reach VNode.Key.
            var node = V.VirtualList(
                items: CreateItems(3),
                keySelector: item => item.Id == "item-1" ? "a\0b" : item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(
                string.Join(",", visibleContainer.Children().Cast<Label>().Select(label => label.text)),
                Is.EqualTo("Item 0,Item 2"));
        }

        [Test]
        public void Given_AKeySelectorReturningTheDelimiter_When_TheRangeIsRendered_Then_TheSkippedItemIsReported()
        {
            // Arrange
            var node = V.VirtualList(
                items: CreateItems(3),
                keySelector: item => item.Id == "item-1" ? "a\0b" : item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("FiberVirtualListController.*NUL"));

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — LogAssert.Expect verifies the item that left the range is named rather than
            // dropped in silence
        }

        // GREEN_ON_BASE(characterization): the base renders all three of these items. No key of
        // theirs holds a delimiter, and it is the control for the cases above that read an item's
        // absence from the range: a range rendering two items whatever their keys would satisfy
        // them, and this is their arrangement with the delimiter taken out of the one key holding it.
        [Test]
        public void Given_NoKeyHoldingTheDelimiter_When_TheRangeIsRendered_Then_EveryItemRenders()
        {
            // Arrange — the two cases above, minus the delimiter.
            var node = V.VirtualList(
                items: CreateItems(3),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(
                string.Join(",", visibleContainer.Children().Cast<Label>().Select(label => label.text)),
                Is.EqualTo("Item 0,Item 1,Item 2"));
        }

        [Test]
        public void Given_EmptyCollection_When_RangeUpdated_Then_DoesNotThrow()
        {
            // Arrange
            var node = V.VirtualList(
                items: CreateItems(0),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id));
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);

            // Act + Assert
            Assert.DoesNotThrow(() => controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f));
        }

        [Test]
        public void Given_ClipPathItems_When_OverlappingRangeRepatchesThem_Then_ItemsAreNotDoubleWrapped()
        {
            // Arrange: item roots carrying a clip-path utility, so CreateElement returns the clip
            // WRAPPER and the controller stores it. The reuse path must patch the resolved INNER
            // and re-store the slot's current top-level element — patching the wrapper itself used
            // to double-wrap it and leak one extra ClipPathBinding per reused item.
            var node = V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Div(className: "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]"),
                overscan: 2);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            Assume.That(visibleContainer.childCount, Is.GreaterThan(0), "Precondition: the top window rendered items");

            // Act: an overlapping window re-renders, routing the shared keys through the patch path.
            controller.UpdateVisibleRange(scrollY: 150f, viewportHeight: 200f);

            // Assert: exactly one clip binding per rendered item — no double-wrap residue.
            Assert.That(Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(visibleContainer.childCount));
        }

        [Test]
        public void Given_MountedVirtualListItem_When_SameKeyRendersADifferentNodeType_Then_TheNewTypeIsCreatedInstead()
        {
            // Arrange: item 0 first renders as a Label under key "item-0".
            var node1 = V.VirtualList(
                items: CreateItems(10),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node1, Reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            Assume.That(visibleContainer.ElementAt(0), Is.InstanceOf<Label>(), "Precondition: item 0 first renders as a Label");

            // Act: the same key now renders as a Div — a same-key type flip, applied via Update (not a fresh mount).
            // ForceRefresh resets the tracked range but only re-renders once _viewportHeight is known (normally
            // set by a live GeometryChangedEvent), so re-supply the range directly, as the other tests do.
            var node2 = V.VirtualList(
                items: CreateItems(10),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Div(key: item.Id),
                overscan: 0);
            controller.Update(node2);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert: item 0 is a fresh VisualElement, not the old Label patched in place.
            Assert.That(visibleContainer.ElementAt(0).GetType(), Is.EqualTo(typeof(VisualElement)));
        }

        #endregion

        #region Row identity across a range change

        private static StateUpdater<string> s_keptRowMark;

        // The mark a row shows is that row's own hook state, which is what separates a row whose fiber
        // survived a range change from one rebuilt in its place: a rebuild runs UseState again and shows
        // the initial value, whichever element instance the pool hands the replacement.
        [Component]
        private static VNode MarkedRowRender(string id)
        {
            var (mark, setMark) = Hooks.UseState("a");
            if (id == "item-2")
            {
                s_keptRowMark = setMark;
            }
            return V.Label(name: "row-" + id, text: mark);
        }

        [Component]
        private static VNode RendererKeyedRowsHostRender()
            => V.VirtualList(
                items: CreateItems(10),
                keySelector: item => "sel-" + item.Id,
                itemHeight: 50f,
                renderer: item => V.Component(MarkedRowRender, item.Id, key: "ren-" + item.Id),
                overscan: 0);

        [Component]
        private static VNode UnkeyedRowsHostRender()
            => V.VirtualList(
                items: CreateItems(10),
                keySelector: item => "sel-" + item.Id,
                itemHeight: 50f,
                renderer: item => V.Component(MarkedRowRender, item.Id),
                overscan: 0);

        [Test]
        public void Given_ARendererKeyingItsOwnNode_When_ARangeChangeKeepsTheRowInRange_Then_TheRowKeepsItsState()
        {
            // Arrange — the renderer's key and the selector's are different strings, which is the whole
            // arrangement. Items 0..4 render, and item-2 is inside the window the Act moves to as well.
            s_keptRowMark = default;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(RendererKeyedRowsHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            var markSetterCaptured = !s_keptRowMark.Equals(default(StateUpdater<string>));
            s_keptRowMark.Invoke("b");
            mounted.FlushStateForTest();

            // Act — the window becomes items 1..5.
            controller.UpdateVisibleRange(scrollY: 50f, viewportHeight: 200f);

            // Assert — the name of the row now at the head of the window and the capture of the setter
            // travel with the mark, and they answer opposite failures. A range update that returned before
            // rendering leaves the mark reading as it was written, which would pass; a first window that
            // never rendered item-2 leaves the setter default, whose Invoke is a silent no-op, which would
            // fail in the words a lost row reuse fails in.
            Assert.That(
                scrollView.Q<Label>("row-item-2")?.text
                    + " under " + visibleContainer.Q<Label>()?.name
                    + " via a " + (markSetterCaptured ? "captured" : "default") + " setter",
                Is.EqualTo("b under row-item-1 via a captured setter"));
        }

        // GREEN_ON_BASE(characterization): the base reuses a row whose node the renderer left unkeyed.
        // The selector's key is the only one there is to index such a row under, so this is what says
        // the mark-writing arrangement answers on the base at all — and that the sibling case's red
        // there is the renderer's key rather than a mount that marked nothing.
        [Test]
        public void Given_ARendererLeavingTheKeyAlone_When_ARangeChangeKeepsTheRowInRange_Then_TheRowKeepsItsState()
        {
            // Arrange — the case above, with the renderer's key taken off.
            s_keptRowMark = default;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(UnkeyedRowsHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            var markSetterCaptured = !s_keptRowMark.Equals(default(StateUpdater<string>));
            s_keptRowMark.Invoke("b");
            mounted.FlushStateForTest();

            // Act — the window becomes items 1..5.
            controller.UpdateVisibleRange(scrollY: 50f, viewportHeight: 200f);

            // Assert — the head-of-window name and the setter's capture travel with the mark for the
            // reason the case above gives.
            Assert.That(
                scrollView.Q<Label>("row-item-2")?.text
                    + " under " + visibleContainer.Q<Label>()?.name
                    + " via a " + (markSetterCaptured ? "captured" : "default") + " setter",
                Is.EqualTo("b under row-item-1 via a captured setter"));
        }

        #endregion

        #region A key the selector left null

        [Component]
        private static VNode NullKeyedRowsHostRender() => NullKeyedRowsList();

        private static VirtualListNode NullKeyedRowsList()
            => V.VirtualList(
                items: CreateItems(10),
                keySelector: item => item.Id == "item-2" ? null : "sel-" + item.Id,
                itemHeight: 50f,
                renderer: item => V.Component(MarkedRowRender, item.Id, key: "ren-" + item.Id),
                overscan: 0);

        [Test]
        public void Given_AKeySelectorReturningNullForOneItem_When_TheRangeIsRendered_Then_ThatRowRendersWithTheRest()
        {
            // Arrange
            var node = V.VirtualList(
                items: CreateItems(10),
                keySelector: item => item.Id == "item-3" ? null : item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — the texts in window order rather than a count, so a row dropped the way the NUL
            // path drops one names itself.
            Assert.That(
                string.Join(",", visibleContainer.Children().Cast<Label>().Select(label => label.text)),
                Is.EqualTo("Item 0,Item 1,Item 2,Item 3,Item 4"));
        }

        [Test]
        public void Given_AKeySelectorReturningNullForOneItem_When_ARangeChangeKeepsThatRowInRange_Then_TheRowKeepsItsState()
        {
            // Arrange — the row-identity cases above, with item-2's selector key taken to null; it is
            // their mark-writing component that answers, for the reason stated above it. Two things
            // diverge from them. Where they pin which of two keys wins, this pins that a row with no key
            // is reused at all, and by which position: an index one row out reuses item-1's fiber, whose
            // mark was never written. And where the renderer-keying one of the two arranges a renderer's
            // key against a selector's string, this arranges it against the selector's null — which has
            // to land on the node and displace it, or the next range indexes the row under the renderer's
            // string and no item index finds it.
            s_keptRowMark = default;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(NullKeyedRowsHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            var markSetterCaptured = !s_keptRowMark.Equals(default(StateUpdater<string>));
            s_keptRowMark.Invoke("b");
            mounted.FlushStateForTest();

            // Act — the window becomes items 1..5.
            controller.UpdateVisibleRange(scrollY: 50f, viewportHeight: 200f);

            // Assert — the three terms for the reason the cases above give.
            Assert.That(
                scrollView.Q<Label>("row-item-2")?.text
                    + " under " + visibleContainer.Q<Label>()?.name
                    + " via a " + (markSetterCaptured ? "captured" : "default") + " setter",
                Is.EqualTo("b under row-item-1 via a captured setter"));
        }

        [Test]
        public void Given_AKeySelectorReturningNullForOneItem_When_TheListIsUpdatedOverTheSameWindow_Then_TheRowKeepsItsState()
        {
            // Arrange — the scrolling case above, with the range left where it is. What differs is the
            // pass that follows Update: Update drops the tracked range to force a render while the render
            // buffers still hold every row, so this pass is where an unkeyed row's item index is read from
            // somewhere other than the tracked range.
            s_keptRowMark = default;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(NullKeyedRowsHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            var markSetterCaptured = !s_keptRowMark.Equals(default(StateUpdater<string>));
            s_keptRowMark.Invoke("b");
            mounted.FlushStateForTest();

            // Act — Update renders nothing while no viewport height is known, so the range is re-supplied
            // as the type-flip case in the visible-range region does.
            controller.Update(NullKeyedRowsList());
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — the setter's capture rides along for the reason the scrolling case gives.
            Assert.That(
                scrollView.Q<Label>("row-item-2")?.text
                    + " via a " + (markSetterCaptured ? "captured" : "default") + " setter",
                Is.EqualTo("b via a captured setter"));
        }

        [Test]
        public void Given_EveryRowUnkeyed_When_ARangeChangeScrollsThemAllOutOfRange_Then_EachOnesCleanupRuns()
        {
            // Arrange — the instrument is a refCallback's returned action rather than the element
            // instance, which a Label being one of the two primitives FiberPrimitiveElementPool pools
            // would not settle: a released row's element can be handed straight back to the row after it.
            var cleaned = new List<string>();
            var node = V.VirtualList(
                items: CreateItems(20),
                keySelector: item => (string)null,
                itemHeight: 50f,
                renderer: CleanupRecordingRenderer(cleaned, _ => false),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Act — the window becomes items 10..14, sharing no item index with items 0..4.
            controller.UpdateVisibleRange(scrollY: 500f, viewportHeight: 200f);

            // Assert
            Assert.That(string.Join(",", cleaned.OrderBy(id => id, StringComparer.Ordinal)),
                Is.EqualTo("item-0,item-1,item-2,item-3,item-4"));
        }

        [Test]
        public void Given_AnUnkeyedRowScrolledAwayAndBack_When_ItReturns_Then_WhatWasTypedIntoItIsGone()
        {
            // Arrange — items 0..4, then 10..14, then 0..4 again. The sweep that takes item-2 out of range
            // disposes its row, so the one that comes back is a remount and starts from what its node
            // declares. The field is uncontrolled so that nothing but the element itself carries what was
            // typed: a disposed field put back on screen shows it, and a freshly created one — or one the
            // pool resets on its way back — does not.
            var node = V.VirtualList(
                items: CreateItems(20),
                keySelector: item => (string)null,
                itemHeight: 50f,
                renderer: item => V.TextField(name: "field-" + item.Id),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            var typedInto = scrollView.Q<TextField>("field-item-2");
            typedInto.value = "typed";
            var typed = typedInto.value;
            controller.UpdateVisibleRange(scrollY: 500f, viewportHeight: 200f);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — what the first field took rides along: text that never landed in it would leave the
            // returning field empty whether or not it is the same one.
            Assert.That(
                "[" + typed + "] typed, [" + scrollView.Q<TextField>("field-item-2")?.value + "] shown on return",
                Is.EqualTo("[typed] typed, [] shown on return"));
        }

        // GREEN_ON_BASE(characterization): a key whose first item renders nothing is free for the next.
        // The pass claims a key before the renderer runs and gives it back when the renderer returns
        // null, and this is the one thing that giving-back still decides now that the scrolled-out sweep
        // no longer reads the claim.
        [Test]
        public void Given_TwoItemsSharingAKeyWhoseFirstRendersNothing_When_TheRangeIsRendered_Then_TheSecondRenders()
        {
            // Arrange — item-2 takes item-1's key, and item-1's renderer returns null.
            var node = V.VirtualList(
                items: CreateItems(5),
                keySelector: item => item.Id == "item-2" ? "item-1" : item.Id,
                itemHeight: 50f,
                renderer: item => item.Id == "item-1" ? null : V.Label(text: item.Name),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(
                string.Join(",", visibleContainer.Children().Cast<Label>().Select(label => label.text)),
                Is.EqualTo("Item 0,Item 2,Item 3,Item 4"));
        }

        // The refCallback delegate is held per item across renders rather than rebuilt inside the
        // renderer. A patch handed a fresh delegate runs the cleanup the previous one returned, which would
        // put a row on the list with nothing having released it — and a failed pass never reaches the drain
        // that would install the replacement, so the release that followed would then record nothing and
        // the two errors would cancel.
        private static Func<TestItem, VNode> CleanupRecordingRenderer(
            List<string> cleaned, Func<TestItem, bool> poisoned)
        {
            var attaches = new Dictionary<string, Func<VisualElement, Action>>();
            return item =>
            {
                if (poisoned(item))
                {
                    throw new InvalidOperationException("item renderer refused " + item.Id);
                }

                if (!attaches.TryGetValue(item.Id, out var attach))
                {
                    var id = item.Id;
                    attach = _ => () => cleaned.Add(id);
                    attaches[id] = attach;
                }

                return V.Label(text: item.Name, refCallback: attach);
            };
        }

        #endregion

        #region A range update that throws

        private static VirtualListNode ThrowingRendererList(Func<TestItem, bool> poisoned, List<string> cleaned)
            => V.VirtualList(
                items: CreateItems(20),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: CleanupRecordingRenderer(cleaned, poisoned),
                overscan: 0);

        // GREEN_ON_BASE(characterization): the base already lets an item renderer's throw out of the update.
        // The unwind the cases below pin must not turn it into a silent skip, and the last two of them
        // share this arrangement exactly, so it is also what says their Act reached the renderer at all.
        [Test]
        public void Given_ARendererThrowingOnOneItem_When_ARangeChangeReachesIt_Then_TheThrowReachesTheCaller()
        {
            // Arrange
            var poisoned = false;
            var node = ThrowingRendererList(item => poisoned && item.Id == "item-5", new List<string>());
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            poisoned = true;

            // Act + Assert — the window becomes items 1..5, whose last item the renderer refuses.
            Assert.Throws<InvalidOperationException>(
                () => controller.UpdateVisibleRange(scrollY: 50f, viewportHeight: 200f));
        }

        [Test]
        public void Given_ARendererThrowingOnTheRowScrollingIn_When_ThatRangeUpdateFails_Then_TheRowsThePassHadPlacedAreReleased()
        {
            // Arrange — the window grows from items 0..4 to items 0..5, so the failed pass has taken every
            // prior row into a slot of its own and left none behind: what it holds when the renderer
            // refuses item-5 is the five it placed, and nothing else. A window that changes size is also
            // the arm of the buffer allocation that does not alias, which the case below takes the other of.
            var poisoned = false;
            var cleaned = new List<string>();
            var node = ThrowingRendererList(item => poisoned && item.Id == "item-5", cleaned);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            poisoned = true;

            // Act — the throw is the characterization case's to pin; here it is only how the pass ends.
            try
            {
                controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 250f);
            }
            catch (InvalidOperationException)
            {
            }

            // Assert
            Assert.That(string.Join(",", cleaned.OrderBy(id => id, StringComparer.Ordinal)),
                Is.EqualTo("item-0,item-1,item-2,item-3,item-4"));
        }

        [Test]
        public void Given_ARendererThrowingOnTheFirstRowOfTheNewRange_When_ThatRangeUpdateFails_Then_TheRowsThePassNeverReachedAreReleased()
        {
            // Arrange — the renderer refuses the first item of the new window, so the pass fills no slot at
            // all and everything it holds is the five prior rows it had not got to. That splits this from
            // the case above, whose five are the placed ones: between them the two halves of the unwind
            // are asked for separately rather than in a sum either half alone could satisfy.
            var poisoned = false;
            var cleaned = new List<string>();
            var node = ThrowingRendererList(item => poisoned && item.Id == "item-1", cleaned);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            poisoned = true;

            // Act — the window becomes items 1..5, whose first item the renderer refuses.
            try
            {
                controller.UpdateVisibleRange(scrollY: 50f, viewportHeight: 200f);
            }
            catch (InvalidOperationException)
            {
            }

            // Assert
            Assert.That(string.Join(",", cleaned.OrderBy(id => id, StringComparer.Ordinal)),
                Is.EqualTo("item-0,item-1,item-2,item-3,item-4"));
        }

        [Test]
        public void Given_ARendererThrowingOnOneItem_When_ThatRangeUpdateFails_Then_TheControllerNamesNoRenderedRange()
        {
            // Arrange
            var poisoned = false;
            var node = ThrowingRendererList(item => poisoned && item.Id == "item-5", new List<string>());
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            poisoned = true;

            // Act — the throw is the characterization case's to pin; here it is only how the pass ends.
            try
            {
                controller.UpdateVisibleRange(scrollY: 50f, viewportHeight: 200f);
            }
            catch (InvalidOperationException)
            {
            }

            // Assert
            Assert.That(
                RenderedIndexForTest(controller, "_firstRenderedIndex")
                    + ".." + RenderedIndexForTest(controller, "_lastRenderedIndex"),
                Is.EqualTo("-1..-1"));
        }

        [Test]
        public void Given_ARendererThrowingOnOneItem_When_ThatRangeUpdateFails_Then_TheVisibleContainerShowsNoRows()
        {
            // Arrange
            var poisoned = false;
            var node = ThrowingRendererList(item => poisoned && item.Id == "item-5", new List<string>());
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            poisoned = true;

            // Act — the throw is the characterization case's to pin; here it is only how the pass ends.
            try
            {
                controller.UpdateVisibleRange(scrollY: 50f, viewportHeight: 200f);
            }
            catch (InvalidOperationException)
            {
            }

            // Assert — the rows are gone from the DOM, not merely released: a disposed element left in the
            // container is what the user goes on scrolling past.
            Assert.That(visibleContainer.childCount, Is.EqualTo(0));
        }

        private static int RenderedIndexForTest(FiberVirtualListController controller, string field)
            => (int)typeof(FiberVirtualListController)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(controller);

        #endregion

        #region Context inheritance

        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("default");
        private static string s_lastSeen;
        private static StateUpdater<int> s_itemSetCount;

        [Component]
        private static VNode ItemConsumerRender()
        {
            s_lastSeen = Hooks.UseContext(ThemeContext);
            return V.Label(text: s_lastSeen);
        }

        [Component]
        private static VNode RerenderItemConsumerRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_itemSetCount = setCount;
            s_lastSeen = Hooks.UseContext(ThemeContext);
            return V.Label(text: $"{s_lastSeen}{count}");
        }

        private static VNode VirtualListHost(Func<TestItem, VNode> renderer)
            => V.Provider(ThemeContext, "provided", new VNode[]
            {
                V.VirtualList(
                    items: CreateItems(100),
                    keySelector: item => item.Id,
                    itemHeight: 50f,
                    renderer: renderer,
                    overscan: 0),
            });

        [Component]
        private static VNode ItemHostRender() => VirtualListHost(item => V.Component(ItemConsumerRender, key: item.Id));

        [Component]
        private static VNode RerenderItemHostRender()
            => VirtualListHost(item => V.Component(RerenderItemConsumerRender, key: item.Id));

        [Test]
        public void Given_ProviderAboveVirtualList_When_ItemRenders_Then_ReadsProvidedValue()
        {
            // Arrange: a VirtualList enclosed by a Provider, whose item renderer reads that context. Items mount
            // through the controller (outside the reconcile pass); the controller restores the context the
            // Provider established at the list's tree position by parenting items under the host fiber.
            s_lastSeen = null;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ItemHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];

            // Act: the visible range renders items (no live panel; drive the range directly).
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert: the item body read the Provider enclosing the VirtualList, not the context default.
            Assert.That(s_lastSeen, Is.EqualTo("provided"),
                "A VirtualList item inherits the context enclosing the list's tree position");
        }

        [Test]
        public void Given_VirtualListItem_When_ItemReRendersInIsolation_Then_StillReadsProvidedValue()
        {
            // Arrange: a VirtualList item that has mounted under an enclosing Provider.
            s_lastSeen = null;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(RerenderItemHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            mounted.Root.Reconciler.Context.VirtualListControllers[scrollView].UpdateVisibleRange(0f, 200f);
            Assume.That(s_lastSeen, Is.EqualTo("provided"), "Precondition: the item mounted under the provided context");

            // Act: an item re-renders on its own setState (no host / controller re-render).
            s_lastSeen = null;
            s_itemSetCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert: the spine rebuilds the enclosing Provider for the isolated re-render.
            Assert.That(s_lastSeen, Is.EqualTo("provided"),
                "An isolated re-render of a VirtualList item reconstructs the context enclosing the list");
        }

        [Test]
        public void Given_VirtualListItemsMounted_When_HostUnmounts_Then_DoesNotThrow()
        {
            // Arrange: items mounted under the host (item fibers parented under the host for context sharing).
            s_lastSeen = null;
            var root = new VisualElement();
            var mounted = V.Mount(root, V.Component(ItemHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            mounted.Root.Reconciler.Context.VirtualListControllers[scrollView].UpdateVisibleRange(0f, 200f);
            Assume.That(s_lastSeen, Is.EqualTo("provided"), "Precondition: items mounted under the host context");

            // Act + Assert: tearing down disposes items via both the VE-anchored sweep and the controller
            // cleanup; FiberRenderer.Dispose is idempotent, so the double path must not throw.
            Assert.DoesNotThrow(() => mounted.Dispose());
        }

        #endregion

        #region Provider placed INSIDE the item renderer (above the consumer)

        private static readonly ComponentContext<string> InnerContext = ComponentContext<string>.Create("inner-default");
        private static string s_innerSeen;
        private static StateUpdater<int> s_innerSetCount;

        [Component]
        private static VNode InnerProviderConsumerRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_innerSetCount = setCount;
            s_innerSeen = Hooks.UseContext(InnerContext);
            return V.Label(text: $"{s_innerSeen}{count}");
        }

        // The renderer's OWN top-level node is a Provider that encloses the consumer — mirroring Portal's drained
        // subtree, which IS reconstructed via DetachedMountContext.DescendantNodes.
        [Component]
        private static VNode InnerProviderItemHostRender()
            => V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Provider(InnerContext, "inner-provided", new VNode[]
                {
                    V.Component(InnerProviderConsumerRender, key: item.Id),
                }),
                overscan: 0);

        [Test]
        public void Given_AVirtualListItemRendererWithItsOwnProvider_When_TheConsumerReRendersInIsolation_Then_ItStillReadsThatProvider()
        {
            // Arrange: an item whose renderer wraps the consumer in its own Provider; on mount the consumer reads it.
            s_innerSeen = null;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(InnerProviderItemHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            mounted.Root.Reconciler.Context.VirtualListControllers[scrollView].UpdateVisibleRange(0f, 200f);
            Assume.That(s_innerSeen, Is.EqualTo("inner-provided"), "Precondition: the item read its own Provider on mount");

            // Act: the consumer re-renders on its own setState (no host / controller re-render).
            s_innerSeen = null;
            s_innerSetCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert: the spine reconstructs the Provider the renderer placed above the consumer (not the default).
            Assert.That(s_innerSeen, Is.EqualTo("inner-provided"),
                "An isolated re-render reconstructs a Provider the item renderer itself placed above the consumer");
        }

        #endregion

        #region DSL argument validation

        [Test]
        public void Given_NullItems_When_VirtualListBuilt_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.VirtualList<TestItem>(
                    items: null,
                    keySelector: item => item.Id,
                    itemHeight: 30f,
                    renderer: item => V.Label(text: item.Name)));
        }

        [Test]
        public void Given_NullKeySelector_When_VirtualListBuilt_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.VirtualList(
                    items: CreateItems(5),
                    keySelector: (Func<TestItem, string>)null,
                    itemHeight: 30f,
                    renderer: item => V.Label(text: item.Name)));
        }

        [Test]
        public void Given_NullRenderer_When_VirtualListBuilt_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.VirtualList(
                    items: CreateItems(5),
                    keySelector: item => item.Id,
                    itemHeight: 30f,
                    renderer: (Func<TestItem, VNode>)null));
        }

        [Test]
        public void Given_ZeroItemHeight_When_VirtualListBuilt_Then_ThrowsArgumentOutOfRangeException()
        {
            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                V.VirtualList(
                    items: CreateItems(5),
                    keySelector: item => item.Id,
                    itemHeight: 0f,
                    renderer: item => V.Label(text: item.Name)));
        }

        [Test]
        public void Given_NegativeItemHeight_When_VirtualListBuilt_Then_ThrowsArgumentOutOfRangeException()
        {
            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                V.VirtualList(
                    items: CreateItems(5),
                    keySelector: item => item.Id,
                    itemHeight: -10f,
                    renderer: item => V.Label(text: item.Name)));
        }

        #endregion

        #region Type erasure

        [Test]
        public void Given_TheTypeErasedItemList_When_ItsAnnotationIsReadBackFromTheAssembly_Then_ItsElementAdmitsNull()
        {
            // Arrange
            var surface = PublicApiSurface.RenderShippedAssemblies();

            // Act
            var items = surface.FirstOrDefault(line =>
                line.StartsWith("[Velvet] property Velvet.VirtualListNode.Items:", StringComparison.Ordinal));

            // Assert — an element goes straight back to the caller's own selector and renderer, so a source
            // list of a nullable element type erases to a nullable element type.
            Assert.That(
                items,
                Is.EqualTo("[Velvet] property Velvet.VirtualListNode.Items: "
                    + "System.Collections.Generic.IReadOnlyList`1[System.Object?]"));
        }

        #endregion

        #region Helpers

        private static VNode[] Tree(IReadOnlyList<TestItem> items, float itemHeight) => new VNode[]
        {
            V.VirtualList(
                items: items,
                keySelector: item => item.Id,
                itemHeight: itemHeight,
                renderer: item => V.Label(text: item.Name, key: item.Id)),
        };

        private sealed class TestItem
        {
            public string Id { get; init; }
            public string Name { get; init; }
        }

        private static IReadOnlyList<TestItem> CreateItems(int count)
        {
            var items = new TestItem[count];
            for (var i = 0; i < count; i++)
            {
                items[i] = new TestItem { Id = $"item-{i}", Name = $"Item {i}" };
            }
            return items;
        }

        #endregion
    }
}
