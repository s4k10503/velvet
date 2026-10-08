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
    /// A <c>singleTabStop</c> group with an orientation keeps an arrow/d-pad move on the other axis on the
    /// member it started from, on a real runtime panel with real layout. Each excluded move has a member
    /// touching the start in that direction, and the allowed-axis cases show the same geometry travels.
    /// </summary>
    internal sealed class FocusScopeOrientationPlaybackTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        private static StateUpdater<FocusScopeOrientation> s_setLiveOrientation;

        [Component]
        private static VNode OrientedGroupsHost()
        {
            var (live, setLive) = Hooks.UseState(FocusScopeOrientation.Both);
            s_setLiveOrientation = setLive;
            return V.Div(className: "flex-col", children: new VNode[]
            {
                Group("colH", FocusScopeOrientation.Horizontal, "flex-col w-[100px]"),
                Group("colV", FocusScopeOrientation.Vertical, "flex-col w-[100px]"),
                Group("rowV", FocusScopeOrientation.Vertical, "flex-row w-[200px]"),
                Group("rowH", FocusScopeOrientation.Horizontal, "flex-row w-[200px]"),
                Group("live", live, "flex-col w-[100px]"),
                Group("plain", FocusScopeOrientation.Horizontal, "flex-col w-[100px]", singleTabStop: false),
                V.FocusScope(singleTabStop: true, className: "flex-row w-[200px]", children: new VNode[]
                {
                    Group("nestVB", FocusScopeOrientation.Vertical, "flex-row w-[200px]"),
                }),
                V.FocusScope(singleTabStop: true, orientation: FocusScopeOrientation.Vertical,
                    className: "flex-row w-[200px]", children: new VNode[]
                    {
                        Group("nestBV", FocusScopeOrientation.Both, "flex-row w-[200px]"),
                    }),
            });
        }

        private static VNode Group(
            string name, FocusScopeOrientation orientation, string className, bool singleTabStop = true)
            => V.FocusScope(singleTabStop: singleTabStop, orientation: orientation, className: className, children: new VNode[]
            {
                V.Button(name: name + "A", className: "w-[100px] h-[40px]"),
                V.Button(name: name + "B", className: "w-[100px] h-[40px]"),
            });

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("FocusScopeOrientationPlaybackPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            VelvetStyleUtilities.AttachTo(doc.rootVisualElement);
            _mounted = V.Mount(doc.rootVisualElement, V.Component(OrientedGroupsHost, key: "root"));
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

        // The held flag is part of the compared value, so a start that never took focus fails the case
        // rather than reporting it inconclusive.
        private IEnumerator MoveFrom(
            VisualElement start, NavigationMoveEvent.Direction direction, System.Action<(bool, Focusable)> result)
        {
            start.Focus();
            var held = start.panel.focusController.focusedElement == start;
            using (var move = NavigationMoveEvent.GetPooled(direction))
            {
                move.target = start;
                start.SendEvent(move);
            }
            yield return null;
            result((held, start.panel.focusController.focusedElement));
        }

        [UnityTest]
        public IEnumerator Given_AHorizontalGroup_When_ADownMoveWouldReachTheMemberBelow_Then_FocusStaysOnTheMember()
        {
            // Arrange
            var start = Element("colHA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Down, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)start)));
        }

        [UnityTest]
        public IEnumerator Given_AHorizontalGroup_When_AnUpMoveWouldReachTheMemberAbove_Then_FocusStaysOnTheMember()
        {
            // Arrange
            var start = Element("colHB");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Up, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)start)));
        }

        [UnityTest]
        public IEnumerator Given_AVerticalGroup_When_ARightMoveWouldReachTheMemberBesideIt_Then_FocusStaysOnTheMember()
        {
            // Arrange
            var start = Element("rowVA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Right, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)start)));
        }

        [UnityTest]
        public IEnumerator Given_AVerticalGroup_When_ALeftMoveWouldReachTheMemberBesideIt_Then_FocusStaysOnTheMember()
        {
            // Arrange
            var start = Element("rowVB");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Left, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)start)));
        }

        [UnityTest]
        public IEnumerator Given_AVerticalGroup_When_ADownMoveReachesTheMemberBelow_Then_FocusMovesThere()
        {
            // Arrange
            var start = Element("colVA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Down, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)Element("colVB"))));
        }

        [UnityTest]
        public IEnumerator Given_AHorizontalGroup_When_ARightMoveReachesTheMemberBesideIt_Then_FocusMovesThere()
        {
            // Arrange
            var start = Element("rowHA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Right, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)Element("rowHB"))));
        }

        [UnityTest]
        public IEnumerator Given_AHorizontalGroup_When_ADownMoveIsIgnored_Then_TheMemberBelowReceivesNoFocusEvent()
        {
            // Arrange
            var start = Element("colHA");
            var focusIns = 0;
            Element("colHB").RegisterCallback<FocusInEvent>(_ => focusIns++);
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Down, r => outcome = r);

            // Assert
            Assert.That((outcome.Item1, focusIns), Is.EqualTo((true, 0)));
        }

        [UnityTest]
        public IEnumerator Given_AVerticalGroupNestedInABothGroup_When_ARightMoveReachesTheNextMember_Then_FocusMovesThere()
        {
            // Arrange
            var start = Element("nestVBA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Right, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)Element("nestVBB"))));
        }

        [UnityTest]
        public IEnumerator Given_AHorizontalGroup_When_ADownMoveIsIgnored_Then_TheFocusedMembersOwnHandlerStillSeesIt()
        {
            // Arrange
            var start = Element("colHA");
            var seen = 0;
            start.RegisterCallback<NavigationMoveEvent>(_ => seen++);
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Down, r => outcome = r);

            // Assert
            Assert.That((outcome.Item1, seen), Is.EqualTo((true, 1)));
        }

        [UnityTest]
        public IEnumerator Given_ABothGroupNestedInAVerticalGroup_When_ARightMoveReachesTheNextMember_Then_FocusStaysOnTheMember()
        {
            // Arrange
            var start = Element("nestBVA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Right, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)start)));
        }

        [UnityTest]
        public IEnumerator Given_AGroupWhoseOrientationBecomesHorizontal_When_ADownMoveReachesTheMemberBelow_Then_FocusStaysOnTheMember()
        {
            // Arrange
            s_setLiveOrientation.Invoke(FocusScopeOrientation.Horizontal);
            _mounted.FlushStateForTest();
            yield return null;
            var start = Element("liveA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Down, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)start)));
        }

        [UnityTest]
        public IEnumerator Given_AVerticalGroup_When_ANoneMoveDispatchesFromAMemberWithAnotherBelow_Then_FocusStaysOnTheMember()
        {
            // Arrange
            var start = Element("colVA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.None, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)start)));
        }

        [UnityTest]
        public IEnumerator Given_AScopeThatIsNotASingleTabStop_When_ItNamesAnOrientation_Then_ADownMoveStillTravels()
        {
            // Arrange
            var start = Element("plainA");
            (bool, Focusable) outcome = default;

            // Act
            yield return MoveFrom(start, NavigationMoveEvent.Direction.Down, r => outcome = r);

            // Assert
            Assert.That(outcome, Is.EqualTo((true, (Focusable)Element("plainB"))));
        }
    }
}
#endif
