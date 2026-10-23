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
    /// An arrow inside a <c>singleTabStop</c> group steps to the next or previous member in hierarchy order, on a
    /// real runtime panel with real layout, wherever the engine's 2D search would have landed: past an element
    /// outside the group lying between two members, and not round to the group's far end unless the group wraps.
    /// </summary>
    internal sealed class FocusGroupArrowPlaybackTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        // Each row is 40px tall, so a horizontal move from one row meets only that row's elements. "between" is
        // outside "gapGroup" and sits in the gap between its two members; "beyond" is outside "wrapGroup" and
        // sits right of it; nothing sits right of "stopGroup".
        [Component]
        private static VNode GroupsHost() => V.Div(className: "flex-col", children: new VNode[]
        {
            V.Div(className: "flex-row w-[300px] h-[40px]", children: new VNode[]
            {
                Group("gapGroup", wrap: false, new VNode[]
                {
                    V.Button(name: "gapA", className: "w-[100px] h-[40px]"),
                    V.Button(name: "gapC", className: "w-[100px] h-[40px] ml-[100px]"),
                }),
                V.Button(name: "between", className: "absolute left-[100px] top-0 w-[100px] h-[40px]"),
            }),
            V.Div(className: "flex-row w-[300px] h-[40px]", children: new VNode[]
            {
                Group("wrapGroup", wrap: true, new VNode[]
                {
                    V.Button(name: "wrapA", className: "w-[100px] h-[40px]"),
                    V.Button(name: "wrapB", className: "w-[100px] h-[40px]"),
                }),
                V.Button(name: "beyond", className: "w-[100px] h-[40px]"),
            }),
            Group("stopGroup", wrap: false, new VNode[]
            {
                V.Button(name: "stopA", className: "w-[100px] h-[40px]"),
                V.Button(name: "stopB", className: "w-[100px] h-[40px]"),
            }),
        });

        private static VNode Group(string name, bool wrap, VNode[] children) => V.Div(
            name: name,
            className: "flex-row",
            props: new FiberElementProps { FocusScope = new FocusScopeSettings(SingleTabStop: true) { Wrap = wrap } },
            children: children);

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("FocusGroupArrowPlaybackPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            VelvetStyleUtilities.AttachTo(doc.rootVisualElement);
            _mounted = V.Mount(doc.rootVisualElement, V.Component(GroupsHost, key: "root"));
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

        // The held flag is part of the compared value, so a start that never took focus fails the case rather
        // than reporting it inconclusive.
        private static IEnumerator MoveRightFrom(VisualElement start, System.Action<(bool, Focusable)> result)
        {
            start.Focus();
            var held = start.panel.focusController.focusedElement == start;
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Right))
            {
                move.target = start;
                start.SendEvent(move);
            }
            yield return null;
            result((held, start.panel.focusController.focusedElement));
        }

        [UnityTest]
        public IEnumerator Given_AnElementOutsideTheGroupBetweenTwoMembers_When_AnArrowMovesFromTheFirst_Then_FocusReachesTheSecond()
        {
            // Arrange
            var start = Element("gapA");
            var between = Element("between");
            var betweenSitsInTheGap = between.worldBound.xMin >= start.worldBound.xMax
                && between.worldBound.xMax <= Element("gapC").worldBound.xMin;
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveRightFrom(start, r => outcome = r);

            // Assert
            Assert.That((betweenSitsInTheGap, outcome.Item1, outcome.Item2),
                Is.EqualTo((true, true, (Focusable)Element("gapC"))));
        }

        [UnityTest]
        public IEnumerator Given_AWrappingGroupWithAnElementBeyondIt_When_AnArrowMovesPastTheLastMember_Then_FocusLandsOnTheFirst()
        {
            // Arrange
            var start = Element("wrapB");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveRightFrom(start, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)Element("wrapA"))));
        }

        [UnityTest]
        public IEnumerator Given_AGroupWithNothingBeyondIt_When_AnArrowMovesPastTheLastMember_Then_FocusStaysThere()
        {
            // Arrange
            var start = Element("stopB");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveRightFrom(start, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)start)));
        }
    }
}
#endif
