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
    /// Pins the engine contract Velvet's focus-navigation layer is built on, against a REAL runtime panel's
    /// own <c>FocusController</c>/<c>NavigateFocusRing</c>: a <c>NavigationMoveEvent</c>'s default focus move
    /// runs in the event's post-dispatch step (after every listener), so a <c>TrickleDown</c> listener on the
    /// panel root ALWAYS runs first — and the ONLY suppression that step respects is
    /// <c>FocusController.IgnoreEvent</c> (its guard is the event's processed-by-focus-controller flag;
    /// <c>StopPropagation</c> alone is invisible to it). If a Unity upgrade breaks either half, scoped focus
    /// containment and cross-panel focus escape both lose their footing — these tests are the tripwire.
    /// </summary>
    internal sealed class FocusNavigationInterceptionTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private Button _first;
        private Button _second;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("FocusInterceptionPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;

            _first = new Button { name = "first", text = "first" };
            _second = new Button { name = "second", text = "second" };
            doc.rootVisualElement.Add(_first);
            doc.rootVisualElement.Add(_second);
            yield return null;

            _first.Focus();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_AFocusedElementOnARuntimePanel_When_ANavigationMoveEventDispatches_Then_TheDefaultFocusRingMovesFocus()
        {
            // Arrange
            var panel = _first.panel;
            Assume.That(panel.focusController.focusedElement, Is.EqualTo(_first),
                "Precondition: the first button holds focus");

            // Act — a real dispatch through the panel's own event pipeline (SendEvent runs the full
            // pre-dispatch → propagation → post-dispatch sequence, unlike a bare callback-registry poke).
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Next))
            {
                move.target = _first;
                _first.SendEvent(move);
            }
            yield return null;

            // Assert — the control case: with no interception, the runtime focus ring advanced focus.
            Assert.That(panel.focusController.focusedElement, Is.EqualTo(_second));
        }

        [UnityTest]
        public IEnumerator Given_ATrickleDownRootListenerCallingIgnoreEvent_When_ANavigationMoveEventDispatches_Then_TheDefaultFocusMoveIsSuppressed()
        {
            // Arrange — the interception pattern under test: a TrickleDown listener on the panel ROOT marks
            // the event as already-processed for the focus controller, then stops propagation.
            var panel = _first.panel;
            Assume.That(panel.focusController.focusedElement, Is.EqualTo(_first),
                "Precondition: the first button holds focus");
            var root = panel.visualTree;
            EventCallback<NavigationMoveEvent> interceptor = evt =>
            {
                panel.focusController.IgnoreEvent(evt);
                evt.StopPropagation();
            };
            root.RegisterCallback(interceptor, TrickleDown.TrickleDown);

            // Act
            try
            {
                using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Next))
                {
                    move.target = _first;
                    _first.SendEvent(move);
                }
                yield return null;
            }
            finally
            {
                root.UnregisterCallback(interceptor, TrickleDown.TrickleDown);
            }

            // Assert — focus never moved: the trickle-down listener genuinely preempted the default action
            // rather than racing it.
            Assert.That(panel.focusController.focusedElement, Is.EqualTo(_first));
        }

        [UnityTest]
        public IEnumerator Given_AFocusInHandlerFocusingAnotherElement_When_TheHandlersElementGainsFocus_Then_TheNestedFocusWins()
        {
            // Arrange — the engine's pending-focus gate: a Focus() call made from inside a FocusIn handler
            // starts a new pending switch whose result wins. Scope snap-back and chained-placeholder
            // forwarding both stand on this, so it gets its own tripwire.
            var third = new Button { name = "third", text = "third" };
            _first.panel.visualTree.Add(third);
            _second.RegisterCallback<FocusInEvent>(_ => third.Focus());

            // Act
            _second.Focus();
            yield return null;

            // Assert
            Assert.That(_first.panel.focusController.focusedElement, Is.EqualTo(third));
        }

        [UnityTest]
        public IEnumerator Given_AVelvetConstructedFocusRing_When_TheEngineExecutesASequentialMove_Then_BothAgreeOnTheTarget()
        {
            // Arrange — sequential prediction rides a Velvet-built VisualElementFocusRing over the panel
            // root; the runtime ring delegates its own Next/Previous to exactly that class, and this pins
            // the agreement so an engine change to the delegation shows up as a red test, not a drift.
            var panel = _first.panel;
            Assume.That(panel.focusController.focusedElement, Is.EqualTo(_first),
                "Precondition: the first button holds focus");
            var predicted = new VisualElementFocusRing(panel.visualTree)
                .GetNextFocusable(_first, VisualElementFocusChangeDirection.right);

            // Act
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Next))
            {
                move.target = _first;
                _first.SendEvent(move);
            }
            yield return null;

            // Assert
            Assert.That(panel.focusController.focusedElement, Is.EqualTo(predicted));
        }

        [UnityTest]
        public IEnumerator Given_ATrickleDownRootListenerCallingOnlyStopPropagation_When_ANavigationMoveEventDispatches_Then_TheDefaultFocusMoveStillRuns()
        {
            // Arrange — the negative contract: StopPropagation alone silences other listeners but the
            // post-dispatch focus move ignores it (its only guard is the processed-by-focus-controller flag),
            // so an interceptor that forgets IgnoreEvent does NOT actually contain focus.
            var panel = _first.panel;
            Assume.That(panel.focusController.focusedElement, Is.EqualTo(_first),
                "Precondition: the first button holds focus");
            var root = panel.visualTree;
            EventCallback<NavigationMoveEvent> interceptor = evt => evt.StopPropagation();
            root.RegisterCallback(interceptor, TrickleDown.TrickleDown);

            // Act
            try
            {
                using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Next))
                {
                    move.target = _first;
                    _first.SendEvent(move);
                }
                yield return null;
            }
            finally
            {
                root.UnregisterCallback(interceptor, TrickleDown.TrickleDown);
            }

            // Assert
            Assert.That(panel.focusController.focusedElement, Is.EqualTo(_second));
        }
    }

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
