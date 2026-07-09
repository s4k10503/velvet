using System;
using System.Reflection;
using NUnit.Framework;
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
    /// <item><c>ParseClassNames</c> splits a space-separated string into its tokens and returns an empty array for
    /// a null or empty string.</item>
    /// <item>Reconciliation registers one manipulator per element that declares gesture classes, registers none
    /// for an element without them, and removes the manipulator when the gesture classes are patched away.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The manipulator's hover/tap state is private, so the unit tests set <c>_isHovered</c> / <c>_isTapped</c>
    /// via reflection (<see cref="SetPrivateField"/>) and the reconciler tests read the internal
    /// <c>GestureManipulators</c> dictionary via reflection (<see cref="GetGestureManipulatorCount"/>).
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

        [Test]
        public void Given_HoveredElement_When_HoverClassesUpdated_Then_SwapsOldHoverForNew()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                new[] { "old-hover" }, Array.Empty<string>(), Array.Empty<string>());
            _element.AddManipulator(manipulator);
            SetPrivateField(manipulator, "_isHovered", true);
            _element.AddToClassList("old-hover");

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

        [Test]
        public void Given_TappedElement_When_TapClassesUpdated_Then_SwapsOldTapForNew()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                Array.Empty<string>(), new[] { "old-tap" }, Array.Empty<string>());
            _element.AddManipulator(manipulator);
            SetPrivateField(manipulator, "_isTapped", true);
            _element.AddToClassList("old-tap");

            // Act
            manipulator.UpdateClasses(Array.Empty<string>(), new[] { "new-tap" }, Array.Empty<string>());

            // Assert
            Assert.That(
                (_element.ClassListContains("old-tap"), _element.ClassListContains("new-tap")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_HoveredAndTappedElement_When_BothClassesUpdated_Then_SwapsBothStatesClasses()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                new[] { "old-hover" }, new[] { "old-tap" }, Array.Empty<string>());
            _element.AddManipulator(manipulator);
            SetPrivateField(manipulator, "_isHovered", true);
            SetPrivateField(manipulator, "_isTapped", true);
            _element.AddToClassList("old-hover");
            _element.AddToClassList("old-tap");

            // Act
            manipulator.UpdateClasses(new[] { "new-hover" }, new[] { "new-tap" }, Array.Empty<string>());

            // Assert
            Assert.That(
                (_element.ClassListContains("old-hover"), _element.ClassListContains("old-tap"),
                    _element.ClassListContains("new-hover"), _element.ClassListContains("new-tap")),
                Is.EqualTo((false, false, true, true)));
        }

        [Test]
        public void Given_FocusedElement_When_FocusClassesUpdated_Then_SwapsOldFocusForNew()
        {
            // Arrange
            var manipulator = new StyleGestureClassManipulator(
                Array.Empty<string>(), Array.Empty<string>(), new[] { "old-focus" });
            _element.AddManipulator(manipulator);
            SetPrivateField(manipulator, "_isFocused", true);
            _element.AddToClassList("old-focus");

            // Act
            manipulator.UpdateClasses(Array.Empty<string>(), Array.Empty<string>(), new[] { "new-focus" });

            // Assert
            Assert.That(
                (_element.ClassListContains("old-focus"), _element.ClassListContains("new-focus")),
                Is.EqualTo((false, true)));
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

        [Test]
        public void Given_NullString_When_Parsed_Then_ReturnsEmptyArray()
        {
            // Act
            var result = V.ParseClassNames(null);

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Given_EmptyString_When_Parsed_Then_ReturnsEmptyArray()
        {
            // Act
            var result = V.ParseClassNames("");

            // Assert
            Assert.That(result, Is.Empty);
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

        private static void SetPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, $"Field {fieldName} not found");
            field.SetValue(obj, value);
        }

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
}
