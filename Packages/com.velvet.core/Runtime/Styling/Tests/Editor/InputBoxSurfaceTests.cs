using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where the surface utilities written on a field control land: on the box the control draws its
    /// value in, as they do on an <c>&lt;input&gt;</c> or a <c>&lt;select&gt;</c>, with every other utility left on
    /// the outer control. <see cref="StyleInputBoxSurface"/> rewrites the class list and redirects the payloads a
    /// condition on the control decides; <see cref="StyleChildVariantManipulator"/> applies the rewritten ones.
    /// </summary>
    [TestFixture]
    internal sealed class InputBoxSurfaceTests
    {
        private const string Scoped = "[&>.unity-base-field__input]:";

        private static VisualElement Box(VisualElement field)
            => field.Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass);

        private static string Joined(string[] classNames) => string.Join("|", classNames);

        #region Routing

        [TestCase("bg-red-500")]
        [TestCase("bg-[#ff0000]")]
        [TestCase("bg-gradient-to-r")]
        [TestCase("bg-linear-to-r")]
        [TestCase("from-blue-500")]
        [TestCase("border")]
        [TestCase("border-2")]
        [TestCase("border-slate-600")]
        [TestCase("border-t-[3px]")]
        [TestCase("border-solid")]
        [TestCase("border-dashed")]
        [TestCase("border-dotted")]
        [TestCase("rounded")]
        [TestCase("rounded-lg")]
        [TestCase("rounded-t-lg")]
        [TestCase("p-4")]
        [TestCase("px-2")]
        [TestCase("py-2")]
        [TestCase("pt-1")]
        [TestCase("pr-1")]
        [TestCase("pb-1")]
        [TestCase("pl-1")]
        [TestCase("ps-3")]
        [TestCase("pe-3")]
        [TestCase("shadow-lg")]
        [TestCase("ring-2")]
        [TestCase("outline-2")]
        [TestCase("hover:bg-red-500")]
        [TestCase("focus:border-blue-500")]
        [TestCase("dark:md:p-2")]
        [TestCase("group-hover:bg-red-500")]
        [TestCase("peer-checked:bg-red-500")]
        [TestCase("bg-red-500!")]
        public void Given_ASurfaceUtilityOnATextField_When_TheNodeIsBuilt_Then_ItIsScopedToTheInputBox(string token)
        {
            // Act
            var classNames = V.TextField(className: token).ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo(Scoped + token));
        }

        [TestCase("slice-[12]")]
        [TestCase("slice-t-[3]")]
        [TestCase("slice-scale-[2]")]
        [TestCase("slice-tiled")]
        [TestCase("hover:slice-[4]")]
        public void Given_ANineSliceUtilityOnATextField_When_TheNodeIsBuilt_Then_ItIsScopedToTheInputBox(string token)
        {
            // Act
            var classNames = V.TextField(className: token).ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo(Scoped + token));
        }

        [Test]
        public void Given_SurfaceAndLayoutUtilities_When_TheNodeIsBuilt_Then_OnlyTheSurfaceOnesAreScoped()
        {
            // Act
            var classNames = V.TextField(className: "w-64 bg-red-500 mt-2 rounded-lg").ClassNames;

            // Assert
            Assert.That(Joined(classNames),
                Is.EqualTo("w-64|" + Scoped + "bg-red-500|mt-2|" + Scoped + "rounded-lg"));
        }

        // GREEN_ON_BASE(characterization): the base leaves every one of these on the outer control. They are the
        // utilities that are not surface ones, and the conditions the control itself decides, which
        // StyleInputBoxSurface.DestinationFor redirects while the payload is applied and Route never rewrites.
        [TestCase("w-64")]
        [TestCase("mt-2")]
        [TestCase("text-white")]
        [TestCase("flex")]
        [TestCase("first:bg-red-500")]
        [TestCase("has-[:checked]:bg-red-500")]
        [TestCase("data-[state=open]:bg-red-500")]
        [TestCase("supports-[display:flex]:bg-red-500")]
        [TestCase("[&>*]:bg-red-500")]
        public void Given_ATokenThatIsNotRewritten_When_TheNodeIsBuilt_Then_ItStaysOnTheOuterControl(string token)
        {
            // Act
            var classNames = V.TextField(className: token).ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo(token));
        }

        [Test]
        public void Given_ASurfaceUtilityOnAnIntegerField_When_TheNodeIsBuilt_Then_ItIsScopedToTheInputBox()
        {
            // Act
            var classNames = V.IntegerField(className: "bg-red-500").ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo(Scoped + "bg-red-500"));
        }

        [Test]
        public void Given_ASurfaceUtilityOnADropdownField_When_TheNodeIsBuilt_Then_ItIsScopedToTheBox()
        {
            // Act
            var classNames = V.DropdownField(className: "bg-red-500").ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo(Scoped + "bg-red-500"));
        }

        [Test]
        public void Given_ASurfaceUtilityOnACustomTextInputField_When_TheNodeIsBuilt_Then_ItIsScopedToTheInputBox()
        {
            // Act
            var classNames = V.Custom<FloatField>(className: "bg-red-500").ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo(Scoped + "bg-red-500"));
        }

        [Test]
        public void Given_ASurfaceUtilityOnACustomPopupField_When_TheNodeIsBuilt_Then_ItIsScopedToTheBox()
        {
            // Act
            var classNames = V.Custom<PopupField<string>>(className: "bg-red-500").ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo(Scoped + "bg-red-500"));
        }

        // GREEN_ON_BASE(characterization): the base routes nothing for a control that is not a field; the case
        // pins that the rewrite belongs to the field controls alone.
        [Test]
        public void Given_ASurfaceUtilityOnACustomNonFieldControl_When_TheNodeIsBuilt_Then_ItIsNotScoped()
        {
            // Act
            var classNames = V.Custom<Foldout>(className: "bg-red-500").ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo("bg-red-500"));
        }

        // GREEN_ON_BASE(characterization): the base routes nothing on a Div; the case pins that the rewrite
        // belongs to the field factories alone.
        [Test]
        public void Given_ASurfaceUtilityOnADiv_When_TheNodeIsBuilt_Then_ItIsNotScoped()
        {
            // Act
            var classNames = V.Div(className: "bg-red-500").ClassNames;

            // Assert
            Assert.That(Joined(classNames), Is.EqualTo("bg-red-500"));
        }

        // Names a member the base does not have, so no base run answers it; setting the literal in
        // `StyleChildVariantClass.InputBoxClass` to another name is what reddens it.
        [Test]
        public void Given_TheInputBoxScope_When_ComparedWithTheEnginesInputClass_Then_TheyAgree()
        {
            // Assert
            Assert.That(StyleChildVariantClass.InputBoxClass, Is.EqualTo(BaseField<string>.inputUssClassName));
        }

        #endregion

        #region Parse

        [Test]
        public void Given_AnInputBoxScopedToken_When_Parsed_Then_ThePayloadAndTheScopeAreReported()
        {
            // Act
            var ok = StyleChildVariantClass.TryParse(Scoped + "hover:bg-red-500", out var payload,
                out var inputBoxOnly);

            // Assert
            Assert.That((ok, payload, inputBoxOnly), Is.EqualTo((true, "hover:bg-red-500", true)));
        }

        [Test]
        public void Given_AnAllChildrenToken_When_Parsed_Then_TheScopeIsNotTheInputBox()
        {
            // Act
            StyleChildVariantClass.TryParse("[&>*]:bg-red-500", out _, out var inputBoxOnly);

            // Assert
            Assert.That(inputBoxOnly, Is.False);
        }

        [Test]
        public void Given_AnInputBoxScopeNestedInAChildVariant_When_Parsed_Then_Declines()
        {
            // Act — a child is never the container the inner scope would walk.
            var ok = StyleChildVariantClass.TryParse("[&>*]:" + Scoped + "bg-red-500", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        #endregion

        #region Reconciled

        [Test]
        public void Given_ABackgroundClassOnATextField_When_Reconciled_Then_OnlyTheInputBoxTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "bg-red-500") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Box(scope.Root[0]).ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnArbitraryBackgroundOnATextField_When_Reconciled_Then_OnlyTheInputBoxIsPainted()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "bg-[#ff0000]") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Box(scope.Root[0]).style.backgroundColor.value, scope.Root[0].style.backgroundColor.keyword),
                Is.EqualTo((Color.red, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ABackgroundOnALabelledTextField_When_Reconciled_Then_TheLabelIsLeftAlone()
        {
            // Arrange — the all-children form reaches the label as well; a field's own surface utility is the
            // box's alone.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "bg-red-500", label: "Name") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var field = (TextField)scope.Root[0];

            // Assert
            Assert.That(
                (Box(field).ClassListContains("bg-red-500"), field.labelElement.ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundOnAnIntegerField_When_Reconciled_Then_OnlyTheInputBoxTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.IntegerField(className: "bg-red-500") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Box(scope.Root[0]).ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundOnADropdownField_When_Reconciled_Then_OnlyTheBoxTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.DropdownField(className: "bg-red-500", choices: new List<string> { "a", "b" }, value: "a"),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Box(scope.Root[0]).ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundOnACustomFloatField_When_Reconciled_Then_OnlyTheInputBoxTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Custom<FloatField>(className: "bg-red-500") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Box(scope.Root[0]).ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundDroppedOnARerender_When_Reconciled_Then_TheInputBoxGivesItUp()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.TextField(className: "bg-red-500") };
            var second = new VNode[] { V.TextField(className: "w-64") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var had = Box(scope.Root[0]).ClassListContains("bg-red-500");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);

            // Assert
            Assert.That((had, Box(scope.Root[0]).ClassListContains("bg-red-500")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundSwappedOnARerender_When_Reconciled_Then_TheInputBoxHoldsTheNewOneAlone()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.TextField(className: "bg-red-500") };
            var second = new VNode[] { V.TextField(className: "bg-blue-500") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);
            var box = Box(scope.Root[0]);

            // Assert
            Assert.That((box.ClassListContains("bg-red-500"), box.ClassListContains("bg-blue-500")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AHoverBackgroundOnATextField_When_ThePointerEntersTheBox_Then_OnlyTheBoxTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "hover:bg-red-500") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var box = Box(scope.Root[0]);
            var before = box.ClassListContains("bg-red-500");

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                box.SimulateEvent(over);
            }

            // Assert
            Assert.That((before, box.ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((false, true, false)));
        }

        [Test]
        public void Given_AWhileHoverSurfaceClassOnATextField_When_ThePointerEntersTheBox_Then_OnlyTheBoxTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(whileHoverClass: "bg-red-500") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var box = Box(scope.Root[0]);

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                box.SimulateEvent(over);
            }

            // Assert
            Assert.That((box.ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base puts every while-hover class on the control. The case pins
        // that a class that is not a surface utility keeps going there.
        [Test]
        public void Given_AWhileHoverLayoutClassOnATextField_When_ThePointerEntersTheField_Then_OnlyTheFieldTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(whileHoverClass: "w-64") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                scope.Root[0].SimulateEvent(over);
            }

            // Assert
            Assert.That((scope.Root[0].ClassListContains("w-64"), Box(scope.Root[0]).ClassListContains("w-64")),
                Is.EqualTo((true, false)));
        }

        // The conditions below are the control's: the first field is the first child, the second is not.
        [Test]
        public void Given_AFirstChildBackgroundOnTextFields_When_Reconciled_Then_OnlyTheFirstFieldsBoxTakesIt()
        {
            // Arrange — the fields sit in a container: a first: rule is evaluated by its parent's post-children
            // pass, which the reconciler's root does not run.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(children: new VNode?[]
                {
                    V.TextField(className: "first:bg-red-500"),
                    V.TextField(className: "first:bg-red-500"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            var fields = scope.Root[0];
            Assert.That(
                (Box(fields[0]).ClassListContains("bg-red-500"), Box(fields[1]).ClassListContains("bg-red-500"),
                    fields[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false, false)));
        }

        [Test]
        public void Given_ADataBackgroundOnATextField_When_TheFieldCarriesTheAttribute_Then_TheInputBoxTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.TextField(className: "data-[state=open]:bg-red-500",
                    data: new Dictionary<string, string> { ["state"] = "open" }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Box(scope.Root[0]).ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AContainerPayloadForEveryChild_When_ItLandsOnATextField_Then_OnlyTheInputBoxTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div("[&>*]:bg-red-500", V.TextField()) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var field = scope.Root[0][0];

            // Assert
            Assert.That((Box(field).ClassListContains("bg-red-500"), field.ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        #endregion

        #region Paint families

        [Test]
        public void Given_AShadowOnATextField_When_Reconciled_Then_TheShadowIsPaintedOnTheInputBox()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "shadow-lg") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var shadows = scope.Reconciler.Context.ShadowBindings;

            // Assert
            Assert.That((shadows.ContainsKey(Box(scope.Root[0])), shadows.ContainsKey(scope.Root[0])),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ARingOnATextField_When_Reconciled_Then_TheRingSitsBesideTheInputBox()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "ring-2") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var field = scope.Root[0];
            var box = Box(field);

            // Assert
            Assert.That(
                (scope.Reconciler.Context.RingBindings.TryGetValue(box, out var ring)
                    && ring.Overlay.parent == field && field.IndexOf(ring.Overlay) == field.IndexOf(box) + 1,
                    scope.Reconciler.Context.RingBindings.ContainsKey(field)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADashedBorderOnATextField_When_Reconciled_Then_TheLineStyleBelongsToTheInputBox()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "border border-dashed") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var borders = scope.Reconciler.Context.BorderStyleBindings;

            // Assert
            Assert.That((borders.ContainsKey(Box(scope.Root[0])), borders.ContainsKey(scope.Root[0])),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AShadowDroppedOnARerender_When_Reconciled_Then_TheInputBoxReleasesItsPainter()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.TextField(className: "shadow-lg") };
            var second = new VNode[] { V.TextField(className: "w-64") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var painted = scope.Reconciler.Context.ShadowBindings.ContainsKey(Box(scope.Root[0]));

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);

            // Assert
            Assert.That((painted, scope.Reconciler.Context.ShadowBindings.Count), Is.EqualTo((true, 0)));
        }

        #endregion

        #region Teardown

        // GREEN_ON_BASE(characterization): the base sweeps the box's own stacked manipulators through the
        // descendant pass of an ordinary removal. The case pins that the routed ones go the same way.
        [Test]
        public void Given_AHoverBackgroundOnAPooledTextField_When_ItIsMountedAndRemovedRepeatedly_Then_NoStackedManipulatorRemains()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var field = new VNode[] { V.TextField(className: "hover:bg-red-500 focus:border-blue-500") };
            var none = Array.Empty<VNode>();

            // Act
            for (var round = 0; round < 3; round++)
            {
                scope.Reconciler.Reconcile(scope.Root, none, field);
                scope.Reconciler.Reconcile(scope.Root, field, none);
            }

            // Assert
            Assert.That(scope.Reconciler.Context.StackedVariantManipulators.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_ARolledBackTextFieldWithBoxPaintAndStates_When_ItsOrphanIsReturned_Then_TheBoxKeepsNoBinding()
        {
            // Arrange — the orphan path reclaims a TextField without walking its descendants by itself.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "shadow-lg hover:bg-red-500") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var context = scope.Reconciler.Context;
            var held = (context.ShadowBindings.Count, context.StackedVariantManipulators.Count);

            // Act
            var field = scope.Root[0];
            field.RemoveFromHierarchy();
            new FiberElementCleaner(context).ReturnRolledBackOrphan(field);

            // Assert
            Assert.That((held, context.ShadowBindings.Count, context.StackedVariantManipulators.Count),
                Is.EqualTo(((1, 1), 0, 0)));
        }

        #endregion
    }
}
