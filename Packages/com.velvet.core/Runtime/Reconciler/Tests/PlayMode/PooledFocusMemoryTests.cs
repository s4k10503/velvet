using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

#if UNITY_EDITOR
namespace Velvet.Tests
{
    /// <summary>
    /// An element Velvet releases leaves no focus behind in the panel it left: a Button that held focus and
    /// comes back from the pool somewhere else behaves as a freshly constructed one.
    /// </summary>
    internal sealed class PooledFocusMemoryTests
    {
        private static StateUpdater<bool> s_setShown;
        private static StateUpdater<bool> s_setOtherShown;
        private static StateUpdater<bool> s_setReversed;

        private GameObject _firstPanelGo;
        private PanelSettings _firstSettings;
        private GameObject _secondPanelGo;
        private PanelSettings _secondSettings;
        private MountedTree _released;
        private MountedTree _reused;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_setShown = default;
            s_setOtherShown = default;
            s_setReversed = default;
            (_firstPanelGo, _firstSettings) = CreatePanel("PooledFocusMemoryFirstPanel");
            (_secondPanelGo, _secondSettings) = CreatePanel("PooledFocusMemorySecondPanel");
            // A case that rents needs the Button it released to be the one handed out.
            VNodePoolTestAccess.ClearButtonPoolForTest();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            SetFocusedPanel(null);
            _released?.Dispose();
            _released = null;
            _reused?.Dispose();
            _reused = null;
            if (_firstPanelGo != null) UnityEngine.Object.Destroy(_firstPanelGo);
            if (_firstSettings != null) UnityEngine.Object.Destroy(_firstSettings);
            if (_secondPanelGo != null) UnityEngine.Object.Destroy(_secondPanelGo);
            if (_secondSettings != null) UnityEngine.Object.Destroy(_secondSettings);
            yield return null;
        }

        private static (GameObject, PanelSettings) CreatePanel(string name)
        {
            var go = new GameObject(name);
            var doc = go.AddComponent<UIDocument>();
            var settings = TestPanelSettings.Create();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = settings;
            return (go, settings);
        }

        private VisualElement FirstRoot => _firstPanelGo.GetComponent<UIDocument>().rootVisualElement;

        private VisualElement SecondRoot => _secondPanelGo.GetComponent<UIDocument>().rootVisualElement;

        [Component]
        private static VNode SingleButton() => V.Button(name: "button");

        // The Button inside a container is released for good rather than pooled.
        [Component]
        private static VNode ButtonInsideADiv() => V.Div(children: new VNode[]
        {
            V.Button(name: "button"),
        });

        // The field hides the Button whenever it loses focus, from inside the dispatch of that focus change.
        [Component]
        private static VNode FieldHidingAButtonOnFocusOut()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(children: new VNode[]
            {
                new ElementNode
                {
                    ElementType = typeof(TextField),
                    Name = "field",
                    Events = new FiberEventBinding[] { new FocusOutBinding { Handler = _ => s_setShown.Invoke(false) } },
                },
                shown ? V.Button(name: "button") : null,
            });
        }

        private static ElementNode FieldHidingTheButtonOnFocusOut() => new()
        {
            ElementType = typeof(TextField),
            Key = "field",
            Name = "field",
            Events = new FiberEventBinding[] { new FocusOutBinding { Handler = _ => s_setShown.Invoke(false) } },
        };

        // Focus can move from the field to "gaining" while the field's focus-out hides "button".
        [Component]
        private static VNode FieldHidingAButtonBesideAnother()
        {
            var (shown, setShown) = Hooks.UseState(true);
            var (otherShown, setOtherShown) = Hooks.UseState(true);
            s_setShown = setShown;
            s_setOtherShown = setOtherShown;
            return V.Div(children: new VNode[]
            {
                FieldHidingTheButtonOnFocusOut(),
                otherShown ? V.Button(key: "gaining", name: "gaining") : null,
                shown ? V.Button(key: "button", name: "button") : null,
            });
        }

        // Taking the Button from ahead of two keyed siblings to behind them moves it, which detaches it and
        // attaches it again.
        [Component]
        private static VNode FieldHidingAReorderedButton()
        {
            var (shown, setShown) = Hooks.UseState(true);
            var (reversed, setReversed) = Hooks.UseState(false);
            s_setShown = setShown;
            s_setReversed = setReversed;
            var button = shown ? V.Button(key: "button", name: "button") : null;
            var first = V.Button(key: "first", name: "first");
            var second = V.Button(key: "second", name: "second");
            return V.Div(children: reversed
                ? new VNode[] { FieldHidingTheButtonOnFocusOut(), first, second, button }
                : new VNode[] { FieldHidingTheButtonOnFocusOut(), button, first, second });
        }

        // A Button expanded into a Toggle, which UI Toolkit treats as a composite root: focus on the Button is
        // reported as focus on the Toggle.
        [Component]
        private static VNode ButtonsInsideAToggle()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Custom<Toggle>(name: "toggle", children: new VNode[]
            {
                V.Button(name: "kept"),
                shown ? V.Button(name: "button") : null,
            });
        }

        // A z-indexed child leaves its slot for a layer container, so its teardown detaches it before releasing it.
        [Component]
        private static VNode ZIndexedButton() => V.Div(children: new VNode[]
        {
            V.Button(name: "button", className: "absolute z-10"),
        });

        private static IEnumerator Settle()
        {
            yield return null;
            yield return null;
        }

        // The panel UI Toolkit's runtime event system treats as focused. Its setter blurs the panel losing
        // focus, which keeps that panel's last focused element, and marks the one gaining it to refocus that
        // element on its next tick.
        private static void SetFocusedPanel(IPanel panel)
        {
            var utility = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility", true);
            var system = utility.GetProperty("defaultEventSystem", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                ?? throw new MissingMemberException(utility.FullName, "defaultEventSystem");
            var focusedPanel = system.GetType().GetProperty("focusedPanel", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMemberException(system.GetType().FullName, "focusedPanel");
            focusedPanel.SetValue(system, panel);
        }

        // What the controller still references as its last focused element, and as its focused element.
        private static (object, object) HeldFocus(object controller)
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = controller.GetType();
            var last = type.GetField("m_LastFocusedElement", instance)
                ?? throw new MissingFieldException(type.FullName, "m_LastFocusedElement");
            var entries = type.GetField("m_FocusedElements", instance)
                ?? throw new MissingFieldException(type.FullName, "m_FocusedElements");
            var list = (IList)entries.GetValue(controller);
            var focused = list.Count == 0
                ? null
                : list[0].GetType().GetField("m_FocusedElement", BindingFlags.Instance | BindingFlags.Public)?.GetValue(list[0])
                    ?? throw new MissingFieldException(list[0].GetType().FullName, "m_FocusedElement");
            return (last.GetValue(controller), focused);
        }

        // Mounts `tree` on the first panel and focuses its Button. The frame afterwards is the panel's tick that
        // refocuses its last focused element after the panel gained focus, which would otherwise run once the
        // Button is already somewhere else.
        private IEnumerator MountAndFocus(VisualElement[] released, VNode tree)
        {
            _released = V.Mount(FirstRoot, tree);
            yield return Settle();
            released[0] = FirstRoot.Q<VisualElement>("button");
            released[0].Focus();
            released[1] = released[0].panel.focusController.focusedElement as VisualElement;
            yield return null;
        }

        private void Release()
        {
            _released.Dispose();
            _released = null;
        }

        [UnityTest]
        public IEnumerator Given_AFocusedButtonReleasedFromOnePanel_When_ItIsRentedOnAnotherPanelAndFocused_Then_ItKeepsTheFocus()
        {
            // Arrange
            var released = new VisualElement[2];
            yield return MountAndFocus(released, V.Component(SingleButton, key: "released"));
            Release();
            _reused = V.Mount(SecondRoot, V.Component(SingleButton, key: "reused"));
            yield return Settle();
            var reused = SecondRoot.Q<VisualElement>("button");

            // Act
            reused.Focus();

            // Assert
            Assert.That(
                (released[1] == released[0], ReferenceEquals(reused, released[0]), reused.panel.focusController.focusedElement),
                Is.EqualTo((true, true, (Focusable)reused)));
        }

        [UnityTest]
        public IEnumerator Given_AButtonTheFirstPanelLastFocusedIsRentedOnAnotherPanel_When_FocusReturnsToTheFirstPanel_Then_TheRentedButtonIsNotFocused()
        {
            // Arrange
            var released = new VisualElement[2];
            yield return MountAndFocus(released, V.Component(SingleButton, key: "released"));
            SetFocusedPanel(null);
            Release();
            _reused = V.Mount(SecondRoot, V.Component(SingleButton, key: "reused"));
            yield return Settle();
            var reused = SecondRoot.Q<VisualElement>("button");
            var focusIns = 0;
            reused.RegisterCallback<FocusInEvent>(_ => focusIns++);

            // Act
            SetFocusedPanel(FirstRoot.panel);
            yield return Settle();

            // Assert
            Assert.That(
                (released[1] == released[0], ReferenceEquals(reused, released[0]), focusIns),
                Is.EqualTo((true, true, 0)));
        }

        [UnityTest]
        public IEnumerator Given_AFocusedButtonReleasedFromAPanel_When_ItIsRentedBackOnTheSamePanel_Then_ThePanelHoldsNoFocus()
        {
            // Arrange
            var released = new VisualElement[2];
            yield return MountAndFocus(released, V.Component(SingleButton, key: "released"));
            Release();

            // Act
            _reused = V.Mount(FirstRoot, V.Component(SingleButton, key: "reused"));
            yield return Settle();
            var reused = FirstRoot.Q<VisualElement>("button");

            // Assert
            Assert.That(
                (released[1] == released[0], ReferenceEquals(reused, released[0]), FirstRoot.panel.focusController.focusedElement),
                Is.EqualTo((true, true, (Focusable)null)));
        }

        [UnityTest]
        public IEnumerator Given_AFocusedZIndexedButtonReleasedFromAPanel_When_ItIsRentedBackOnTheSamePanel_Then_ThePanelHoldsNoFocus()
        {
            // Arrange
            var released = new VisualElement[2];
            yield return MountAndFocus(released, V.Component(ZIndexedButton, key: "released"));
            var zManaged = _released.Root.Reconciler.Context.ZLayerMembers.ContainsKey(released[0]);
            Release();

            // Act
            _reused = V.Mount(FirstRoot, V.Component(SingleButton, key: "reused"));
            yield return Settle();
            var reused = FirstRoot.Q<VisualElement>("button");

            // Assert
            Assert.That(
                (zManaged, released[1] == released[0], ReferenceEquals(reused, released[0]), FirstRoot.panel.focusController.focusedElement),
                Is.EqualTo((true, true, true, (Focusable)null)));
        }

        [UnityTest]
        public IEnumerator Given_AFocusedButtonInsideAContainer_When_TheContainerIsReleased_Then_ThePanelKeepsNoReferenceToTheButton()
        {
            // Arrange
            var released = new VisualElement[2];
            yield return MountAndFocus(released, V.Component(ButtonInsideADiv, key: "released"));
            var controller = FirstRoot.panel.focusController;

            // Act
            Release();

            // Assert
            var (last, focused) = HeldFocus(controller);
            Assert.That((released[1] == released[0], last, focused), Is.EqualTo((true, (object)null, (object)null)));
        }

        [UnityTest]
        public IEnumerator Given_AFieldWhoseFocusOutHidesAButton_When_FocusMovesToThatButtonAndItIsShownAgain_Then_ThePanelHoldsNoFocus()
        {
            // Arrange
            _released = V.Mount(FirstRoot, V.Component(FieldHidingAButtonOnFocusOut, key: "form"));
            yield return Settle();
            var field = FirstRoot.Q<VisualElement>("field");
            var button = FirstRoot.Q<VisualElement>("button");
            field.Focus();
            var fieldFocused = FirstRoot.panel.focusController.focusedElement == field;
            yield return null;

            // Act
            button.Focus();
            var hiddenByTheFocusOut = button.panel == null;
            s_setShown.Invoke(true);
            yield return Settle();
            var shownAgain = FirstRoot.Q<VisualElement>("button");

            // Assert
            Assert.That(
                (fieldFocused, hiddenByTheFocusOut, ReferenceEquals(shownAgain, button),
                    FirstRoot.panel.focusController.focusedElement, shownAgain.hasFocusPseudoState),
                Is.EqualTo((true, true, true, (Focusable)null, false)));
        }

        [UnityTest]
        public IEnumerator Given_AFocusedButtonInsideAToggle_When_TheButtonIsReleased_Then_TheToggleLosesTheFocusState()
        {
            // Arrange
            _released = V.Mount(FirstRoot, V.Component(ButtonsInsideAToggle, key: "toggle"));
            yield return Settle();
            var toggle = FirstRoot.Q<VisualElement>("toggle");
            var button = FirstRoot.Q<VisualElement>("button");
            button.Focus();
            var reportedOnTheToggle = FirstRoot.panel.focusController.focusedElement == toggle;
            yield return null;

            // Act
            s_setShown.Invoke(false);
            yield return Settle();

            // Assert
            Assert.That(
                (reportedOnTheToggle, button.panel == null, FirstRoot.panel.focusController.focusedElement,
                    toggle.hasFocusPseudoState),
                Is.EqualTo((true, true, (Focusable)null, false)));
        }

        // GREEN_ON_BASE(characterization): the base has no scrub to disturb this. The case pins that the scrub
        // leaves alone focus on an element outside what it releases, beside it in the same composite root.
        [UnityTest]
        public IEnumerator Given_AFocusedButtonInsideAToggle_When_ASiblingButtonIsReleased_Then_TheFocusedButtonKeepsTheFocus()
        {
            // Arrange
            _released = V.Mount(FirstRoot, V.Component(ButtonsInsideAToggle, key: "toggle"));
            yield return Settle();
            var toggle = FirstRoot.Q<VisualElement>("toggle");
            var kept = FirstRoot.Q<VisualElement>("kept");
            var button = FirstRoot.Q<VisualElement>("button");
            kept.Focus();
            var controller = FirstRoot.panel.focusController;
            yield return null;

            // Act
            s_setShown.Invoke(false);
            yield return Settle();

            // Assert
            var (last, focused) = HeldFocus(controller);
            Assert.That(
                (button.panel == null, controller.focusedElement, last, focused, toggle.hasFocusPseudoState),
                Is.EqualTo((true, (Focusable)toggle, (object)kept, (object)kept, true)));
        }

        // GREEN_ON_BASE(characterization): the base has no scrub to disturb this. The case pins that the
        // element gaining focus while another is released carries nothing that later clears focus elsewhere.
        [UnityTest]
        public IEnumerator Given_AButtonGainingFocusWhileAnotherIsReleased_When_ItIsMountedAgainLater_Then_TheOtherKeepsTheFocusItTookSince()
        {
            // Arrange
            _released = V.Mount(FirstRoot, V.Component(FieldHidingAButtonBesideAnother, key: "form"));
            yield return Settle();
            FirstRoot.Q<VisualElement>("field").Focus();
            var gaining = FirstRoot.Q<VisualElement>("gaining");
            gaining.Focus();
            var hiddenByTheFocusOut = FirstRoot.Q<VisualElement>("button") == null;
            s_setShown.Invoke(true);
            yield return Settle();
            var button = FirstRoot.Q<VisualElement>("button");
            button.Focus();
            var buttonFocused = FirstRoot.panel.focusController.focusedElement == button;
            s_setOtherShown.Invoke(false);
            yield return Settle();

            // Act
            s_setOtherShown.Invoke(true);
            yield return Settle();

            // Assert
            Assert.That(
                (hiddenByTheFocusOut, buttonFocused, ReferenceEquals(FirstRoot.Q<VisualElement>("gaining"), gaining),
                    FirstRoot.panel.focusController.focusedElement),
                Is.EqualTo((true, true, true, (Focusable)button)));
        }

        // GREEN_ON_BASE(characterization): a keyed move keeps focus on the base. This case pins that the scrub
        // armed for a Button released while gaining focus is spent once that Button is mounted again.
        [UnityTest]
        public IEnumerator Given_AButtonReleasedWhileGainingFocusAndMountedAgain_When_ItIsFocusedAndMoved_Then_ItKeepsTheFocus()
        {
            // Arrange
            _released = V.Mount(FirstRoot, V.Component(FieldHidingAReorderedButton, key: "form"));
            yield return Settle();
            FirstRoot.Q<VisualElement>("field").Focus();
            var button = FirstRoot.Q<VisualElement>("button");
            button.Focus();
            var hiddenByTheFocusOut = button.panel == null;
            s_setShown.Invoke(true);
            yield return Settle();
            var shownAgain = FirstRoot.Q<VisualElement>("button");
            shownAgain.Focus();
            var focusLanded = FirstRoot.panel.focusController.focusedElement == shownAgain;
            var attaches = 0;
            shownAgain.RegisterCallback<AttachToPanelEvent>(_ => attaches++);

            // Act
            s_setReversed.Invoke(true);
            yield return Settle();

            // Assert
            Assert.That(
                (hiddenByTheFocusOut, ReferenceEquals(shownAgain, button), focusLanded, attaches,
                    FirstRoot.panel.focusController.focusedElement),
                Is.EqualTo((true, true, true, 1, (Focusable)shownAgain)));
        }
    }
}
#endif
