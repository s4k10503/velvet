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
    /// Arrow/d-pad moves stay inside a <c>singleTabStop</c> group on a real runtime panel with real layout,
    /// and only they do: Tab, a pointer press and a programmatic <c>Focus()</c> still take focus out.
    /// </summary>
    internal sealed class FocusScopeSingleTabStopPlaybackTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        // "last" sits in a plain scope nested in the group, so the group is its nearest SingleTabStop scope
        // without being its innermost scope. The row below is a second group laid out in reverse, so the
        // member under "last" is the row's ring-last while "rowFirst" is its ring-first.
        [Component]
        private static VNode TwoGroupsHost() => V.Div(className: "flex-col", children: new VNode[]
        {
            V.FocusScope(name: "group", singleTabStop: true, children: new VNode[]
            {
                V.Button(name: "first", className: "w-[100px] h-[40px]"),
                V.FocusScope(name: "nested", children: new VNode[]
                {
                    V.Button(name: "last", className: "w-[100px] h-[40px]"),
                }),
            }),
            V.FocusScope(name: "row", singleTabStop: true, className: "flex-row-reverse w-[200px]",
                children: new VNode[]
                {
                    V.Button(name: "rowFirst", className: "w-[100px] h-[40px] focus-visible:bg-blue-400"),
                    V.Button(name: "rowLast", className: "w-[100px] h-[40px] focus-visible:bg-blue-400"),
                }),
        });

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("FocusSingleTabStopPlaybackPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            VelvetStyleUtilities.AttachTo(doc.rootVisualElement);
            _mounted = V.Mount(doc.rootVisualElement, V.Component(TwoGroupsHost, key: "root"));
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

        private static VisualElement FocusedAfterFocusing(VisualElement start)
        {
            start.Focus();
            Assume.That(start.panel.focusController.focusedElement, Is.EqualTo(start),
                "Precondition: the move starts from a focused group member");
            return start;
        }

        private static void SendMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using var move = NavigationMoveEvent.GetPooled(direction);
            move.target = target;
            target.SendEvent(move);
        }

        [UnityTest]
        public IEnumerator Given_ASingleTabStopGroup_When_ASpatialMoveWouldLeaveItAtItsEdge_Then_FocusStaysOnTheMember()
        {
            // Arrange
            var last = FocusedAfterFocusing(Element("last"));

            // Act — the engine's own 2D move lands on "rowLast", outside the group.
            SendMove(last, NavigationMoveEvent.Direction.Down);
            yield return null;

            // Assert
            Assert.That(last.panel.focusController.focusedElement, Is.EqualTo(last));
        }

        // GREEN_ON_BASE(characterization): the base lets a spatial move between two group members through.
        // This case pins that the hold spares a move between two members of the group.
        [UnityTest]
        public IEnumerator Given_ASingleTabStopGroup_When_ASpatialMoveStaysInsideIt_Then_FocusMovesToTheOtherMember()
        {
            // Arrange
            var first = FocusedAfterFocusing(Element("first"));

            // Act
            SendMove(first, NavigationMoveEvent.Direction.Down);
            yield return null;

            // Assert
            var last = Element("last");
            Assert.That(last.panel.focusController.focusedElement, Is.EqualTo(last));
        }

        // GREEN_ON_BASE(characterization): the base lets a pointer press take focus out of a group.
        // This case pins that the hold spares the unspecified direction a press carries.
        [UnityTest]
        public IEnumerator Given_ASingleTabStopGroupMember_When_APointerPressFocusesAnElementOutside_Then_FocusLeavesTheGroup()
        {
            // Arrange
            FocusedAfterFocusing(Element("last"));
            var rowLast = Element("rowLast");

            // Act
            rowLast.SendPointerDownEvent(rowLast.worldBound.center);
            yield return null;

            // Assert
            Assert.That(rowLast.panel.focusController.focusedElement, Is.EqualTo(rowLast));
        }

        // GREEN_ON_BASE(characterization): the base lets a programmatic Focus() take focus out of a group.
        // This case pins that the hold spares the unspecified direction such a call carries.
        [UnityTest]
        public IEnumerator Given_ASingleTabStopGroupMember_When_AnElementOutsideIsFocusedProgrammatically_Then_FocusLeavesTheGroup()
        {
            // Arrange
            FocusedAfterFocusing(Element("last"));
            var rowLast = Element("rowLast");

            // Act
            rowLast.Focus();
            yield return null;

            // Assert
            Assert.That(rowLast.panel.focusController.focusedElement, Is.EqualTo(rowLast));
        }

        // GREEN_ON_BASE(characterization): the base lets the engine's own Tab move leave a group.
        // The group's innermost scope around "last" is the plain one, so no interception redirects this Tab
        // and the engine's sequential move reaches FocusIn; this case pins that the hold spares it.
        [UnityTest]
        public IEnumerator Given_AGroupMemberInsideANestedPlainScope_When_TabMovesPastTheGroup_Then_FocusLeavesTheGroup()
        {
            // Arrange
            var last = FocusedAfterFocusing(Element("last"));

            // Act
            SendMove(last, NavigationMoveEvent.Direction.Next);
            yield return null;

            // Assert
            var rowFirst = Element("rowFirst");
            Assert.That(rowFirst.panel.focusController.focusedElement, Is.EqualTo(rowFirst));
        }

        [UnityTest]
        public IEnumerator Given_ASpatialMoveHeldAtAGroupEdge_When_TabThenEntersTheGroupItHadLandedIn_Then_FocusTakesThatGroupsFirstMember()
        {
            // Arrange — the held move's landing, "rowLast", is a member of the row the user never reached.
            var last = FocusedAfterFocusing(Element("last"));
            SendMove(last, NavigationMoveEvent.Direction.Down);
            yield return null;

            // Act
            SendMove(last, NavigationMoveEvent.Direction.Next);
            yield return null;

            // Assert
            var rowFirst = Element("rowFirst");
            Assert.That(rowFirst.panel.focusController.focusedElement, Is.EqualTo(rowFirst));
        }

        [UnityTest]
        public IEnumerator Given_ASpatialMoveHeldAtAGroupEdge_When_TheLandingKeepsAFocusVisibleResidue_Then_TheNextTickSettlesIt()
        {
            // Arrange
            var last = FocusedAfterFocusing(Element("last"));
            var rowLast = Element("rowLast");
            SendMove(last, NavigationMoveEvent.Direction.Down);

            // The residue a reverted landing can keep is staged through the signal channel, for the reason
            // FocusScopeTests' contained-scope settle case gives.
            using (var evt = FocusEvent.GetPooled()) rowLast.SimulateEvent(evt);
            Assume.That(rowLast.ClassListContains("bg-blue-400"), Is.True,
                "Precondition: the focus-visible payload is lit");

            // Act — the panel's next tick runs the scheduled settle.
            yield return null;
            yield return null;

            // Assert
            Assert.That(rowLast.ClassListContains("bg-blue-400"), Is.False);
        }
    }
}
#endif
