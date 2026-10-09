using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
    /// <item>What an insertion effect of the same commit writes is in the box a layout effect reads.</item>
    /// <item>An update commit whose layout effects and handles all keep their deps lays out nothing, as a browser
    /// forces a reflow only where something reads the layout.</item>
    /// <item>A commit made inside the panel's own layout pass — a virtual list rendering its rows, or a tree mounted
    /// from a <see cref="GeometryChangedEvent"/> callback — completes that pass, and its layout effects and callback
    /// refs read the box the pass gives their elements.</item>
    /// <item>Such a commit's layout effects that store a measurement in state re-render before the pass ends, a
    /// boundary catching one of them reports once, the report of an error boundary among its fibers waits until
    /// that boundary's layout effects run, and a callback ref attached in it that commits synchronously has the elements that commit
    /// adds laid out before their own refs read them.</item>
    /// <item>Work handed to the panel's layout pass runs once, after the panel lays out, from the panel's scheduler
    /// where that pass does not reach it, and one piece of work that throws leaves the rest running.</item>
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
            s_setWidth = null;
            s_widthReadInLayoutEffect = float.NaN;
            s_resolvedWidthReadInLayoutEffect = float.NaN;
            s_worldWidthReadInLayoutEffect = float.NaN;
            s_offsetRendered = float.NaN;
            s_handle.Set(null);
            s_stableBox.Set(null);
            s_setStableWidth = null;
            s_widthReadInRef = float.NaN;
            s_rowHeightsReadInLayoutEffect.Clear();
            s_rowHeightsReadInRef.Clear();
        }

        public override void TearDown()
        {
            foreach (var mounted in _mountedFromLayoutPass) mounted.Dispose();
            _mountedFromLayoutPass.Clear();
            base.TearDown();
        }

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

        // GREEN_ON_BASE(characterization): the base lays out a virtual list whose rows run layout effects.
        // A commit there lays out no panel at all, so it never starts the panel's layout pass again from inside it.
        // Here a commit lays its panel out before its layout effects read it, unless it runs inside that pass;
        // making `FiberLayoutReflow.IsInsideLayoutPass` return `false` is the perturbation this case is for.
        [Test]
        public void Given_AVirtualListWhoseRowsRunLayoutEffects_When_ThePanelLaysTheListOut_Then_TheLayoutPassCompletes()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, MeasuredRowList());

            // Act + Assert
            Assert.DoesNotThrow(() => ForcePanelUpdate(_window.rootVisualElement.panel));
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

        // GREEN_ON_BASE(characterization): the base mounts a tree from a GeometryChangedEvent without throwing.
        // A commit there lays out no panel at all, so it never starts the panel's layout pass again from inside it.
        // Here a commit lays its panel out before its layout effects read it, unless it runs inside that pass;
        // making `FiberLayoutReflow.IsInsideLayoutPass` return `false` is the perturbation this case is for.
        [Test]
        public void Given_ATreeMountedFromAGeometryChangedCallback_When_ThePanelLaysItOut_Then_TheLayoutPassCompletes()
        {
            // Arrange
            MountMeasuredBoxOnFirstGeometryChange();

            // Act + Assert
            Assert.DoesNotThrow(() => ForcePanelUpdate(_window.rootVisualElement.panel));
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
        public void Given_APanelThatAlreadyRanALayoutPassCommit_When_ATreeWithARefAndALayoutEffectMountsFromALaterOne_Then_ItsLayoutEffectReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange — the first mount leaves the panel holding a probe it has laid out, and the second hands the
            // pass both its callback refs and its layout effects in one dispatch.
            MountMeasuredBoxOnFirstGeometryChange();
            ForcePanelUpdate(_window.rootVisualElement.panel);
            s_widthReadInLayoutEffect = float.NaN;
            MountOnFirstGeometryChange(V.Component(MeasuredBoxWithRefRender, key: "box-with-ref"), null);

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base reports this boundary's catch once.
        // The tree mounts from a GeometryChangedEvent. There the boundary's layout effects run at the mount; here
        // they wait for the pass, and while they wait the report is not due — dropping
        // `!ctx.FibersAwaitingLayout.Contains(report.Boundary)` from `IsDue` runs the follow-up commits to their bound.
        [Test]
        public void Given_ABoundaryCatchingInATreeMountedFromAGeometryChangedCallback_When_ThePanelLaysItOut_Then_TheHandlerRunsOnce()
        {
            // Arrange
            var reports = 0;
            MountOnFirstGeometryChange(V.Component(BoundaryWithLayoutEffectRender, key: "boundary"),
                new MountOptions((_, _) => reports++));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(reports, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base reports this layout effect's throw once.
        // The tree mounts from a GeometryChangedEvent. Here that effect runs from the pass's next iteration
        // instead, and dropping the `CommitStrandedLayoutWork(ctx)` that ends `RunDeferredSetupPass` leaves the
        // report undelivered.
        [Test]
        public void Given_ABoundaryWhoseChildsLayoutEffectThrowsInATreeMountedFromAGeometryChangedCallback_When_ThePanelLaysItOut_Then_TheHandlerRunsOnce()
        {
            // Arrange
            var reports = 0;
            MountOnFirstGeometryChange(V.Component(BoundaryAroundThrowingLayoutEffectRender, key: "boundary"),
                new MountOptions((_, _) => reports++));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(reports, Is.EqualTo(1));
        }

        [Test]
        public void Given_ALayoutEffectThatStoresAMeasuredHeightInStateInATreeMountedFromAGeometryChangedCallback_When_ThePanelLaysItOut_Then_TheRenderCarriesTheHeight()
        {
            // Arrange
            MountOnFirstGeometryChange(V.Component(MeasureThenAdjustRender, key: "adjust"), null);

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_offsetRendered, Is.EqualTo(AnchorHeight).Within(0.01f));
        }

        [Test]
        public void Given_ACallbackRefThatRevealsAMeasuringElementByAClick_When_ItsTreeMountsFromAGeometryChangedCallback_Then_TheRevealedRefReadsItsWidth()
        {
            // Arrange
            MountOnFirstGeometryChange(V.Component(RevealOnRefRender, key: "reveal"), null);

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(s_widthReadInRef, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_WorkHandedToAPanelOutsideItsLayoutPass_When_ThePanelsSchedulerTicks_Then_TheWorkReadsTheBoxTheTickLaidOut()
        {
            // Arrange
            var panel = _window.rootVisualElement.panel;
            var box = UnlaidBox();
            var read = float.NaN;
            FiberLayoutReflow.RunAfterLayout(panel, () => read = box.layout.width);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);

            // Assert
            Assert.That(read, Is.EqualTo(MountWidth).Within(0.01f));
        }

        [Test]
        public void Given_WorkThePanelAlreadyRan_When_MoreWorkIsHandedToIt_Then_TheLaterWorkRunsAndTheFirstDoesNotRunAgain()
        {
            // Arrange
            var panel = _window.rootVisualElement.panel;
            var runs = 0;
            var laterRan = false;
            FiberLayoutReflow.RunAfterLayout(panel, () => runs++);
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);
            FiberLayoutReflow.RunAfterLayout(panel, () => laterRan = true);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);

            // Assert
            Assert.That($"{runs} | {laterRan}", Is.EqualTo("1 | True"));
        }

        [Test]
        public void Given_WorkThatThrowsAheadOfOtherWork_When_ThePanelRunsIt_Then_TheOtherWorkStillRuns()
        {
            // Arrange
            var panel = _window.rootVisualElement.panel;
            var ranAfter = false;
            FiberLayoutReflow.RunAfterLayout(panel, () => throw new InvalidOperationException("work boom"));
            FiberLayoutReflow.RunAfterLayout(panel, () => ranAfter = true);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("work boom"));

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);

            // Assert
            Assert.That(ranAfter, Is.True);
        }

        private VisualElement UnlaidBox()
        {
            var box = new VisualElement();
            box.style.width = MountWidth;
            box.style.height = 20f;
            _window.rootVisualElement.Add(box);
            return box;
        }

        // The host is laid out for the first time by the next panel update, whose GeometryChangedEvent mounts the tree
        // from inside that update's layout pass.
        private void MountMeasuredBoxOnFirstGeometryChange()
            => MountOnFirstGeometryChange(V.Component(MeasuredBoxRender, key: "box"), null);

        private void MountOnFirstGeometryChange(VNode tree, MountOptions options)
        {
            var host = new VisualElement { name = "geometry-host" };
            EventCallback<GeometryChangedEvent> onGeometry = null;
            onGeometry = _ =>
            {
                host.UnregisterCallback(onGeometry);
                _mountedFromLayoutPass.Add(options == null ? V.Mount(host, tree) : V.Mount(host, tree, options));
            };
            host.RegisterCallback(onGeometry);
            _window.rootVisualElement.Add(host);
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

        [Component]
        private static VNode MeasuredBoxWithRefRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthReadInLayoutEffect = s_root.Q<VisualElement>("measured-box-with-ref").layout.width;
                return null;
            }), Array.Empty<object>());
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "measured-box-with-ref",
                refCallback: s_measuringRef);
        }

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

        #region BoundaryWithLayoutEffect component (an error boundary with a layout effect catching its child)

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWithLayoutEffectRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            Hooks.UseLayoutEffect((Func<Action>)(() => null), Array.Empty<object>());
            return V.Component(ThrowingChildRender, key: "child");
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new InvalidOperationException("child boom");

        #endregion

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryAroundThrowingLayoutEffectRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(ThrowingLayoutEffectRender, key: "child");
        }

        [Component]
        private static VNode ThrowingLayoutEffectRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => throw new InvalidOperationException("effect boom")),
                Array.Empty<object>());
            return V.Label(text: "child");
        }

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

        private static readonly Dictionary<string, float> s_rowHeightsReadInLayoutEffect = new();
        private static readonly Dictionary<string, float> s_rowHeightsReadInRef = new();

        [Component]
        private static VNode MeasuredRowRender(string label)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_rowHeightsReadInLayoutEffect[label] = s_root.Q<VisualElement>($"row-{label}").layout.height;
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
