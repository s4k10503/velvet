using System;
using System.Collections.Generic;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the layout phase of a commit reads on a real panel: as a read inside React's
    /// <c>useLayoutEffect</c> or a callback ref forces the reflow the commit owes, the committed tree is laid out by
    /// the time the phase reads it.
    /// <list type="bullet">
    /// <item>A layout effect reads the box its mount commit gave an element, and the box an update commit gave it
    /// rather than the one the previous frame laid out.</item>
    /// <item>A state update the effect makes from that reading has re-rendered by the time the mount returns.</item>
    /// <item>An imperative handle built in the same phase, and a callback ref attached in it, read the laid-out box
    /// too.</item>
    /// <item>What an insertion effect of the same commit writes is in the box a layout effect reads, and in the box a
    /// callback ref reads.</item>
    /// <item>An update commit whose layout effects and handles all keep their deps lays out nothing, as a browser
    /// forces a reflow only where something reads the layout.</item>
    /// <item>A commit made inside the panel's own layout pass — a virtual list rendering its rows, or a tree mounted
    /// from a <see cref="GeometryChangedEvent"/> callback — reads the box it gives its elements too, whether the pass
    /// came from the panel's repaint or from its layout validation, and whether the event came from the pass's own
    /// changes or from its list of hierarchy changes. A measurement stored in state re-renders, and a callback ref
    /// that commits synchronously has the elements that commit adds laid out before their own refs read them.</item>
    /// <item>The commit computes that box without sending its <see cref="GeometryChangedEvent"/>s, which the panel's
    /// next layout visit sends, so a callback that mounts a tree runs once and an element the commit laid out
    /// still receives its event. A control that settles from those events, a ScrollView showing its scroller,
    /// settles in that visit, after the commit.</item>
    /// <item>The world position of an element an update moved is current in the commit's layout effects, whether
    /// its own styles moved it or a sibling's growth did.</item>
    /// <item>A box a bundled stylesheet class sizes is laid out with that class's size.</item>
    /// <item>A layout effect inside a portal into another panel reads its own element there and an element of the
    /// panel the tree is mounted on.</item>
    /// <item>A ref a hidden tree re-renders attaches without running that tree's insertion effects.</item>
    /// <item>A virtual list's row, and a component inside it, commit their layout effects once the list holds the
    /// row.</item>
    /// <item>A commit made from inside the style updater's traversal lays nothing out.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Every measured box is sized with an arbitrary value, which resolves to an inline style and so needs no
    /// stylesheet attached. The layout-effect and handle components find their element by name rather than through a
    /// callback ref, because attaching a callback ref lays the panel out on its own. An update case lays the mount out
    /// first, so that the box the last frame laid out is there for a stale read to return.
    /// </remarks>
    [TestFixture]
    internal sealed class UseLayoutEffectLayoutReadTests : PanelTestBase
    {
        private const float MountWidth = 120f;
        private const float UpdatedWidth = 200f;
        private const float AnchorHeight = 40f;
        private const float RowHeight = 30f;

        private readonly List<MountedTree> _mountedFromLayoutPass = new();

        public override void SetUp()
        {
            base.SetUp();
            s_root = _window.rootVisualElement;
            s_setWidth = default;
            s_widthReadInLayoutEffect = float.NaN;
            s_resolvedWidthReadInLayoutEffect = float.NaN;
            s_worldWidthReadInLayoutEffect = float.NaN;
            s_offsetRendered = float.NaN;
            s_handle.Set(null);
            s_stableBox.Set(null);
            s_setStableWidth = default;
            s_widthReadInRef = float.NaN;
            s_rowHeightsReadInLayoutEffect.Clear();
            s_rowsFoundInLayoutEffect.Clear();
            s_rowHeightsReadInRef.Clear();
            s_geometryEventsReceived = 0;
            s_setAnchorOffset = default;
            s_anchorWorldOffsetInLayoutEffect = float.NaN;
            s_scrollView = null;
            s_scrollerDisplayInLayoutEffect = null;
            s_hiddenInsertionRuns = 0;
            s_hiddenRefTick = -1;
            s_setSuspended = default;
            s_setHiddenTick = default;
            s_setSiblingHeight = default;
            s_otherRoot = null;
            s_neverResolves = new VelvetTaskCompletionSource<string>();
        }

        public override void TearDown()
        {
            foreach (var mounted in _mountedFromLayoutPass) mounted.Dispose();
            _mountedFromLayoutPass.Clear();
            base.TearDown();
            if (_otherWindow != null)
            {
                _otherWindow.Close();
                UnityEngine.Object.DestroyImmediate(_otherWindow);
                _otherWindow = null;
            }
        }

        private UnityEditor.EditorWindow _otherWindow;

        // A second editor window, so a portal can render into a panel other than the one the tree is mounted on.
        private UnityEditor.EditorWindow OpenOtherWindow()
        {
            _otherWindow = ScriptableObject.CreateInstance<OtherHostWindow>();
            _otherWindow.position = new Rect(0, 0, 400, 300);
            _otherWindow.Show();
            return _otherWindow;
        }

        private sealed class OtherHostWindow : UnityEditor.EditorWindow { }

        [Test]
        public void Given_ALayoutEffectReadingItsElementsWidth_When_TheTreeMounts_Then_ItReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Component(MeasuredBoxRender, key: "box"));

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutEffectReadingItsElementsResolvedWidth_When_TheTreeMounts_Then_ItReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Component(MeasuredBoxRender, key: "box"));

            // Assert
            Assert.That(s_resolvedWidthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutEffectReadingItsElementsWorldBound_When_TheTreeMounts_Then_ItReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Component(MeasuredBoxRender, key: "box"));

            // Assert
            Assert.That(s_worldWidthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_AMountedElementTheLastFrameLaidOut_When_AnUpdateWidensIt_Then_TheLayoutEffectReadsTheNewWidth()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(MeasuredBoxRender, key: "box"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_setWidth.Invoke(UpdatedWidth);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(UpdatedWidth).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutEffectThatStoresAMeasuredHeightInState_When_TheTreeMounts_Then_TheRenderBeforeTheMountReturnsCarriesTheHeight()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Component(MeasureThenAdjustRender, key: "adjust"));

            // Assert
            Assert.That(s_offsetRendered, Is.EqualTo(AnchorHeight).Within(0.01f));
        }

        [Test]
        public void Given_AnImperativeHandleThatMeasuresItsElement_When_TheTreeMounts_Then_TheHandleCarriesTheWidthTheMountGaveTheElement()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Component(MeasuringHandleRender, key: "handle"));

            // Assert
            Assert.That(s_handle.Current?.Width ?? float.NaN, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_ACallbackRefThatMeasuresItsElement_When_TheTreeMounts_Then_ItReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Div(className: $"w-[{MountWidth}px] h-[20px]", refCallback: s_measuringRef));

            // Assert
            Assert.That(s_widthReadInRef, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_AnInsertionEffectThatWidensAnElement_When_TheTreeMounts_Then_ALayoutEffectReadsTheWidenedWidth()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Component(InsertionWidenedRender, key: "widened"));

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(UpdatedWidth).Within(0.01f));
        }

        [Test]
        public void Given_AnInsertionEffectThatWidensAnElement_When_TheTreeMounts_Then_TheElementsCallbackRefReadsTheWidenedWidth()
        {
            // Arrange
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Component(InsertionWidenedWithRefRender, key: "widened"));

            // Assert
            Assert.That(s_widthReadInRef, Is.EqualTo(UpdatedWidth).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base runs no insertion effect of a tree a Suspense has hidden.
        // The layout commit skips that tree; here a ref the hidden tree re-renders runs its owners' insertion effects
        // ahead of the commit, and dropping the `fiber.LayoutEffectsHidden` check from
        // `RunInsertionEffectsAheadOfRef` runs them. The ref's own reading is folded in, so the case fails where the
        // hidden tree's ref never attaches.
        [Test]
        public void Given_ATreeASuspenseHasHidden_When_ItReRendersARef_Then_TheRefAttachesAndItsInsertionEffectDoesNotRun()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(SuspendingHostRender, key: "host"));
            s_setSuspended.Invoke(true);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_hiddenInsertionRuns = 0;
            s_setHiddenTick.Invoke(1);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That($"{s_hiddenRefTick} | {s_hiddenInsertionRuns}", Is.EqualTo("1 | 0"));
        }

        [Test]
        public void Given_AnAnchorBelowASiblingTheLastFrameLaidOut_When_AnUpdateGrowsTheSibling_Then_TheLayoutEffectReadsTheAnchorsNewWorldPosition()
        {
            // Arrange — the anchor's own styles do not change, so only the computation's layout moves it.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(GrowingSiblingRender, key: "sibling"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_setSiblingHeight.Invoke(AnchorOffset);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_anchorWorldOffsetInLayoutEffect, Is.EqualTo(AnchorOffset).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutEffectReadingAnElementSizedByAStylesheetClass_When_TheTreeMounts_Then_ItReadsTheWidthTheClassGivesIt()
        {
            // Arrange
            var root = _window.rootVisualElement;
            VelvetStyleUtilities.AttachTo(root);

            // Act
            _mounted = V.Mount(root, V.Component(SheetSizedBoxRender, key: "sheet"));
            ForcePanelUpdate(root.panel);

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(root.Q<VisualElement>("sheet-box").resolvedStyle.width).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutEffectInsideAPortalIntoAnotherPanel_When_TheTreeMounts_Then_ItReadsTheWidthTheMountGaveItsElement()
        {
            // Arrange
            s_otherRoot = OpenOtherWindow().rootVisualElement;

            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Component(PortalIntoOtherPanelRender, key: "portal"));

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutEffectInsideAPortalIntoAnotherPanelReadingTheMountedPanel_When_TheTreeMounts_Then_ItReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange — nothing on the mounted panel reads layout but the effect inside the portal.
            s_otherRoot = OpenOtherWindow().rootVisualElement;

            // Act
            _mounted = V.Mount(_window.rootVisualElement, V.Component(PortalReadingMountedPanelRender, key: "portal"));

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base mounts from a CustomStyleResolvedEvent reading the box unlaid.
        // That event is sent from inside the style updater's traversal, where a commit here lays nothing out;
        // dropping the `_applyingStyles` check in `FiberLayoutReflow` starts a second traversal under the first.
        [Test]
        public void Given_ATreeMountedFromACustomStyleResolvedEvent_When_ThePanelResolvesStyles_Then_ThePassCompletesWithTheLastLayoutRead()
        {
            // Arrange — the bundled sheet's `.dark` rule declares custom properties, which is what sends the event.
            var root = _window.rootVisualElement;
            VelvetStyleUtilities.AttachTo(root);
            var styled = new VisualElement();
            styled.AddToClassList("dark");
            var host = new VisualElement();
            EventCallback<CustomStyleResolvedEvent> onStyle = null;
            onStyle = _ =>
            {
                styled.UnregisterCallback(onStyle);
                _mountedFromLayoutPass.Add(V.Mount(host, V.Component(MeasuredBoxRender, key: "box")));
            };
            styled.RegisterCallback(onStyle);
            root.Add(styled);
            root.Add(host);

            // Act
            Exception thrown = null;
            try
            {
                ForcePanelUpdate(root.panel);
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            // Assert
            Assert.That($"{thrown?.GetType().Name ?? "none"} | {_mountedFromLayoutPass.Count} | {s_widthReadInLayoutEffect}",
                Is.EqualTo($"none | 1 | {float.NaN}"));
        }

        // GREEN_ON_BASE(characterization): the base lays out no panel from an update commit at all.
        // This pins that a commit whose only layout effect keeps its deps still leaves the box to the frame.
        [Test]
        public void Given_ALayoutEffectWhoseDepsHold_When_AnUpdateWidensItsElement_Then_TheElementKeepsTheBoxTheLastFrameLaidOut()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(StableLayoutEffectBoxRender, key: "stable"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_setStableWidth.Invoke(UpdatedWidth);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_stableBox.Current.layout.width, Is.EqualTo(MountWidth).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base lays out no panel from an update commit at all.
        // This pins that a commit whose only imperative handle keeps its deps still leaves the box to the frame.
        [Test]
        public void Given_AnImperativeHandleWhoseDepsHold_When_AnUpdateWidensItsElement_Then_TheElementKeepsTheBoxTheLastFrameLaidOut()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(StableHandleBoxRender, key: "stable"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_setStableWidth.Invoke(UpdatedWidth);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_stableBox.Current.layout.width, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_AVirtualListWhoseRowsRunLayoutEffects_When_ThePanelLaysTheListOut_Then_ARowsLayoutEffectFindsItsRowInThePanel()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, MeasuredRowList());

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_rowsFoundInLayoutEffect.Contains("a"), Is.True);
        }

        [Test]
        public void Given_AVirtualListWhoseRowsMeasureThemselves_When_ThePanelLaysTheListOut_Then_ARowsLayoutEffectReadsItsHeight()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, MeasuredRowList());

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(ReadRow(s_rowHeightsReadInLayoutEffect, "a"), Is.EqualTo(RowHeight).Within(0.01f));
        }

        [Test]
        public void Given_AVirtualListWhoseRowsMeasureThemselves_When_ThePanelLaysTheListOut_Then_ARowsCallbackRefReadsItsHeight()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, MeasuredRowList());

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(ReadRow(s_rowHeightsReadInRef, "a"), Is.EqualTo(RowHeight).Within(0.01f));
        }

        [Test]
        public void Given_AVirtualListRowWithAComponentInsideIt_When_ThePanelLaysTheListOut_Then_TheInnerLayoutEffectFindsItsRowInThePanel()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.VirtualList(new List<string> { "a" }, item => item,
                itemHeight: 40f, renderer: item => V.Component(RowWithInnerRender, item, key: item),
                name: "vlist", className: "h-[200px]"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_rowsFoundInLayoutEffect.Contains("inner-a"), Is.True);
        }

        [Test]
        public void Given_AnAnchorTheLastFrameLaidOut_When_AnUpdateMovesIt_Then_TheLayoutEffectReadsItsNewWorldPosition()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(MovingAnchorRender, key: "moving"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_setAnchorOffset.Invoke(AnchorOffset);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_anchorWorldOffsetInLayoutEffect, Is.EqualTo(AnchorOffset).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base's layout effect inside a ScrollView reads its scroller unsettled.
        // A ScrollView shows its scrollers from its own GeometryChangedEvents, which the panel's layout pass sends
        // after the commit, so the commit's layout computation leaves them as they were and that pass settles them.
        [Test]
        public void Given_ALayoutEffectInsideAScrollViewItsContentOverflows_When_TheTreeMountsAndThePanelUpdates_Then_TheEffectReadsTheScrollerHiddenAndThePassShowsIt()
        {
            // Arrange
            var scrollView = new ScrollView();
            scrollView.style.width = 200f;
            scrollView.style.height = 100f;
            _window.rootVisualElement.Add(scrollView);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_scrollView = scrollView;

            // Act
            _mounted = V.Mount(scrollView.contentContainer, V.Component(OverflowingContentRender, key: "overflow"));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That($"{s_scrollerDisplayInLayoutEffect} | {scrollView.verticalScroller.resolvedStyle.display}",
                Is.EqualTo($"{DisplayStyle.None} | {DisplayStyle.Flex}"));
        }

        // GREEN_ON_BASE(characterization): the base runs a GeometryChangedEvent callback that mounts a tree once.
        // Here the mount computes the panel's layout from inside the callback; running the layout updater's
        // `Update` there instead of `_calculate.Invoke(node, s_unbounded)` in `FiberLayoutReflow` dispatches the
        // host's next GeometryChangedEvent while this one is still running, which reaches the callback again.
        [Test]
        public void Given_AGeometryChangedCallbackThatUnregistersItselfAndMountsATree_When_ThePanelLaysItsHostOut_Then_TheCallbackRunsOnce()
        {
            // Arrange
            var runs = 0;
            MountOnFirstGeometryChange(V.Component(MeasuredBoxRender, key: "box"), () => runs++);

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(runs, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base sends an element its GeometryChangedEvent on the panel's update.
        // Here the mount's layout effect has computed the element's box already, and only the dirty flag the
        // computation leaves sends the event; dropping `_dirty.SetValue(node, true)` from `FiberLayoutReflow` leaves
        // the panel's layout updater nothing to visit.
        [Test]
        public void Given_AnElementALayoutEffectLaidOut_When_ThePanelUpdates_Then_TheElementReceivesItsGeometryChangedEvent()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(GeometryWatchingRender, key: "watched"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_geometryEventsReceived, Is.EqualTo(1));
        }

        [Test]
        public void Given_ATreeMountedFromAGeometryChangedCallback_When_ThePanelLaysItOut_Then_ItsLayoutEffectReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange
            MountMeasuredBoxOnFirstGeometryChange();

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_ATreeMountedFromAGeometryChangedCallback_When_ThePanelValidatesItsLayout_Then_ItsLayoutEffectReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange
            MountMeasuredBoxOnFirstGeometryChange();

            // Act
            ValidateLayout(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_ATreeMountedFromAScrollViewContentsHierarchyGeometryEvent_When_ThePanelLaysItOut_Then_ThePassCompletesWithTheLayoutEffectReadingItsBox()
        {
            // Arrange — the content keeps its own box while a child of it moves, so the layout updater reports the
            // content's GeometryChangedEvent from its list of hierarchy changes, after the pass's other events.
            var scrollView = new ScrollView();
            scrollView.style.width = 200f;
            scrollView.style.height = 200f;
            var content = scrollView.contentContainer;
            content.style.width = 100f;
            content.style.height = 100f;
            var mover = new VisualElement();
            mover.style.position = Position.Absolute;
            mover.style.width = 10f;
            mover.style.height = 10f;
            content.Add(mover);
            var host = new VisualElement();
            _window.rootVisualElement.Add(scrollView);
            _window.rootVisualElement.Add(host);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            EventCallback<GeometryChangedEvent> onGeometry = null;
            onGeometry = _ =>
            {
                content.UnregisterCallback(onGeometry);
                _mountedFromLayoutPass.Add(V.Mount(host, V.Component(MeasuredBoxRender, key: "box")));
            };
            content.RegisterCallback(onGeometry);
            mover.style.left = 20f;

            // Act
            Exception thrown = null;
            try
            {
                ForcePanelUpdate(_window.rootVisualElement.panel);
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            // Assert
            Assert.That($"{thrown?.GetType().Name ?? "none"} | {s_widthReadInLayoutEffect}", Is.EqualTo($"none | {MountWidth}"));
        }

        [Test]
        public void Given_ALayoutEffectThatStoresAMeasuredHeightInStateInATreeMountedFromAGeometryChangedCallback_When_ThePanelLaysItOut_Then_TheRenderCarriesTheHeight()
        {
            // Arrange
            MountOnFirstGeometryChange(V.Component(MeasureThenAdjustRender, key: "adjust"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_offsetRendered, Is.EqualTo(AnchorHeight).Within(0.01f));
        }

        [Test]
        public void Given_ACallbackRefThatRevealsAMeasuringElementByAClick_When_ItsTreeMountsFromAGeometryChangedCallback_Then_TheRevealedRefReadsItsWidth()
        {
            // Arrange
            MountOnFirstGeometryChange(V.Component(RevealOnRefRender, key: "reveal"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_widthReadInRef, Is.EqualTo(MountWidth).Within(0.01f));
        }

        // The host is laid out for the first time by the next panel update, whose GeometryChangedEvent mounts the tree
        // from inside that update's layout pass.
        private void MountMeasuredBoxOnFirstGeometryChange()
            => MountOnFirstGeometryChange(V.Component(MeasuredBoxRender, key: "box"));

        private void MountOnFirstGeometryChange(VNode tree, Action onRun = null)
        {
            var host = new VisualElement { name = "geometry-host" };
            EventCallback<GeometryChangedEvent> onGeometry = null;
            onGeometry = _ =>
            {
                onRun?.Invoke();
                host.UnregisterCallback(onGeometry);
                _mountedFromLayoutPass.Add(V.Mount(host, tree));
            };
            host.RegisterCallback(onGeometry);
            _window.rootVisualElement.Add(host);
        }

        // The panel's layout validation, which runs the style and layout updaters under a guard of its own, without
        // the repaint ForcePanelUpdate starts with.
        private static void ValidateLayout(IPanel panel)
        {
            for (var type = panel.GetType(); type != null; type = type.BaseType)
            {
                var method = type.GetMethod("ValidateLayout",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (method == null) continue;
                method.Invoke(panel, null);
                return;
            }
            throw new MissingMethodException(panel.GetType().FullName, "ValidateLayout");
        }

        private static float ReadRow(Dictionary<string, float> heights, string label)
            => heights.TryGetValue(label, out var height) ? height : float.NaN;

        private static VNode MeasuredRowList()
            => V.VirtualList(new List<string> { "a", "b", "c" }, item => item, itemHeight: 40f,
                renderer: item => V.Component(MeasuredRowRender, item, key: item),
                name: "vlist", className: "h-[200px]");

        private static VisualElement s_root;

        #region MeasuredBox component (a layout effect reads its element's width)

        private static StateUpdater<float> s_setWidth;
        private static float s_widthReadInLayoutEffect;
        private static float s_resolvedWidthReadInLayoutEffect;
        private static float s_worldWidthReadInLayoutEffect;

        [Component]
        private static VNode MeasuredBoxRender()
        {
            var (width, setWidth) = Hooks.UseState(MountWidth);
            s_setWidth = setWidth;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                var box = s_root.Q<VisualElement>("measured-box");
                s_widthReadInLayoutEffect = box.layout.width;
                s_resolvedWidthReadInLayoutEffect = box.resolvedStyle.width;
                s_worldWidthReadInLayoutEffect = box.worldBound.width;
                return null;
            }), new object[] { width });
            return V.Div(className: $"w-[{width}px] h-[20px]", name: "measured-box");
        }

        #endregion

        private const float AnchorOffset = 40f;
        private static StateUpdater<float> s_setAnchorOffset;
        private static float s_anchorWorldOffsetInLayoutEffect;

        // An existing anchor whose left margin an update moves; its layout effect reads where the panel draws it.
        [Component]
        private static VNode MovingAnchorRender()
        {
            var (offset, setOffset) = Hooks.UseState(0f);
            s_setAnchorOffset = setOffset;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                var anchor = s_root.Q<VisualElement>("moving-anchor");
                s_anchorWorldOffsetInLayoutEffect = anchor.worldBound.x - anchor.parent.worldBound.x;
                return null;
            }), new object[] { offset });
            return V.Div(className: $"ml-[{offset}px] w-[20px] h-[20px]", name: "moving-anchor");
        }

        private static ScrollView s_scrollView;
        private static DisplayStyle? s_scrollerDisplayInLayoutEffect;

        [Component]
        private static VNode OverflowingContentRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_scrollerDisplayInLayoutEffect = s_scrollView.verticalScroller.resolvedStyle.display;
                return null;
            }), Array.Empty<object>());
            return V.Div(className: "h-[300px]");
        }

        [Component]
        private static VNode RowWithInnerRender(string label)
            => V.Div(className: $"h-[{RowHeight}px]", name: $"row-{label}",
                children: new VNode[] { V.Component(InnerRowRender, label, key: "inner") });

        [Component]
        private static VNode InnerRowRender(string label)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (s_root.Q<VisualElement>($"row-{label}") != null) s_rowsFoundInLayoutEffect.Add($"inner-{label}");
                return null;
            }), new object[] { label });
            return V.Div();
        }

        private static int s_geometryEventsReceived;

        [Component]
        private static VNode GeometryWatchingRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_root.Q<VisualElement>("watched-box")
                    .RegisterCallback<GeometryChangedEvent>(_ => s_geometryEventsReceived++);
                return null;
            }), Array.Empty<object>());
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "watched-box");
        }

        [Component]
        private static VNode InsertionWidenedWithRefRender()
        {
            Hooks.UseInsertionEffect((Func<Action>)(() =>
            {
                s_root.Q<VisualElement>("widened-with-ref").style.width = UpdatedWidth;
                return null;
            }), Array.Empty<object>());
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "widened-with-ref", refCallback: s_measuringRef);
        }

        private static int s_hiddenInsertionRuns;
        private static VelvetTaskCompletionSource<string> s_neverResolves;

        private static StateUpdater<bool> s_setSuspended;
        private static StateUpdater<int> s_setHiddenTick;
        private static int s_hiddenRefTick;

        [Component]
        private static VNode SuspendingHostRender()
        {
            var (suspended, setSuspended) = Hooks.UseState(false);
            s_setSuspended = setSuspended;
            return V.Suspense(V.Label(text: "fallback"), new VNode[]
            {
                V.Component(HiddenInsertionRender, key: "hidden"),
                suspended ? V.Component(NeverResolvingRender, key: "suspends") : null,
            });
        }

        // Its ref is a new delegate on every render, so each render queues the ref's setup again.
        [Component]
        private static VNode HiddenInsertionRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setHiddenTick = setTick;
            Hooks.UseInsertionEffect((Func<Action>)(() =>
            {
                s_hiddenInsertionRuns++;
                return null;
            }), new object[] { tick });
            return V.Div(name: "hidden-with-ref", refCallback: _ =>
            {
                s_hiddenRefTick = tick;
                return null;
            });
        }

        private static StateUpdater<float> s_setSiblingHeight;

        [Component]
        private static VNode GrowingSiblingRender()
        {
            var (height, setHeight) = Hooks.UseState(0f);
            s_setSiblingHeight = setHeight;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                var anchor = s_root.Q<VisualElement>("still-anchor");
                s_anchorWorldOffsetInLayoutEffect = anchor.worldBound.y - anchor.parent.worldBound.y;
                return null;
            }), new object[] { height });
            return V.Div(children: new VNode[]
            {
                V.Div(className: $"h-[{height}px] w-[20px]"),
                V.Div(className: "w-[20px] h-[20px]", name: "still-anchor"),
            });
        }

        [Component]
        private static VNode SheetSizedBoxRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthReadInLayoutEffect = s_root.Q<VisualElement>("sheet-box").layout.width;
                return null;
            }), Array.Empty<object>());
            return V.Div(className: "w-8 h-[20px]", name: "sheet-box");
        }

        private static VisualElement s_otherRoot;

        // The component inside the portal is mounted on the other panel and reads its own element there.
        [Component]
        private static VNode PortalIntoOtherPanelRender()
            => V.Portal(s_otherRoot, children: new VNode[]
            {
                V.Div(children: new VNode[] { V.Component(OtherPanelBoxRender, key: "other") }),
            });

        [Component]
        private static VNode OtherPanelBoxRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthReadInLayoutEffect = s_otherRoot.Q<VisualElement>("other-box").layout.width;
                return null;
            }), Array.Empty<object>());
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "other-box");
        }

        // The component inside the portal is mounted on the other panel and reads an element of the mounted panel.
        [Component]
        private static VNode PortalReadingMountedPanelRender()
            => V.Div(children: new VNode[]
            {
                V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "mounted-box"),
                V.Portal(s_otherRoot, children: new VNode[]
                {
                    V.Div(children: new VNode[] { V.Component(MountedPanelReaderRender, key: "reader") }),
                }),
            });

        [Component]
        private static VNode MountedPanelReaderRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthReadInLayoutEffect = s_root.Q<VisualElement>("mounted-box").layout.width;
                return null;
            }), Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode NeverResolvingRender() => V.Label(text: Hooks.Use(() => s_neverResolves.Task));

        #region MeasureThenAdjust component (a layout effect stores a measured height in state)

        private static float s_offsetRendered;

        [Component]
        private static VNode MeasureThenAdjustRender()
        {
            var (offset, setOffset) = Hooks.UseState(-1f);
            s_offsetRendered = offset;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                setOffset.Invoke(s_root.Q<VisualElement>("anchor").layout.height);
                return null;
            }), new object[] { offset });
            return V.Div(className: $"h-[{AnchorHeight}px]", name: "anchor");
        }

        #endregion

        #region MeasuringHandle component (an imperative handle reads its element's width)

        private sealed class MeasuredWidth
        {
            public MeasuredWidth(float width) => Width = width;

            public float Width { get; }
        }

        private static readonly Ref<MeasuredWidth> s_handle = new();

        [Component]
        private static VNode MeasuringHandleRender()
        {
            Hooks.UseImperativeHandle(s_handle,
                () => new MeasuredWidth(s_root.Q<VisualElement>("handle-box").layout.width));
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "handle-box");
        }

        #endregion

        #region MeasuringRef callback (a callback ref reads its element's width)

        private static float s_widthReadInRef;

        private static readonly Func<VisualElement, Action> s_measuringRef = element =>
        {
            s_widthReadInRef = element.layout.width;
            return null;
        };

        #endregion

        #region InsertionWidened component (an insertion effect widens the element a layout effect reads)

        [Component]
        private static VNode InsertionWidenedRender()
        {
            Hooks.UseInsertionEffect((Func<Action>)(() =>
            {
                s_root.Q<VisualElement>("widened").style.width = UpdatedWidth;
                return null;
            }), Array.Empty<object>());
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthReadInLayoutEffect = s_root.Q<VisualElement>("widened").layout.width;
                return null;
            }), Array.Empty<object>());
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "widened");
        }

        #endregion

        #region StableBox components (a layout effect or a handle whose deps hold across a widening update)

        private static readonly Ref<VisualElement> s_stableBox = new();
        private static StateUpdater<float> s_setStableWidth;

        [Component]
        private static VNode StableLayoutEffectBoxRender()
        {
            var (width, setWidth) = Hooks.UseState(MountWidth);
            s_setStableWidth = setWidth;
            Hooks.UseLayoutEffect((Func<Action>)(() => null), Array.Empty<object>());
            return V.Div(className: $"w-[{width}px] h-[20px]", refCallback: s_stableBox.SetElement);
        }

        [Component]
        private static VNode StableHandleBoxRender()
        {
            var (width, setWidth) = Hooks.UseState(MountWidth);
            s_setStableWidth = setWidth;
            Hooks.UseImperativeHandle(s_handle, () => new MeasuredWidth(0f), Array.Empty<object>());
            return V.Div(className: $"w-[{width}px] h-[20px]", refCallback: s_stableBox.SetElement);
        }

        #endregion

        #region RevealOnRef component (a callback ref clicks a button that reveals a measuring element)

        [Component]
        private static VNode RevealOnRefRender()
        {
            var (revealed, setRevealed) = Hooks.UseState(false);
            var clickOnAttach = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                element.Q<Button>().SimulateClick();
                return null;
            }, Array.Empty<object>());
            return V.Div(refCallback: clickOnAttach, children: new VNode[]
            {
                V.Button(text: "reveal", onClick: () => setRevealed.Invoke(true)),
                revealed ? V.Div(className: $"w-[{MountWidth}px] h-[20px]", refCallback: s_measuringRef) : null,
            });
        }

        #endregion

        #region MeasuredRow component (a virtual-list row whose layout effect and callback ref read its height)

        private static readonly HashSet<string> s_rowsFoundInLayoutEffect = new();
        private static readonly Dictionary<string, float> s_rowHeightsReadInLayoutEffect = new();
        private static readonly Dictionary<string, float> s_rowHeightsReadInRef = new();

        [Component]
        private static VNode MeasuredRowRender(string label)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                var row = s_root.Q<VisualElement>($"row-{label}");
                if (row == null) return null;
                s_rowsFoundInLayoutEffect.Add(label);
                s_rowHeightsReadInLayoutEffect[label] = row.layout.height;
                return null;
            }), new object[] { label });
            var measure = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                s_rowHeightsReadInRef[label] = element.layout.height;
                return null;
            }, label);
            return V.Div(className: $"h-[{RowHeight}px]", name: $"row-{label}", refCallback: measure);
        }

        #endregion
    }
}
