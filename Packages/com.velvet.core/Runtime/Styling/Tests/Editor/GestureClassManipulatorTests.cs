using System;
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
            var applied = _element.ClassListContains("hovered") && _element.ClassListContains("tapped")
                && _element.ClassListContains("focused");

            // Act
            _element.RemoveManipulator(manipulator);

            // Assert — the classes applied before ride along, since a state that never came on removes nothing.
            Assert.That((applied, _element.ClassListContains("hovered"), _element.ClassListContains("tapped"),
                _element.ClassListContains("focused")), Is.EqualTo((true, false, false, false)));
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

        // The child's background while hovered, and once the pointer has left again.
        private static (Color Hovered, Color Left) HoverBackground(VisualElement child)
        {
            Hover(child, true);
            var hovered = child.resolvedStyle.backgroundColor;
            Hover(child, false);
            return (hovered, child.resolvedStyle.backgroundColor);
        }

        [Test]
        public void Given_ABaseColorClassTheStylesheetDeclaresLater_When_Hovered_Then_TheHoverClassWins()
        {
            // Arrange — bg-blue-500 sits after bg-red-500 in the bundled stylesheet.
            var (child, reference) = Mount("", "bg-blue-500", hover: "bg-red-500", reference: "bg-red-500");
            var resting = child.resolvedStyle.backgroundColor;

            // Act
            var (hovered, left) = HoverBackground(child);

            // Assert
            Assert.That((hovered, left), Is.EqualTo((reference.resolvedStyle.backgroundColor, resting)));
        }

        [Test]
        public void Given_ABaseBracketColor_When_Hovered_Then_TheHoverClassWins()
        {
            // Arrange
            var (child, reference) = Mount("", "bg-[#0000ff]", hover: "bg-red-500", reference: "bg-red-500");

            // Act
            var (hovered, left) = HoverBackground(child);

            // Assert
            Assert.That((hovered, left), Is.EqualTo((reference.resolvedStyle.backgroundColor, Color.blue)));
        }

        [Test]
        public void Given_AHoverBracketColor_When_Hovered_Then_ItApplies()
        {
            // Arrange
            var (child, _) = Mount("", "bg-blue-500", hover: "bg-[#ff0000]");
            var resting = child.resolvedStyle.backgroundColor;

            // Act
            var (hovered, left) = HoverBackground(child);

            // Assert
            Assert.That((hovered, left), Is.EqualTo((Color.red, resting)));
        }

        [Test]
        public void Given_ADividedChild_When_AHoverClassGivesItABorderColor_Then_ItWinsOverTheDivideColor()
        {
            // Arrange
            var (child, reference) = Mount("divide-x divide-gray-200", "", hover: "border-red-500",
                reference: "border-red-500");
            var resting = child.resolvedStyle.borderRightColor;

            // Act
            Hover(child, true);
            var hovered = child.resolvedStyle.borderRightColor;
            Hover(child, false);

            // Assert
            Assert.That((hovered, child.resolvedStyle.borderRightColor),
                Is.EqualTo((reference.resolvedStyle.borderRightColor, resting)));
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

        // Whether the reconciler holds a paint binding of the given table for a child whose whileHoverClass is
        // hover, while hovered and once the pointer has left.
        private (bool Hovered, bool Left) PaintWhileHovered(string hover,
            System.Func<ReconcilerContext, VisualElement, bool> painted)
        {
            var (child, _) = Mount("", "bg-white", hover: hover);
            var ctx = _mounted.Root.Reconciler.Context;
            Hover(child, true);
            var hovered = painted(ctx, child);
            Hover(child, false);
            return (hovered, painted(ctx, child));
        }

        [Test]
        public void Given_AShadowHoverClass_When_HoveredAndLeft_Then_TheShadowPaintsAndGoes()
        {
            // Act
            var painted = PaintWhileHovered("shadow-lg", (ctx, child) => ctx.ShadowBindings.ContainsKey(child));

            // Assert
            Assert.That(painted, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ARingHoverClass_When_HoveredAndLeft_Then_TheRingPaintsAndGoes()
        {
            // Act
            var painted = PaintWhileHovered("ring-2", (ctx, child) => ctx.RingBindings.ContainsKey(child));

            // Assert
            Assert.That(painted, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASkewHoverClass_When_HoveredAndLeft_Then_TheSkewPaintsAndGoes()
        {
            // Act
            var painted = PaintWhileHovered("skew-x-6", (ctx, child) => ctx.SkewBindings.ContainsKey(child));

            // Assert
            Assert.That(painted, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AGradientHoverClass_When_HoveredAndLeft_Then_TheGradientPaintsAndGoes()
        {
            // Act
            var painted = PaintWhileHovered("bg-gradient-to-r from-red-500 to-blue-500",
                (ctx, child) => ctx.GradientBackgrounds.ContainsKey(child));

            // Assert
            Assert.That(painted, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnAnimateHoverClass_When_HoveredAndLeft_Then_TheAnimationRunsAndStops()
        {
            // Act
            var painted = PaintWhileHovered("animate-pulse", (ctx, child) => ctx.AnimationBindings.ContainsKey(child));

            // Assert
            Assert.That(painted, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADashedBorderHoverClass_When_HoveredAndLeft_Then_TheDashPaintsAndGoes()
        {
            // Act
            var painted = PaintWhileHovered("border-2 border-dashed",
                (ctx, child) => ctx.BorderStyleBindings.ContainsKey(child));

            // Assert
            Assert.That(painted, Is.EqualTo((true, false)));
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
    }
}
