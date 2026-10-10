#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a <c>V.Anchored</c> element a Suspense keeps hidden stays hidden while a real runtime panel
    /// ticks the anchoring, whose own display writes would otherwise show it again. Set up as
    /// <see cref="AnchoredPlaybackTests"/> is.
    /// </summary>
    internal sealed class AnchoredSuspenseHiddenPlaybackTests
    {
        private static Transform s_target;
        private static Camera s_camera;
        private static StateUpdater<int> s_setOwn;

        private GameObject _panelGo;
        private GameObject _cameraGo;
        private GameObject _targetGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [Component]
        private static VNode ReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : new VelvetTaskCompletionSource<int>().Task, own);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode HiddenAnchoredHost()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Anchored(s_target, camera: s_camera, name: "anchored", className: "w-[10px] h-[10px]"),
                    V.Component(ReaderRender, key: "reader"),
                });

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_setOwn = default;
            _panelGo = new GameObject("AnchoredSuspenseHiddenPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            VelvetStyleUtilities.AttachTo(doc.rootVisualElement);

            _cameraGo = new GameObject("AnchoredSuspenseHiddenCamera");
            s_camera = _cameraGo.AddComponent<Camera>();
            s_camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            s_camera.fieldOfView = 60f;
            _targetGo = new GameObject("AnchoredSuspenseHiddenTarget");
            _targetGo.transform.position = Vector3.zero;
            s_target = _targetGo.transform;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            s_target = null;
            s_camera = null;
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
            if (_targetGo != null) Object.Destroy(_targetGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_AnAnchoredElementInARevealedPrimary_When_TheBoundarySuspendsAgainAndThePanelTicks_Then_TheElementStaysHidden()
        {
            // Arrange — the anchoring has ticked on the shown element first
            var root = _panelGo.GetComponent<UIDocument>().rootVisualElement;
            _mounted = V.Mount(root, V.Component(HiddenAnchoredHost, key: "host"));
            yield return null;
            yield return null;
            var element = root.Q<VisualElement>("anchored");

            // Act
            s_setOwn.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            yield return null;
            yield return null;

            // Assert
            Assert.That(element?.style.display.value, Is.EqualTo(DisplayStyle.None),
                "React's display: none on a hidden child outranks the child's own display");
        }
    }
}
#endif
