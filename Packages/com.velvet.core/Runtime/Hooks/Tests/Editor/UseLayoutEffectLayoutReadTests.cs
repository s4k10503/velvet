using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the layout phase of a commit reads on a real panel: as a read inside React's
    /// <c>useLayoutEffect</c> forces the reflow the commit owes, the committed tree is laid out by the time the
    /// phase reads it.
    /// <list type="bullet">
    /// <item>A layout effect reads the box its mount commit gave an element, and the box an update commit gave it
    /// rather than the one the previous frame laid out.</item>
    /// <item>A state update the effect makes from that reading has re-rendered by the time the mount returns.</item>
    /// <item>An imperative handle built in the same phase reads the laid-out box too.</item>
    /// <item>What an insertion effect of the same commit writes is in the box a layout effect reads.</item>
    /// <item>An update commit whose layout effects and handles all keep their deps lays out nothing, as a browser
    /// forces a reflow only where something reads the layout.</item>
    /// <item>A virtual list whose rows run layout effects still lays out: it renders rows from inside the panel's
    /// own layout pass, which the commit there does not start again; a tree mounted after the list rendered them
    /// still reads its box laid out.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Every measured box is sized with an arbitrary value, which resolves to an inline style and so needs no
    /// stylesheet attached. An update case lays the mount out first, so that the box the last frame laid out is
    /// there for a stale read to return.
    /// </remarks>
    [TestFixture]
    internal sealed class UseLayoutEffectLayoutReadTests : PanelTestBase
    {
        private const float MountWidth = 120f;
        private const float UpdatedWidth = 200f;
        private const float AnchorHeight = 40f;

        public override void SetUp()
        {
            base.SetUp();
            s_box.Set(null);
            s_setWidth = null;
            s_widthReadInLayoutEffect = float.NaN;
            s_resolvedWidthReadInLayoutEffect = float.NaN;
            s_worldWidthReadInLayoutEffect = float.NaN;
            s_anchor.Set(null);
            s_offsetRendered = float.NaN;
            s_handleBox.Set(null);
            s_handle.Set(null);
            s_insertionRoot = _window.rootVisualElement;
            s_stableBox.Set(null);
            s_setStableWidth = null;
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
        // A commit there lays out no panel at all, so nothing it does can start the panel's layout pass again
        // from inside it. Here a commit lays its panel out before its layout effects read it, and a range
        // rendered inside the layout pass is the place that must not; dropping the
        // `FiberLayoutReflow.EnterSuppressed()` around `RenderRange` is the perturbation this case is for.
        [Test]
        public void Given_AVirtualListWhoseRowsRunLayoutEffects_When_ThePanelLaysTheListOut_Then_TheLayoutPassCompletes()
        {
            // Arrange
            var items = new List<string> { "a", "b", "c" };
            _mounted = V.Mount(_window.rootVisualElement,
                V.VirtualList(items, item => item, itemHeight: 40f,
                    renderer: item => V.Component(LayoutEffectRowRender, item, key: item),
                    name: "vlist", className: "h-[200px]"));

            // Act + Assert
            Assert.DoesNotThrow(() => ForcePanelUpdate(_window.rootVisualElement.panel));
        }

        [Test]
        public void Given_AVirtualListThatRenderedItsRows_When_ALaterTreeMounts_Then_ItsLayoutEffectReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var items = new List<string> { "a", "b", "c" };
            _mounted = V.Mount(root,
                V.VirtualList(items, item => item, itemHeight: 40f,
                    renderer: item => V.Div(className: "h-[30px]", name: $"row-{item}"),
                    name: "vlist", className: "h-[200px]"));
            ForcePanelUpdate(root.panel);
            var laterHost = new VisualElement();
            root.Add(laterHost);

            // Act
            using var later = V.Mount(laterHost, V.Component(MeasuredBoxRender, key: "box"));

            // Assert
            Assert.That(s_widthReadInLayoutEffect, Is.EqualTo(MountWidth).Within(0.01f));
        }

        #region MeasuredBox component (a layout effect reads its element's width)

        private static readonly Ref<VisualElement> s_box = new();
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
                s_widthReadInLayoutEffect = s_box.Current.layout.width;
                s_resolvedWidthReadInLayoutEffect = s_box.Current.resolvedStyle.width;
                s_worldWidthReadInLayoutEffect = s_box.Current.worldBound.width;
                return null;
            }), new object[] { width });
            return V.Div(className: $"w-[{width}px] h-[20px]", refCallback: s_box.SetElement);
        }

        #endregion

        #region MeasureThenAdjust component (a layout effect stores a measured height in state)

        private static readonly Ref<VisualElement> s_anchor = new();
        private static float s_offsetRendered;

        [Component]
        private static VNode MeasureThenAdjustRender()
        {
            var (offset, setOffset) = Hooks.UseState(-1f);
            s_offsetRendered = offset;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                setOffset.Invoke(s_anchor.Current.layout.height);
                return null;
            }), new object[] { offset });
            return V.Div(className: $"h-[{AnchorHeight}px]", refCallback: s_anchor.SetElement);
        }

        #endregion

        #region MeasuringHandle component (an imperative handle reads its element's width)

        private sealed class MeasuredWidth
        {
            public MeasuredWidth(float width) => Width = width;

            public float Width { get; }
        }

        private static readonly Ref<VisualElement> s_handleBox = new();
        private static readonly Ref<MeasuredWidth> s_handle = new();

        [Component]
        private static VNode MeasuringHandleRender()
        {
            Hooks.UseImperativeHandle(s_handle, () => new MeasuredWidth(s_handleBox.Current.layout.width));
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", refCallback: s_handleBox.SetElement);
        }

        #endregion

        #region InsertionWidened component (an insertion effect widens the element a layout effect reads)

        private static VisualElement s_insertionRoot;

        [Component]
        private static VNode InsertionWidenedRender()
        {
            Hooks.UseInsertionEffect((Func<Action>)(() =>
            {
                s_insertionRoot.Q<VisualElement>("widened").style.width = UpdatedWidth;
                return null;
            }), Array.Empty<object>());
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthReadInLayoutEffect = s_insertionRoot.Q<VisualElement>("widened").layout.width;
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

        #region LayoutEffectRow component (a virtual-list row that runs a layout effect)

        [Component]
        private static VNode LayoutEffectRowRender(string label)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => null), new object[] { label });
            return V.Div(className: "h-[30px]", name: $"row-{label}");
        }

        #endregion
    }
}
