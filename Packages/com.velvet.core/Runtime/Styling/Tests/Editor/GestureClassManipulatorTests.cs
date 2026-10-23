using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the gesture-driven class manipulation contract (hover / tap class toggling).
    /// <list type="bullet">
    /// <item>The element builders expose <c>whileHoverClass</c> / <c>whileTapClass</c> on the produced node, and
    /// a node built without them leaves both properties null.</item>
    /// <item>Updating the gesture classes while a state is active swaps the old classes off and the new classes
    /// on for that state; while the state is inactive the update changes no classes on the element.</item>
    /// <item>Null hover/tap arrays are treated as empty, so constructing and attaching the manipulator never throws.</item>
    /// <item><c>ParseClassNames</c> splits a space-separated string into its tokens (the null/empty contract is
    /// owned by the ParseClassNames cache fixture).</item>
    /// <item>Reconciliation registers one manipulator per element that declares gesture classes, registers none
    /// for an element without them, and removes the manipulator when the gesture classes are patched away.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The reconciler tests read the internal <c>GestureManipulators</c> dictionary via reflection
    /// (<see cref="GetGestureManipulatorCount"/>).
    /// </remarks>
    [TestFixture]
    internal sealed class GestureClassManipulatorTests
    {
        private VisualElement _element;

        [SetUp]
        public void SetUp()
        {
            _element = new VisualElement();
        }

        #region Driven writes

        private static readonly long HoverGesture = StyleLayerPriority.GestureOf(StyleLayerPriority.Hover);

        [Test]
        public void Given_AGestureLayerOnTheBackground_When_APlayDrivesTheBackground_Then_TheGesturesColorStays()
        {
            // Arrange
            StyleArbitraryValueResolver.Apply(_element, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.red),
                HoverGesture);

            // Act
            StyleArbitraryValueResolver.ApplyDriven(_element,
                new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.blue));

            // Assert
            Assert.That(_element.style.backgroundColor.value, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AGestureLayerOnTheBackground_When_AnExitDrivesTheBackground_Then_TheExitsColorShows()
        {
            // Arrange
            StyleArbitraryValueResolver.Apply(_element, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.red),
                HoverGesture);

            // Act
            StyleArbitraryValueResolver.ApplyDriven(_element,
                new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.blue), exit: true);

            // Assert
            Assert.That(_element.style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        #endregion

        #region VNode Builder

        [Test]
        public void Given_DivWithWhileHoverClass_When_Built_Then_NodeExposesHoverClass()
        {
            // Act
            var node = V.Div(whileHoverClass: "hover-glow");

            // Assert
            Assert.That(node.WhileHoverClass, Is.EqualTo("hover-glow"));
        }

        [Test]
        public void Given_DivWithWhileTapClass_When_Built_Then_NodeExposesTapClass()
        {
            // Act
            var node = V.Div(whileTapClass: "tap-shrink");

            // Assert
            Assert.That(node.WhileTapClass, Is.EqualTo("tap-shrink"));
        }

        [Test]
        public void Given_ButtonWithHoverAndTapClasses_When_Built_Then_NodeExposesBothClasses()
        {
            // Act
            var node = V.Button(whileHoverClass: "btn-hover", whileTapClass: "btn-tap");

            // Assert
            Assert.That((node.WhileHoverClass, node.WhileTapClass), Is.EqualTo(("btn-hover", "btn-tap")));
        }

        [Test]
        public void Given_LabelWithWhileHoverClass_When_Built_Then_NodeExposesHoverClass()
        {
            // Act
            var node = V.Label(whileHoverClass: "label-hover");

            // Assert
            Assert.That(node.WhileHoverClass, Is.EqualTo("label-hover"));
        }

        [Test]
        public void Given_MotionWithHoverAndTapClasses_When_Built_Then_NodeExposesBothClasses()
        {
            // Act
            var node = V.Motion(whileHoverClass: "motion-hover", whileTapClass: "motion-tap");

            // Assert
            Assert.That((node.WhileHoverClass, node.WhileTapClass), Is.EqualTo(("motion-hover", "motion-tap")));
        }

        [Test]
        public void Given_ImageWithHoverAndTapClasses_When_Built_Then_NodeExposesBothClasses()
        {
            // Act
            var node = V.Image(whileHoverClass: "img-hover", whileTapClass: "img-tap");

            // Assert
            Assert.That((node.WhileHoverClass, node.WhileTapClass), Is.EqualTo(("img-hover", "img-tap")));
        }

        [Test]
        public void Given_ButtonWithWhileFocusClass_When_Built_Then_NodeExposesFocusClass()
        {
            // Act
            var node = V.Button(whileFocusClass: "focus-ring");

            // Assert
            Assert.That(node.WhileFocusClass, Is.EqualTo("focus-ring"));
        }

        [Test]
        public void Given_DivWithoutGestureClasses_When_Built_Then_BothGesturePropertiesAreNull()
        {
            // Act
            var node = V.Div(className: "plain");

            // Assert
            Assert.That((node.WhileHoverClass, node.WhileTapClass), Is.EqualTo(((string)null, (string)null)));
        }

        #endregion

        #region StyleGestureClassManipulator Unit

        // GREEN_ON_BASE(characterization): the base swaps the classes on the class list itself.
        // What reddens it is UpdateClasses leaving the old hover classes on, or not putting the new ones on.
        [Test]
        public void Given_HoveredElement_When_HoverClassesUpdated_Then_SwapsOldHoverForNew()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                new[] { "old-hover" }, Array.Empty<string>(), Array.Empty<string>());
            _element.AddManipulator(manipulator);
            using (var over = PointerOverEvent.GetPooled()) _element.SimulateEvent(over);

            // Act
            manipulator.UpdateClasses(new[] { "new-hover" }, Array.Empty<string>(), Array.Empty<string>());

            // Assert
            Assert.That(
                (_element.ClassListContains("old-hover"), _element.ClassListContains("new-hover")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_NotHoveredElement_When_HoverClassesUpdated_Then_NewClassIsNotApplied()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                new[] { "old-hover" }, Array.Empty<string>(), Array.Empty<string>());
            _element.AddManipulator(manipulator);

            // Act
            manipulator.UpdateClasses(new[] { "new-hover" }, Array.Empty<string>(), Array.Empty<string>());

            // Assert
            Assert.That(
                (_element.ClassListContains("old-hover"), _element.ClassListContains("new-hover")),
                Is.EqualTo((false, false)));
        }

        // GREEN_ON_BASE(characterization): the base swaps the classes on the class list itself.
        // What reddens it is UpdateClasses leaving the old tap classes on, or not putting the new ones on.
        [Test]
        public void Given_TappedElement_When_TapClassesUpdated_Then_SwapsOldTapForNew()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                Array.Empty<string>(), new[] { "old-tap" }, Array.Empty<string>());
            _element.AddManipulator(manipulator);
            using (var down = PointerDownEvent.GetPooled()) _element.SimulateEvent(down);

            // Act
            manipulator.UpdateClasses(Array.Empty<string>(), new[] { "new-tap" }, Array.Empty<string>());

            // Assert
            Assert.That(
                (_element.ClassListContains("old-tap"), _element.ClassListContains("new-tap")),
                Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): the base swaps the classes on the class list itself.
        // What reddens it is UpdateClasses swapping one state's classes and not the other's.
        [Test]
        public void Given_HoveredAndTappedElement_When_BothClassesUpdated_Then_SwapsBothStatesClasses()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                new[] { "old-hover" }, new[] { "old-tap" }, Array.Empty<string>());
            _element.AddManipulator(manipulator);
            using (var over = PointerOverEvent.GetPooled()) _element.SimulateEvent(over);
            using (var down = PointerDownEvent.GetPooled()) _element.SimulateEvent(down);

            // Act
            manipulator.UpdateClasses(new[] { "new-hover" }, new[] { "new-tap" }, Array.Empty<string>());

            // Assert
            Assert.That(
                (_element.ClassListContains("old-hover"), _element.ClassListContains("old-tap"),
                    _element.ClassListContains("new-hover"), _element.ClassListContains("new-tap")),
                Is.EqualTo((false, false, true, true)));
        }

        // GREEN_ON_BASE(characterization): the base swaps the classes on the class list itself.
        // What reddens it is UpdateClasses leaving the old focus classes on, or not putting the new ones on.
        [Test]
        public void Given_FocusedElement_When_FocusClassesUpdated_Then_SwapsOldFocusForNew()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                Array.Empty<string>(), Array.Empty<string>(), new[] { "old-focus" });
            _element.AddManipulator(manipulator);
            using (var focus = FocusEvent.GetPooled()) _element.SimulateEvent(focus);

            // Act
            manipulator.UpdateClasses(Array.Empty<string>(), Array.Empty<string>(), new[] { "new-focus" });

            // Assert
            Assert.That(
                (_element.ClassListContains("old-focus"), _element.ClassListContains("new-focus")),
                Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): the base takes the classes off the class list itself.
        // What reddens it is the manipulator's removal leaving one state's classes on.
        [Test]
        public void Given_AHoveredTappedAndFocusedElement_When_TheManipulatorIsRemoved_Then_EveryStatesClassesGo()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(new[] { "hovered" }, new[] { "tapped" },
                new[] { "focused" });
            _element.AddManipulator(manipulator);
            using (var over = PointerOverEvent.GetPooled()) _element.SimulateEvent(over);
            using (var down = PointerDownEvent.GetPooled()) _element.SimulateEvent(down);
            using (var focus = FocusEvent.GetPooled()) _element.SimulateEvent(focus);
            // Whether every state came on, since a state that never did removes nothing.
            var on = new[] { "hovered", "tapped", "focused" }.All(_element.ClassListContains);

            // Act
            _element.RemoveManipulator(manipulator);

            // Assert — read as the three states where they never all came on, which is not empty.
            Assert.That(on ? new[] { "hovered", "tapped", "focused" }.Where(_element.ClassListContains)
                : new[] { "hovered", "tapped", "focused" }, Is.Empty);
        }

        [Test]
        public void Given_NullClassArrays_When_ManipulatorConstructedAndAttached_Then_DoesNotThrow()
        {
            // Act + Assert
            Assert.DoesNotThrow(() =>
            {
                var manipulator = new StyleGestureClassManipulator(null, null, null);
                _element.AddManipulator(manipulator);
            });
        }

        #endregion

        #region Signal-Driven Edge Detection (ElementLocalVariantSignals wiring)

        [Test]
        public void Given_UnhoveredElement_When_PointerOverFires_Then_HoverClassIsApplied()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(new[] { "hover-glow" }, Array.Empty<string>(), Array.Empty<string>());
            _element.AddManipulator(manipulator);
            Assume.That(_element.ClassListContains("hover-glow"), Is.False, "Precondition: hover class not yet applied");

            // Act
            using (var evt = PointerOverEvent.GetPooled()) _element.SimulateEvent(evt);

            // Assert
            Assert.That(_element.ClassListContains("hover-glow"), Is.True);
        }

        [Test]
        public void Given_HoveredElement_When_PointerLeavesItsBounds_Then_HoverClassIsRemoved()
        {
            // Arrange — a detached element's worldBound is Rect.zero, so a default-position PointerOut never
            // reads as "still inside" and is treated as a real leave.
            var manipulator = new StyleGestureClassManipulator(new[] { "hover-glow" }, Array.Empty<string>(), Array.Empty<string>());
            _element.AddManipulator(manipulator);
            using (var over = PointerOverEvent.GetPooled()) _element.SimulateEvent(over);
            Assume.That(_element.ClassListContains("hover-glow"), Is.True, "Precondition: hover class applied while hovered");

            // Act
            using (var evt = PointerOutEvent.GetPooled()) _element.SimulateEvent(evt);

            // Assert
            Assert.That(_element.ClassListContains("hover-glow"), Is.False);
        }

        [Test]
        public void Given_UntappedElement_When_PointerDownFires_Then_TapClassIsApplied()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(Array.Empty<string>(), new[] { "tap-shrink" }, Array.Empty<string>());
            _element.AddManipulator(manipulator);
            Assume.That(_element.ClassListContains("tap-shrink"), Is.False, "Precondition: tap class not yet applied");

            // Act
            using (var evt = PointerDownEvent.GetPooled()) _element.SimulateEvent(evt);

            // Assert
            Assert.That(_element.ClassListContains("tap-shrink"), Is.True);
        }

        [Test]
        public void Given_TappedElement_When_PointerUpFires_Then_TapClassIsRemoved()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(Array.Empty<string>(), new[] { "tap-shrink" }, Array.Empty<string>());
            _element.AddManipulator(manipulator);
            using (var down = PointerDownEvent.GetPooled()) _element.SimulateEvent(down);
            Assume.That(_element.ClassListContains("tap-shrink"), Is.True, "Precondition: tap class applied while pressed");

            // Act
            using (var evt = PointerUpEvent.GetPooled()) _element.SimulateEvent(evt);

            // Assert
            Assert.That(_element.ClassListContains("tap-shrink"), Is.False);
        }

        [Test]
        public void Given_TappedElement_When_PointerIsCancelled_Then_TapClassIsRemoved()
        {
            // Arrange — a cancelled gesture (e.g. an OS-level interruption) ends the tap state just like a release.
            var manipulator = new StyleGestureClassManipulator(Array.Empty<string>(), new[] { "tap-shrink" }, Array.Empty<string>());
            _element.AddManipulator(manipulator);
            using (var down = PointerDownEvent.GetPooled()) _element.SimulateEvent(down);
            Assume.That(_element.ClassListContains("tap-shrink"), Is.True, "Precondition: tap class applied while pressed");

            // Act
            using (var evt = PointerCancelEvent.GetPooled()) _element.SimulateEvent(evt);

            // Assert
            Assert.That(_element.ClassListContains("tap-shrink"), Is.False);
        }

        [Test]
        public void Given_UnfocusedElement_When_FocusEventFires_Then_FocusClassIsApplied()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(Array.Empty<string>(), Array.Empty<string>(), new[] { "focus-ring" });
            _element.AddManipulator(manipulator);
            Assume.That(_element.ClassListContains("focus-ring"), Is.False, "Precondition: focus class not yet applied");

            // Act
            using (var evt = FocusEvent.GetPooled()) _element.SimulateEvent(evt);

            // Assert
            Assert.That(_element.ClassListContains("focus-ring"), Is.True);
        }

        [Test]
        public void Given_FocusedElement_When_BlurEventFires_Then_FocusClassIsRemoved()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(Array.Empty<string>(), Array.Empty<string>(), new[] { "focus-ring" });
            _element.AddManipulator(manipulator);
            using (var focus = FocusEvent.GetPooled()) _element.SimulateEvent(focus);
            Assume.That(_element.ClassListContains("focus-ring"), Is.True, "Precondition: focus class applied while focused");

            // Act
            using (var evt = BlurEvent.GetPooled()) _element.SimulateEvent(evt);

            // Assert
            Assert.That(_element.ClassListContains("focus-ring"), Is.False);
        }

        #endregion

        #region ParseClassNames

        [Test]
        public void Given_SpaceSeparatedString_When_Parsed_Then_ReturnsTokenArray()
        {
            // Act
            var result = V.ParseClassNames("hover-glow hover-scale");

            // Assert
            Assert.That(result, Is.EqualTo(new[] { "hover-glow", "hover-scale" }));
        }

        #endregion

        #region Reconciler Integration

        [Test]
        public void Given_ElementWithGestureClasses_When_Reconciled_Then_RegistersOneManipulator()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(whileHoverClass: "hover-glow", whileTapClass: "tap-shrink"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetGestureManipulatorCount(scope.Reconciler), Is.EqualTo(1));
        }

        [Test]
        public void Given_NonMotionButtonWithGestureClasses_When_Reconciled_Then_RegistersOneManipulator()
        {
            // Arrange — a plain Button (not a Motion) carrying gesture classes.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Button(text: "Press", whileHoverClass: "scale-105", whileTapClass: "scale-95"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert — the gesture manipulator is attached through the same path Motion uses.
            Assert.That(GetGestureManipulatorCount(scope.Reconciler), Is.EqualTo(1));
        }

        [Test]
        public void Given_ElementWithWhileFocusClass_When_Reconciled_Then_RegistersOneManipulator()
        {
            // Arrange — an element declaring only a whileFocus gesture class.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Button(text: "Press", whileFocusClass: "focus-ring"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert — the gesture manipulator is attached (whileFocus alone is enough).
            Assert.That(GetGestureManipulatorCount(scope.Reconciler), Is.EqualTo(1));
        }

        [Test]
        public void Given_ElementWithoutGestureClasses_When_Reconciled_Then_RegistersNoManipulator()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "plain"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetGestureManipulatorCount(scope.Reconciler), Is.EqualTo(0));
        }

        [Test]
        public void Given_RegisteredManipulator_When_GestureClassesPatchedAway_Then_ManipulatorIsRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { V.Div(whileHoverClass: "hover-glow") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree1);
            Assume.That(GetGestureManipulatorCount(scope.Reconciler), Is.EqualTo(1),
                "Precondition: the gesture class registered a manipulator");

            // Act
            var tree2 = new VNode[] { V.Div() };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(GetGestureManipulatorCount(scope.Reconciler), Is.EqualTo(0));
        }

        #endregion

        #region Helpers

        private static int GetGestureManipulatorCount(Reconciler reconciler)
        {
            var ctxField = typeof(Reconciler).GetField(
                "_ctx",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(ctxField, Is.Not.Null, "_ctx field not found");
            var ctx = ctxField.GetValue(reconciler);
            var prop = ctx.GetType().GetProperty("GestureManipulators");
            Assert.That(prop, Is.Not.Null, "GestureManipulators property not found");
            var dict = prop.GetValue(ctx) as System.Collections.IDictionary;
            return dict?.Count ?? 0;
        }

        #endregion
    }

    /// <summary>
    /// A gesture class against the element's own base utilities on a real panel: it wins while its state is on,
    /// whichever way the base realises the property, as Tailwind's variant rule wins over the base one, and the
    /// base takes the property back when the state goes off.
    /// </summary>
    [TestFixture]
    internal sealed class GesturePrecedencePanelTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        // The middle child of a row of rowClass, carrying baseClass and the gesture classes, beside a reference
        // element carrying reference alone, whose resolved color is the one the gesture should give.
        private (VisualElement Child, VisualElement Reference) Mount(string rowClass, string baseClass,
            string? hover = null, string? tap = null, string reference = "", string? focus = null)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Div(className: "flex flex-row " + rowClass, children: new VNode[]
                {
                    V.Div(className: "w-[20px] h-[20px]"),
                    V.Div(name: "b", className: "w-[20px] h-[20px] " + baseClass, whileHoverClass: hover,
                        whileTapClass: tap, whileFocusClass: focus),
                    V.Div(className: "w-[20px] h-[20px]"),
                }),
                V.Div(name: "ref", className: "border " + reference),
            }));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);
            return (child, _window.rootVisualElement.Q("ref"));
        }

        private static void Hover(VisualElement element, bool on)
        {
            if (on)
            {
                using var over = PointerOverEvent.GetPooled();
                element.SimulateEvent(over);
            }
            else
            {
                using var leave = PointerOutEvent.GetPooled();
                element.SimulateEvent(leave);
            }
            ForcePanelUpdate(element.panel);
        }

        // The pointer over child and away again.
        private static void HoverAndLeave(VisualElement child)
        {
            Hover(child, true);
            Hover(child, false);
        }

        [Test]
        public void Given_ABaseColorClassTheStylesheetDeclaresLater_When_Hovered_Then_TheHoverClassWins()
        {
            // Arrange — bg-blue-500 sits after bg-red-500 in the bundled stylesheet.
            var (child, reference) = Mount("", "bg-blue-500", hover: "bg-red-500", reference: "bg-red-500");

            // Act
            Hover(child, true);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(reference.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_ABaseColorClassTheStylesheetDeclaresLater_When_ThePointerLeaves_Then_TheBaseColorComesBack()
        {
            // Arrange
            var (child, _) = Mount("", "bg-blue-500", hover: "bg-red-500", reference: "bg-red-500");
            var resting = child.resolvedStyle.backgroundColor;

            // Act
            HoverAndLeave(child);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(resting));
        }

        [Test]
        public void Given_ABaseBracketColor_When_Hovered_Then_TheHoverClassWins()
        {
            // Arrange
            var (child, reference) = Mount("", "bg-[#0000ff]", hover: "bg-red-500", reference: "bg-red-500");

            // Act
            Hover(child, true);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(reference.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_ABaseBracketColor_When_ThePointerLeaves_Then_TheBracketColorComesBack()
        {
            // Arrange
            var (child, _) = Mount("", "bg-[#0000ff]", hover: "bg-red-500", reference: "bg-red-500");

            // Act
            HoverAndLeave(child);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(Color.blue));
        }

        private static object Read(VisualElement element, string property) => property switch
        {
            "width" => element.resolvedStyle.width,
            "paddingTop" => element.resolvedStyle.paddingTop,
            "backgroundColor" => element.resolvedStyle.backgroundColor,
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
        };

        // The row's w-[20px] is the bracket width the w-8 case contends with.
        [TestCase("", "w-8", "width")]
        [TestCase("p-[10px]", "p-4", "paddingTop")]
        public void Given_ABaseBracketValue_When_AHoverClassOfItsFamilyHolds_Then_ItWinsWhileHovered(
            string baseClass, string hover, string property)
        {
            // Arrange
            var (child, reference) = Mount("", baseClass, hover: hover, reference: hover);

            // Act
            Hover(child, true);

            // Assert
            Assert.That(Read(child, property), Is.EqualTo(Read(reference, property)));
        }

        [TestCase("", "w-8", "width")]
        [TestCase("p-[10px]", "p-4", "paddingTop")]
        public void Given_ABaseBracketValue_When_AHoverClassOfItsFamilyHolds_Then_TheBaseComesBackWhenThePointerLeaves(
            string baseClass, string hover, string property)
        {
            // Arrange
            var (child, _) = Mount("", baseClass, hover: hover, reference: hover);
            var resting = Read(child, property);

            // Act
            HoverAndLeave(child);

            // Assert
            Assert.That(Read(child, property), Is.EqualTo(resting));
        }

        // GREEN_ON_BASE(characterization): the base already lets a className hover: class outrank a bracket base value.
        // The case holds the same verdict for the className's own hover: rule that the gesture channel now shares.
        [TestCase("", "w-8", "width")]
        [TestCase("p-[10px]", "p-4", "paddingTop")]
        [TestCase("bg-[#0000ff]", "bg-red-500", "backgroundColor")]
        public void Given_ABaseBracketValue_When_AClassNameHoverRuleOfItsFamilyHolds_Then_ItWinsWhileHovered(
            string baseClass, string hover, string property)
        {
            // Arrange
            var (child, reference) = Mount("", baseClass + " hover:" + hover, reference: hover);

            // Act
            Hover(child, true);

            // Assert
            Assert.That(Read(child, property), Is.EqualTo(Read(reference, property)));
        }

        // GREEN_ON_BASE(characterization): the base already lets a className hover: class outrank a bracket base value.
        // The case holds the same verdict for the className's own hover: rule that the gesture channel now shares.
        [TestCase("", "w-8", "width")]
        [TestCase("p-[10px]", "p-4", "paddingTop")]
        [TestCase("bg-[#0000ff]", "bg-red-500", "backgroundColor")]
        public void Given_ABaseBracketValue_When_AClassNameHoverRuleOfItsFamilyHolds_Then_TheBaseComesBackWhenThePointerLeaves(
            string baseClass, string hover, string property)
        {
            // Arrange
            var (child, _) = Mount("", baseClass + " hover:" + hover, reference: hover);
            var resting = Read(child, property);

            // Act
            HoverAndLeave(child);

            // Assert
            Assert.That(Read(child, property), Is.EqualTo(resting));
        }

        [Test]
        public void Given_AHoverBracketColor_When_Hovered_Then_ItApplies()
        {
            // Arrange
            var (child, _) = Mount("", "bg-blue-500", hover: "bg-[#ff0000]");

            // Act
            Hover(child, true);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AHoverBracketColor_When_ThePointerLeaves_Then_TheRestingColorComesBack()
        {
            // Arrange
            var (child, _) = Mount("", "bg-blue-500", hover: "bg-[#ff0000]");
            var resting = child.resolvedStyle.backgroundColor;

            // Act
            HoverAndLeave(child);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(resting));
        }

        [Test]
        public void Given_ADividedChild_When_AHoverClassGivesItABorderColor_Then_ItWinsOverTheDivideColor()
        {
            // Arrange
            var (child, reference) = Mount("divide-x divide-gray-200", "", hover: "border-red-500",
                reference: "border-red-500");

            // Act
            Hover(child, true);

            // Assert
            Assert.That(child.resolvedStyle.borderRightColor, Is.EqualTo(reference.resolvedStyle.borderRightColor));
        }

        [Test]
        public void Given_ADividedChild_When_AHoverClassGivesItABorderColor_Then_TheDivideColorComesBackWhenThePointerLeaves()
        {
            // Arrange
            var (child, _) = Mount("divide-x divide-gray-200", "", hover: "border-red-500", reference: "border-red-500");
            var resting = child.resolvedStyle.borderRightColor;

            // Act
            HoverAndLeave(child);

            // Assert
            Assert.That(child.resolvedStyle.borderRightColor, Is.EqualTo(resting));
        }

        [Test]
        public void Given_AHoveredAndTappedChild_When_BothClassesSetTheColor_Then_TheTapClassWins()
        {
            // Arrange — active: sits after hover: in Tailwind's variant order, where the bundled stylesheet puts
            // the hover class's bg-blue-500 after the tap class's bg-red-500.
            var (child, reference) = Mount("", "", hover: "bg-blue-500", tap: "bg-red-500", reference: "bg-red-500");
            Hover(child, true);

            // Act
            using (var down = PointerDownEvent.GetPooled())
            {
                child.SimulateEvent(down);
            }
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(reference.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_AHoveredAndFocusedChild_When_BothClassesSetTheColor_Then_TheFocusClassWins()
        {
            // Arrange — focus: sits after hover: in Tailwind's variant order, as the tap case's classes do.
            var (child, reference) = Mount("", "", hover: "bg-blue-500", focus: "bg-red-500", reference: "bg-red-500");
            Hover(child, true);

            // Act
            using (var focus = FocusEvent.GetPooled())
            {
                child.SimulateEvent(focus);
            }
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(reference.resolvedStyle.backgroundColor));
        }

        // Whether the reconciler holds a binding of the named paint for child.
        private static bool Painted(ReconcilerContext ctx, VisualElement child, string paint) => paint switch
        {
            "shadow" => ctx.ShadowBindings.ContainsKey(child),
            "ring" => ctx.RingBindings.ContainsKey(child),
            "skew" => ctx.SkewBindings.ContainsKey(child) || ctx.SkewTransformed.ContainsKey(child),
            "gradient" => ctx.GradientBackgrounds.ContainsKey(child),
            "animation" => ctx.AnimationBindings.ContainsKey(child),
            "dash" => ctx.BorderStyleBindings.ContainsKey(child),
            _ => throw new ArgumentOutOfRangeException(nameof(paint), paint, null),
        };

        [TestCase("shadow-lg", "shadow")]
        [TestCase("ring-2", "ring")]
        [TestCase("skew-x-6", "skew")]
        [TestCase("bg-gradient-to-r from-red-500 to-blue-500", "gradient")]
        [TestCase("animate-pulse", "animation")]
        [TestCase("border-2 border-dashed", "dash")]
        public void Given_APaintingHoverClass_When_Hovered_Then_ThePaintIsBound(string hover, string paint)
        {
            // Arrange
            var (child, _) = Mount("", "bg-white", hover: hover);

            // Act
            Hover(child, true);

            // Assert
            Assert.That(Painted(_mounted.Root.Reconciler.Context, child, paint), Is.True);
        }

        [TestCase("shadow-lg", "shadow")]
        [TestCase("ring-2", "ring")]
        [TestCase("skew-x-6", "skew")]
        [TestCase("bg-gradient-to-r from-red-500 to-blue-500", "gradient")]
        [TestCase("animate-pulse", "animation")]
        [TestCase("border-2 border-dashed", "dash")]
        public void Given_APaintingHoverClass_When_ThePointerLeaves_Then_ThePaintGoes(string hover, string paint)
        {
            // Arrange
            var (child, _) = Mount("", "bg-white", hover: hover);
            Hover(child, true);
            // Whether the paint bound while hovered, since a paint never bound has nothing to go.
            var shown = Painted(_mounted.Root.Reconciler.Context, child, paint);

            // Act
            Hover(child, false);

            // Assert — read as null where the paint never bound, which is not false.
            Assert.That(shown ? Painted(_mounted.Root.Reconciler.Context, child, paint) : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AShadowTapClass_When_Pressed_Then_TheShadowPaints()
        {
            // Arrange
            var (child, _) = Mount("", "bg-white", tap: "shadow-lg");

            // Act
            using (var down = PointerDownEvent.GetPooled())
            {
                child.SimulateEvent(down);
            }
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(child), Is.True);
        }

        [Test]
        public void Given_AShadowFocusClass_When_Focused_Then_TheShadowPaints()
        {
            // Arrange
            var (child, _) = Mount("", "bg-white", focus: "shadow-lg");

            // Act
            using (var focus = FocusEvent.GetPooled())
            {
                child.SimulateEvent(focus);
            }
            ForcePanelUpdate(child.panel);

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(child), Is.True);
        }

        [Test]
        public void Given_AHoverRuleOfTheClassName_When_AHoverClassSetsTheSameColor_Then_TheHoverClassWins()
        {
            // Arrange — the bundled stylesheet puts the className's bg-blue-500 after the gesture's bg-red-500.
            var (child, reference) = Mount("", "hover:bg-blue-500", hover: "bg-red-500", reference: "bg-red-500");

            // Act
            Hover(child, true);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(reference.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_AHoverBracketOfTheClassName_When_AHoverBracketSetsTheSameColor_Then_TheHoverClassWins()
        {
            // Arrange
            var (child, _) = Mount("", "hover:bg-[#0000ff]", hover: "bg-[#ff0000]");

            // Act
            Hover(child, true);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(Color.red));
        }

        private static StateUpdater<bool> s_setLit;

        // A Motion entering from bg-blue-500 to bg-red-500 on a 0.2 s tween, carrying className and hover, beside a
        // reference carrying bg-red-500, on a panel whose scheduler runs on the fake clock.
        private (VisualElement Child, VisualElement Reference) MountEnteringMotion(string className, string? hover)
        {
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, UseFrameFakeClockHost.ReadFakeClock);
            var poses = new Dictionary<string, MotionVariant> { ["hidden"] = "bg-blue-500", ["visible"] = "bg-red-500" };
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Motion(name: "b", className: "w-[20px] h-[20px] " + className, variants: poses, initial: "hidden",
                    animate: "visible", transition: new StyleTransitionConfig { DurationSec = 0.2f }, whileHoverClass: hover),
                V.Div(name: "ref", className: "border bg-red-500"),
            }));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);
            return (child, _window.rootVisualElement.Q("ref"));
        }

        // The play's class swap, then its completion, each a second later on the fake clock.
        private static void PlayOut(VisualElement child)
        {
            for (var i = 0; i < 2; i++)
            {
                UseFrameFakeClockHost.Ms += 1000;
                EditorPanelTestHelpers.DriveSchedulerOnce(child.panel);
            }
            ForcePanelUpdate(child.panel);
        }

        // GREEN_ON_BASE(characterization): the base's gesture class builds no class projection, so nothing adopts the
        // play's starting class. What reddens it is a play's removal leaving that class adopted at the base layer.
        [Test]
        public void Given_AnEnteringMotionHoveredBeforeItsSwap_When_ThePlayEndsAndThePointerLeaves_Then_ItShowsTheEnteredColor()
        {
            // Arrange
            var (child, reference) = MountEnteringMotion("", "bg-green-500");
            Hover(child, true);

            // Act
            PlayOut(child);
            Hover(child, false);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(reference.resolvedStyle.backgroundColor));
        }

        [Test]
        public void Given_AnEnteringMotionWithAHoverRuleHoveredBeforeItsSwap_When_ThePlayEndsAndThePointerLeaves_Then_ItShowsTheEnteredColor()
        {
            // Arrange
            var (child, reference) = MountEnteringMotion("hover:bg-green-500", null);
            Hover(child, true);

            // Act
            PlayOut(child);
            Hover(child, false);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor, Is.EqualTo(reference.resolvedStyle.backgroundColor));
        }

        // A Motion that swaps from bg-white to bg-blue-500 on a 0.2 s tween when s_setLit says so, and is red while
        // hovered, beside a reference carrying bg-red-500.
        [Component]
        private static VNode SwappingMotion()
        {
            var (lit, setLit) = Hooks.UseState(false);
            s_setLit = setLit;
            var poses = new Dictionary<string, MotionVariant> { ["a"] = "bg-white", ["b"] = "bg-blue-500" };
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "b", className: "w-[20px] h-[20px]", variants: poses, animate: lit ? "b" : "a",
                    transition: new StyleTransitionConfig { DurationSec = 0.2f }, whileHoverClass: "bg-red-500"),
                V.Div(name: "ref", className: "border bg-red-500"),
            });
        }

        [Test]
        public void Given_AHoveredMotion_When_AVariantSwapPlaysOut_Then_TheHoverClassStillWins()
        {
            // Arrange
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_window.rootVisualElement.panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_window.rootVisualElement, V.Component(SwappingMotion));
            var child = _window.rootVisualElement.Q("b");
            ForcePanelUpdate(child.panel);
            Hover(child, true);

            // Act
            s_setLit.Invoke(true);
            _mounted.FlushStateForTest();
            PlayOut(child);

            // Assert
            Assert.That(child.resolvedStyle.backgroundColor,
                Is.EqualTo(_window.rootVisualElement.Q("ref").resolvedStyle.backgroundColor));
        }
    }

    /// <summary>
    /// A background colour a class change brings in outside a reconcile pass — a variant payload, a gesture
    /// class, a theme flip, a play — on an element whose shadow or skew Velvet paints. Both layers repaint the
    /// element's face, so the face has to take the colour the background would resolve to.
    /// </summary>
    internal sealed class SilhouetteFaceColorPanelTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
            // The sheet's dark token set applies under the class this binding puts on the root.
            VelvetStyleUtilities.BindThemeTo(_window.rootVisualElement);
        }

        public override void TearDown()
        {
            VelvetTheme.IsDark = false;
            base.TearDown();
        }

        // A card carrying className and the hover class, beside a reference carrying reference alone, whose
        // resolved background is the one the card should show. A geometry event is delivered as a live host's
        // layout delivers it, since that is where a skew layer on a panel takes its first stash.
        private (VisualElement Card, Color Expected) Mount(string className, string reference, string? hover = null,
            string name = "card")
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Div(name: name, className: "w-[40px] h-[40px] " + className, whileHoverClass: hover),
                V.Div(name: "ref", className: "w-[40px] h-[40px] " + reference),
            }));
            var card = _window.rootVisualElement.Q(name);
            ForcePanelUpdate(card.panel);
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                card.SimulateEvent(evt);
            }
            return (card, _window.rootVisualElement.Q("ref").resolvedStyle.backgroundColor);
        }

        private static void Hover(VisualElement element)
        {
            using (var over = PointerOverEvent.GetPooled())
            {
                element.SimulateEvent(over);
            }
            ForcePanelUpdate(element.panel);
        }

        private Color SkewFace(VisualElement card) => _mounted.Root.Reconciler.Context.SkewBindings[card].BgColor;

        private Color ShadowFace(VisualElement card)
            => _mounted.Root.Reconciler.Context.ShadowBindings[card].Face.CurrentBg;

        private const string IdRuleSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ShadowFaceIdRule.uss";

        private static readonly FieldInfo s_carries =
            typeof(SilhouetteFaceStash).GetField("_carries", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // The rules are read again only once the rules matched to the element change (StyleCascade.RulesHash), which
        // the panel's check asks each frame; a colour written over the carried one stands until then.
        [Test]
        public void Given_AShadowedCardWhoseRulesStayTheSame_When_ThePanelChecksItsFace_Then_ItsColorsAreNotReadAgain()
        {
            // Arrange
            var (card, _) = Mount("bg-blue-500 shadow-md", "");
            var face = _mounted.Root.Reconciler.Context.ShadowBindings[card].Face;
            // A read first, which reads the rules this pass resolved; a colour then written over the carried one.
            _ = face.CurrentBg;
            ((ColorCarry[])s_carries.GetValue(face))[0].Land(Color.magenta);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);

            // Assert
            Assert.That(face.CurrentBg, Is.EqualTo(Color.magenta));
        }

        // GREEN_ON_BASE(characterization): the base captures the face from the element's resolved style, an id rule's
        // colour included. Reading the face off an element that copies the shadowed one's classes reddens this case.
        [Test]
        public void Given_AShadowedElementAnIdRuleColors_When_ThePanelResolves_Then_ItsFaceTakesTheRulesColor()
        {
            // Arrange
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(IdRuleSheet);
            Assume.That(sheet, Is.Not.Null, "Precondition: the test's id rule sheet loads");
            _window.rootVisualElement.styleSheets.Add(sheet);

            // Act
            var (card, _) = Mount("shadow-md", "", name: "buy");

            // Assert
            Assert.That(ShadowFace(card), Is.EqualTo(new Color(0f, 128f / 255f, 0f)));
        }

        [Test]
        public void Given_AShadowedCardWithAHoverBackground_When_Hovered_Then_ItsFaceTakesTheHoverColor()
        {
            // Arrange
            var (card, expected) = Mount("bg-white hover:bg-gray-50 shadow-md", "bg-gray-50");

            // Act
            Hover(card);

            // Assert
            Assert.That(ShadowFace(card), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AShadowedCardWithAHoverClassBackground_When_Hovered_Then_ItsFaceTakesTheHoverColor()
        {
            // Arrange
            var (card, expected) = Mount("bg-blue-500 shadow-md", "bg-red-500", hover: "bg-red-500");

            // Act
            Hover(card);

            // Assert
            Assert.That(ShadowFace(card), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AShadowedCardWithADarkBackground_When_TheThemeTurnsDark_Then_ItsFaceTakesTheDarkColor()
        {
            // Arrange
            var (card, expected) = Mount("bg-blue-500 dark:bg-red-500 shadow-md", "bg-red-500");

            // Act
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(ShadowFace(card), Is.EqualTo(expected));
        }

        // The theme's colours are custom properties the root sets, which the card inherits: its matched rules stay the
        // same, and the face is read again because StyleCascade.RulesHash keys on the inherited variables too.
        [Test]
        public void Given_AShadowedCardWithAThemeBackground_When_TheThemeTurnsDark_Then_ItsFaceTakesTheDarkColor()
        {
            // Arrange
            var (card, light) = Mount("bg-background shadow-md", "bg-background");

            // Act
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(card.panel);
            var dark = _window.rootVisualElement.Q("ref").resolvedStyle.backgroundColor;

            // Assert — NaN where the theme gives both the same colour, which a face that never moved would match.
            Assert.That(ShadowFace(card), Is.EqualTo(dark != light ? dark : new Color(float.NaN, 0f, 0f, 0f)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; only a rotate class was added so it still reaches the painted skew layer.
        [Test]
        public void Given_ASkewedCardWithAHoverBackground_When_Hovered_Then_ItsFaceTakesTheHoverColor()
        {
            // Arrange
            var (card, expected) = Mount("bg-blue-500 hover:bg-red-500 -skew-x-6 rotate-0", "bg-red-500");

            // Act
            Hover(card);

            // Assert
            Assert.That(SkewFace(card), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; only a rotate class was added so it still reaches the painted skew layer.
        [Test]
        public void Given_ASkewedCardWithAHoverClassBackground_When_Hovered_Then_ItsFaceTakesTheHoverColor()
        {
            // Arrange
            var (card, expected) = Mount("bg-blue-500 -skew-x-6 rotate-0", "bg-red-500", hover: "bg-red-500");

            // Act
            Hover(card);

            // Assert
            Assert.That(SkewFace(card), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; only a rotate class was added so it still reaches the painted skew layer.
        [Test]
        public void Given_ASkewedCardWithADarkBackground_When_TheThemeTurnsDark_Then_ItsFaceTakesTheDarkColor()
        {
            // Arrange
            var (card, expected) = Mount("bg-blue-500 dark:bg-red-500 -skew-x-6 rotate-0", "bg-red-500");

            // Act
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(SkewFace(card), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AShadowedCard_When_APlayShowsABackgroundClass_Then_ItsFaceTakesThePlaysColor()
        {
            // Arrange
            var (card, expected) = Mount("bg-blue-500 shadow-md", "bg-red-500");

            // Act — the class writer every play goes through.
            StyleAnimationClassUtils.AddClasses(card, new[] { "bg-red-500" }, StyleLayerPriority.Animation);
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(ShadowFace(card), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; only a rotate class was added so it still reaches the painted skew layer.
        [Test]
        public void Given_ASkewedCard_When_APlayShowsABackgroundClass_Then_ItsFaceTakesThePlaysColor()
        {
            // Arrange
            var (card, expected) = Mount("bg-blue-500 -skew-x-6 rotate-0", "bg-red-500");

            // Act — the class writer every play goes through.
            StyleAnimationClassUtils.AddClasses(card, new[] { "bg-red-500" }, StyleLayerPriority.Animation);
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(SkewFace(card), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AMotionWithAShadowHoverClass_When_Hovered_Then_TheShadowPaints()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                V.Motion(name: "card", className: "w-[40px] h-[40px] bg-blue-500", whileHoverClass: "shadow-lg"));
            var card = _window.rootVisualElement.Q("card");
            ForcePanelUpdate(card.panel);

            // Act
            Hover(card);

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.ShadowBindings.ContainsKey(card), Is.True);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; only a rotate class was added so it still reaches the painted skew layer.
        [Test]
        public void Given_ASkewedCardWithABracketHoverClassBackground_When_Hovered_Then_ItsFaceTakesTheHoverColor()
        {
            // Arrange — a bracket value is written inline over the face's suppression, which the face reads
            // directly rather than through its probe.
            var (card, _) = Mount("bg-[#0000ff] -skew-x-6 rotate-0", "", hover: "bg-[#ff0000]");

            // Act
            Hover(card);

            // Assert
            Assert.That(SkewFace(card), Is.EqualTo(Color.red));
        }
    }
}
