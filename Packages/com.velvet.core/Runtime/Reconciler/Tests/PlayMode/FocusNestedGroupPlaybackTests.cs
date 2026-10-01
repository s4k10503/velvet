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
    /// Arrows move across <c>singleTabStop</c> groups nested in each other, as the outermost of nested toolbars
    /// handles them in React Aria's <c>useToolbar</c>, on a real runtime panel with real layout.
    /// </summary>
    internal sealed class FocusNestedGroupPlaybackTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [Component]
        private static VNode NestedGroupsHost() => V.FocusScope(name: "outer", singleTabStop: true,
            className: "flex-row w-[300px]", children: new VNode[]
            {
                V.Button(name: "x", className: "w-[100px] h-[40px]"),
                V.FocusScope(name: "inner", singleTabStop: true, className: "flex-row", children: new VNode[]
                {
                    V.Button(name: "y", className: "w-[100px] h-[40px]"),
                }),
                V.Button(name: "z", className: "w-[100px] h-[40px]"),
            });

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("FocusNestedGroupPlaybackPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            VelvetStyleUtilities.AttachTo(doc.rootVisualElement);
            _mounted = V.Mount(doc.rootVisualElement, V.Component(NestedGroupsHost, key: "root"));
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        private VisualElement Element(string name)
            => _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>(name);

        [UnityTest]
        public IEnumerator Given_FocusInAGroupNestedInAnother_When_AnArrowMovesToTheOuterGroupsNextMember_Then_FocusMovesThere()
        {
            // Arrange
            var y = Element("y");
            var z = Element("z");
            y.Focus();
            var yHeld = y.panel.focusController.focusedElement == y;
            var zRightOfY = z.worldBound.xMin >= y.worldBound.xMax;

            // Act
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Right))
            {
                move.target = y;
                y.SendEvent(move);
            }
            yield return null;

            // Assert
            Assert.That((yHeld, zRightOfY, y.panel.focusController.focusedElement), Is.EqualTo((true, true, (Focusable)z)));
        }
    }
}
#endif
