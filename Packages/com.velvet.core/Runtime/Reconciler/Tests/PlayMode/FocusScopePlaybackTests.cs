#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the containment path the sequential interception cannot cover, on a real runtime panel with real
    /// layout: a spatial (2D) move that exits a contained scope is snapped back inside within the same event
    /// flush — Velvet never predicts or reimplements the engine's 2D navigation, it observes the exit through
    /// FocusIn and corrects it on the engine's own pending-focus gate.
    /// </summary>
    internal sealed class FocusScopePlaybackTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [Component]
        private static VNode ContainedColumnHost() => V.Div(className: "flex-col", children: new VNode[]
        {
            V.FocusScope(name: "scope", contain: true, children: new VNode[]
            {
                V.Button(name: "inside", className: "w-[100px] h-[40px]"),
            }),
            V.Button(name: "below", className: "w-[100px] h-[40px]"),
        });

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("FocusScopePlaybackPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            doc.rootVisualElement.styleSheets.Add(sheet);
            _mounted = V.Mount(doc.rootVisualElement, V.Component(ContainedColumnHost, key: "root"));
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

        [UnityTest]
        public IEnumerator Given_AContainedFocusScope_When_ASpatialMoveWouldExitTheScope_Then_FocusIsBackInsideTheScopeAfterTheFlush()
        {
            // Arrange
            var root = _panelGo.GetComponent<UIDocument>().rootVisualElement;
            var inside = root.Q<VisualElement>("inside");
            inside.Focus();
            Assume.That(inside.panel.focusController.focusedElement, Is.EqualTo(inside),
                "Precondition: the scope member holds focus");

            // Act — a Down move: the engine's own 2D navigation lands on "below" (outside the scope), and
            // the FocusIn snap-back pulls it back within the same flush.
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Down))
            {
                move.target = inside;
                inside.SendEvent(move);
            }
            yield return null;

            // Assert
            Assert.That(inside.panel.focusController.focusedElement, Is.EqualTo(inside));
        }
    }
}
#endif
