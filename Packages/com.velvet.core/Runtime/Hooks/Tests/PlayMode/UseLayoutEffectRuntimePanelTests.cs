using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the laid-out read of a commit's layout phase on runtime panels, which a <see cref="UIDocument"/>
    /// drives from <see cref="PanelSettings"/> rather than from an editor window.
    /// <list type="bullet">
    /// <item>A layout effect reads the box the mount gave its element.</item>
    /// <item>A layout effect reads, on an update, the box the update gave an element its tree renders into a layer
    /// panel, through a ref that update leaves in place, and the same for a world-space panel.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each case commits synchronously between frames, so no frame of the panel's own lays the commit out first.
    /// </remarks>
    internal sealed class UseLayoutEffectRuntimePanelTests
    {
        private const float MountWidth = 120f;
        private const float UpdatedWidth = 200f;

        private GameObject _documentObject;
        private PanelSettings _settings;
        private MountedTree _mounted;

        private static VisualElement s_root;
        private static float s_widthRead;
        private static StateUpdater<float> s_setWidth;
        private static readonly Ref<VisualElement> s_layerBox = new();

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_widthRead = float.NaN;
            s_setWidth = default;
            s_layerBox.Set(null);
            _documentObject = new GameObject("UseLayoutEffectRuntimePanel");
            var document = _documentObject.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            document.panelSettings = _settings;
            yield return null;
            s_root = document.rootVisualElement;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_documentObject != null) UnityEngine.Object.Destroy(_documentObject);
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            yield return null;
        }

        private VisualElement Root => _documentObject.GetComponent<UIDocument>().rootVisualElement;

        [UnityTest]
        public IEnumerator Given_ALayoutEffectReadingItsElementsWidthOnARuntimePanel_When_TheTreeMounts_Then_ItReadsTheWidthTheMountGaveTheElement()
        {
            // Arrange
            var root = Root;

            // Act
            _mounted = V.Mount(root, V.Component(MeasuredBoxRender, key: "box"));

            // Assert
            Assert.That(s_widthRead, Is.EqualTo(MountWidth).Within(0.01f));
            yield break;
        }

        [UnityTest]
        public IEnumerator Given_ALayerPortalElementTheLastFrameLaidOut_When_AnUpdateWidensIt_Then_TheDeclaringComponentsLayoutEffectReadsTheNewWidth()
        {
            // Arrange — the layer host is created by the drain after the mount, and its frames lay the element out.
            _mounted = V.Mount(Root, V.Component(LayerPortalBoxRender, key: "layer"));
            yield return null;
            yield return null;
            s_setWidth.Invoke(UpdatedWidth);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_widthRead, Is.EqualTo(UpdatedWidth).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator Given_AWorldSpaceElementTheLastFrameLaidOut_When_AnUpdateWidensIt_Then_TheDeclaringComponentsLayoutEffectReadsTheNewWidth()
        {
            // Arrange — the world-space host is created by the drain after the mount, and its frames lay the element
            // out.
            _mounted = V.Mount(Root, V.Component(WorldSpaceBoxRender, key: "world"));
            yield return null;
            yield return null;
            s_setWidth.Invoke(UpdatedWidth);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_widthRead, Is.EqualTo(UpdatedWidth).Within(0.01f));
        }

        [Component]
        private static VNode WorldSpaceBoxRender()
        {
            var (width, setWidth) = Hooks.UseState(MountWidth);
            s_setWidth = setWidth;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthRead = s_layerBox.Current?.layout.width ?? float.NaN;
                return null;
            }), new object[] { width });
            return V.Div(children: new VNode[]
            {
                V.WorldSpace(Vector3.zero, children: new VNode[]
                {
                    V.Div(className: $"w-[{width}px] h-[20px]", refCallback: s_layerBox.SetElement),
                }),
            });
        }

        [Component]
        private static VNode MeasuredBoxRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthRead = s_root.Q<VisualElement>("measured-box").layout.width;
                return null;
            }), Array.Empty<object>());
            return V.Div(className: $"w-[{MountWidth}px] h-[20px]", name: "measured-box");
        }

        [Component]
        private static VNode LayerPortalBoxRender()
        {
            var (width, setWidth) = Hooks.UseState(MountWidth);
            s_setWidth = setWidth;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_widthRead = s_layerBox.Current?.layout.width ?? float.NaN;
                return null;
            }), new object[] { width });
            return V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[]
                {
                    V.Div(className: $"w-[{width}px] h-[20px]", refCallback: s_layerBox.SetElement),
                }),
            });
        }
    }
}
