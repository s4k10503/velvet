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
    /// What a negative <c>tabIndex</c> does on a runtime panel, which the focus guide states: the element
    /// stays focusable by <c>Focus()</c> and by a pointer press, and drops out of both the Tab ring and
    /// 2D navigation.
    /// </summary>
    internal sealed class NegativeTabIndexEngineContractTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private Button _above;
        private Button _skipped;
        private Button _belowSkipped;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("NegativeTabIndexPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;

            _above = SizedButton("above");
            _skipped = SizedButton("skipped");
            _skipped.tabIndex = -1;
            _belowSkipped = SizedButton("belowSkipped");
            doc.rootVisualElement.Add(_above);
            doc.rootVisualElement.Add(_skipped);
            doc.rootVisualElement.Add(_belowSkipped);
            yield return null;

            _above.Focus();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        private static Button SizedButton(string name)
        {
            var button = new Button { name = name, text = name };
            button.style.width = 100f;
            button.style.height = 40f;
            return button;
        }

        private void SendMove(NavigationMoveEvent.Direction direction)
        {
            using var move = NavigationMoveEvent.GetPooled(direction);
            move.target = _above;
            _above.SendEvent(move);
        }

        // GREEN_ON_BASE(characterization): an engine fact the focus guide states, which Velvet does not change.
        [UnityTest]
        public IEnumerator Given_ANegativeTabIndex_When_TheElementIsFocusedProgrammatically_Then_ItTakesFocus()
        {
            // Act
            _skipped.Focus();
            yield return null;

            // Assert
            Assert.That(_skipped.panel.focusController.focusedElement, Is.EqualTo(_skipped));
        }

        // GREEN_ON_BASE(characterization): an engine fact the focus guide states, which Velvet does not change.
        [UnityTest]
        public IEnumerator Given_ANegativeTabIndex_When_APointerPressLandsOnTheElement_Then_ItTakesFocus()
        {
            // Act
            _skipped.SendPointerDownEvent(_skipped.worldBound.center);
            yield return null;

            // Assert
            Assert.That(_skipped.panel.focusController.focusedElement, Is.EqualTo(_skipped));
        }

        // GREEN_ON_BASE(characterization): an engine fact the focus guide states, which Velvet does not change.
        [UnityTest]
        public IEnumerator Given_ANegativeTabIndex_When_TabMovesPastThePrecedingElement_Then_TheRingSkipsIt()
        {
            // Act
            SendMove(NavigationMoveEvent.Direction.Next);
            yield return null;

            // Assert
            Assert.That(_above.panel.focusController.focusedElement, Is.EqualTo(_belowSkipped));
        }

        // GREEN_ON_BASE(characterization): an engine fact the focus guide states, which Velvet does not change.
        [UnityTest]
        public IEnumerator Given_ANegativeTabIndex_When_ASpatialMoveHeadsTowardIt_Then_NavigationSkipsIt()
        {
            // Act
            SendMove(NavigationMoveEvent.Direction.Down);
            yield return null;

            // Assert
            Assert.That(_above.panel.focusController.focusedElement, Is.EqualTo(_belowSkipped));
        }
    }
}
#endif
