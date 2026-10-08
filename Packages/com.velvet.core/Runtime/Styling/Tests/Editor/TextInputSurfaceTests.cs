using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where the background, border, radius and padding utilities written on a text-input control land:
    /// on its text-input box, as they do on an <c>&lt;input&gt;</c>, with every other utility left on the outer
    /// control. <see cref="StyleTextInputSurface"/> rewrites the class list and
    /// <see cref="StyleChildVariantManipulator"/> applies the result.
    /// </summary>
    [TestFixture]
    internal sealed class TextInputSurfaceTests
    {
        private const string Scoped = "[&>#unity-text-input]:";

        #region Routing

        [TestCase("bg-red-500")]
        [TestCase("bg-[#ff0000]")]
        [TestCase("border")]
        [TestCase("border-2")]
        [TestCase("border-slate-600")]
        [TestCase("border-t-[3px]")]
        [TestCase("rounded")]
        [TestCase("rounded-lg")]
        [TestCase("p-4")]
        [TestCase("px-2")]
        [TestCase("pe-3")]
        [TestCase("hover:bg-red-500")]
        [TestCase("focus:border-blue-500")]
        [TestCase("dark:md:p-2")]
        [TestCase("bg-red-500!")]
        public void Given_ASurfaceUtilityOnATextField_When_TheNodeIsBuilt_Then_ItIsScopedToTheInput(string token)
        {
            // Act
            var classNames = V.TextField(className: token).ClassNames;

            // Assert
            Assert.That(string.Join("|", classNames), Is.EqualTo(Scoped + token));
        }

        [Test]
        public void Given_SurfaceAndLayoutUtilities_When_TheNodeIsBuilt_Then_OnlyTheSurfaceOnesAreScoped()
        {
            // Act
            var classNames = V.TextField(className: "w-64 bg-red-500 mt-2 rounded-lg").ClassNames;

            // Assert
            Assert.That(string.Join("|", classNames),
                Is.EqualTo("w-64|" + Scoped + "bg-red-500|mt-2|" + Scoped + "rounded-lg"));
        }

        // GREEN_ON_BASE(characterization): the base leaves every one of these on the outer control, and the
        // change declines them on purpose, so the case pins the part of the routing that must not move.
        [TestCase("bg-gradient-to-r")]
        [TestCase("bg-linear-to-r")]
        [TestCase("border-dashed")]
        [TestCase("border-dotted")]
        [TestCase("border-solid")]
        [TestCase("shadow-lg")]
        [TestCase("ring-2")]
        [TestCase("text-white")]
        [TestCase("first:bg-red-500")]
        [TestCase("[&>*]:bg-red-500")]
        public void Given_AnUtilityTheRoutingDeclines_When_TheNodeIsBuilt_Then_ItStaysOnTheOuterControl(string token)
        {
            // Act
            var classNames = V.TextField(className: token).ClassNames;

            // Assert
            Assert.That(string.Join("|", classNames), Is.EqualTo(token));
        }

        [Test]
        public void Given_ASurfaceUtilityOnAnIntegerField_When_TheNodeIsBuilt_Then_ItIsScopedToTheInput()
        {
            // Act
            var classNames = V.IntegerField(className: "bg-red-500").ClassNames;

            // Assert
            Assert.That(string.Join("|", classNames), Is.EqualTo(Scoped + "bg-red-500"));
        }

        // GREEN_ON_BASE(characterization): the base routes nothing on a Div; the case pins that the rewrite
        // belongs to the text-input controls alone.
        [Test]
        public void Given_ASurfaceUtilityOnADiv_When_TheNodeIsBuilt_Then_ItIsNotScoped()
        {
            // Act
            var classNames = V.Div(className: "bg-red-500").ClassNames;

            // Assert
            Assert.That(string.Join("|", classNames), Is.EqualTo("bg-red-500"));
        }

        // GREEN_ON_BASE(construction): both sides are the repository's own text. Change the literal in
        // `StyleChildVariantClass.TextInputName` and this is what reddens.
        [Test]
        public void Given_TheTextInputScope_When_ComparedWithTheEnginesInputName_Then_TheyAgree()
        {
            // Assert
            Assert.That(StyleChildVariantClass.TextInputName, Is.EqualTo(TextField.textInputUssName));
        }

        #endregion

        #region Parse

        [Test]
        public void Given_ATextInputScopedToken_When_Parsed_Then_ThePayloadAndTheScopeAreReported()
        {
            // Act
            var ok = StyleChildVariantClass.TryParse(Scoped + "hover:bg-red-500", out var payload,
                out var textInputOnly);

            // Assert
            Assert.That((ok, payload, textInputOnly), Is.EqualTo((true, "hover:bg-red-500", true)));
        }

        [Test]
        public void Given_AnAllChildrenToken_When_Parsed_Then_TheScopeIsNotTheTextInput()
        {
            // Act
            StyleChildVariantClass.TryParse("[&>*]:bg-red-500", out _, out var textInputOnly);

            // Assert
            Assert.That(textInputOnly, Is.False);
        }

        [Test]
        public void Given_ATextInputScopeNestedInAChildVariant_When_Parsed_Then_Declines()
        {
            // Act — a child is never the container the inner scope would walk.
            var ok = StyleChildVariantClass.TryParse("[&>*]:" + Scoped + "bg-red-500", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        #endregion

        #region Reconciled

        private static VisualElement Input(VisualElement field) => field.Q<VisualElement>(TextField.textInputUssName);

        [Test]
        public void Given_ABackgroundClassOnATextField_When_Reconciled_Then_OnlyTheInputTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "bg-red-500") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Input(scope.Root[0]).ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AnArbitraryBackgroundOnATextField_When_Reconciled_Then_OnlyTheInputIsPainted()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "bg-[#ff0000]") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Input(scope.Root[0]).style.backgroundColor.value, scope.Root[0].style.backgroundColor.keyword),
                Is.EqualTo((Color.red, StyleKeyword.Undefined)));
        }

        [Test]
        public void Given_ABackgroundOnALabelledTextField_When_Reconciled_Then_TheLabelIsLeftAlone()
        {
            // Arrange — the all-children form reaches the label as well; a field's own surface utility is the
            // input's alone.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.TextField(className: "bg-red-500", label: "Name") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var field = (TextField)scope.Root[0];

            // Assert
            Assert.That(
                (Input(field).ClassListContains("bg-red-500"), field.labelElement.ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundOnAnIntegerField_When_Reconciled_Then_OnlyTheInputTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.IntegerField(className: "bg-red-500") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (Input(scope.Root[0]).ClassListContains("bg-red-500"), scope.Root[0].ClassListContains("bg-red-500")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundDroppedOnARerender_When_Reconciled_Then_TheInputGivesItUp()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.TextField(className: "bg-red-500") };
            var second = new VNode[] { V.TextField(className: "w-64") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var had = Input(scope.Root[0]).ClassListContains("bg-red-500");

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);

            // Assert
            Assert.That((had, Input(scope.Root[0]).ClassListContains("bg-red-500")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABackgroundSwappedOnARerender_When_Reconciled_Then_TheInputHoldsTheNewOneAlone()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.TextField(className: "bg-red-500") };
            var second = new VNode[] { V.TextField(className: "bg-blue-500") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);
            var input = Input(scope.Root[0]);

            // Assert
            Assert.That((input.ClassListContains("bg-red-500"), input.ClassListContains("bg-blue-500")),
                Is.EqualTo((false, true)));
        }

        #endregion
    }
}
