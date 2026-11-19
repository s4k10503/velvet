using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the engine fact the backdrop filters' absence from a world-space panel rests on
    /// (styling-filters.md): UI Toolkit refuses an immediate-mode element in a panel cameras draw, with an error,
    /// and draws nothing for it, while a backdrop layer is one.
    /// </summary>
    internal sealed class WorldSpaceImmediateModeTests
    {
        private sealed class Probe : ImmediateModeElement
        {
            public int Repaints;

            protected override void ImmediateRepaint() => Repaints++;
        }

        private GameObject _docGo;
        private GameObject _cameraGo;
        private PanelSettings _settings;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_docGo != null) Object.Destroy(_docGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        // GREEN_ON_BASE(characterization): pins an engine refusal the base already met, which the guide now cites.
        [UnityTest]
        public IEnumerator Given_AnImmediateModeElementInAWorldSpacePanel_When_CamerasDraw_Then_UIToolkitRefusesItAndNeverRunsIt()
        {
            // Arrange — a camera at z = -10 looking down +z at a world-space document at the origin.
            _cameraGo = new GameObject(nameof(WorldSpaceImmediateModeTests) + "Camera");
            _cameraGo.AddComponent<Camera>().transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            _docGo = new GameObject(nameof(WorldSpaceImmediateModeTests) + "Document");
            var document = _docGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.renderMode = PanelRenderMode.WorldSpace;
            document.panelSettings = _settings;
            yield return null;
            var probe = new Probe { style = { width = 50f, height = 50f } };
            var refused = false;
            void Watch(string message, string stack, LogType type)
                => refused |= type == LogType.Error && message.Contains("cannot be used in a panel drawn by cameras");
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Watch;

            // Act
            document.rootVisualElement.Add(probe);
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.5);
            Application.logMessageReceived -= Watch;

            // Assert
            Assert.That((refused, probe.Repaints), Is.EqualTo((true, 0)));
        }
    }
}
