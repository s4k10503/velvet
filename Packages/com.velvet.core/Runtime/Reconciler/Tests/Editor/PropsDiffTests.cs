using System;
using System.Collections.Generic;
using NUnit.Framework;
using Velvet;
using Velvet.TestUtilities;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a patch applies a changed element's props to its reused VisualElement.
    /// <list type="bullet">
    /// <item>A changed scalar prop (text, tooltip, enabled, field value, scroller visibility) is
    /// written to the element on patch; a value set to null clears the corresponding property.</item>
    /// <item>A changed choices collection (dropdown, radio group) replaces the element's choices.</item>
    /// <item>A <c>Visible = false</c> prop adds the reserved hidden class to the element.</item>
    /// <item>The class list is reconciled by diffing old against new: only the symmetric difference is
    /// applied, removed classes leave and added classes enter while unchanged ones stay. The diff uses
    /// a linear comparison when both sides hold at most eight classes and a HashSet beyond that, with
    /// identical results.</item>
    /// <item>Patching with both sides null is a no-op.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class PropsDiffTests : ReconcilerTestFixture
    {
        private const string HiddenClass = "hidden";

        [Test]
        public void Given_LabelTextChanged_When_Patched_Then_LabelTextUpdated()
        {
            // Arrange
            var oldTree = new VNode[] { V.Label(text: "old") };
            var newTree = new VNode[] { V.Label(text: "new") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((Label)Root.ElementAt(0)).text, Is.EqualTo("new"));
        }

        [Test]
        public void Given_ButtonTextChanged_When_Patched_Then_ButtonTextUpdated()
        {
            // Arrange
            var oldTree = new VNode[] { V.Button(text: "old") };
            var newTree = new VNode[] { V.Button(text: "new") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((Button)Root.ElementAt(0)).text, Is.EqualTo("new"));
        }

        [Test]
        public void Given_EnabledChanged_When_Patched_Then_ElementDisabled()
        {
            // Arrange
            var oldTree = new VNode[] { V.Button(text: "btn", enabled: true) };
            var newTree = new VNode[] { V.Button(text: "btn", enabled: false) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(0).enabledSelf, Is.False);
        }

        [Test]
        public void Given_VisibleFalse_When_Patched_Then_HiddenClassAdded()
        {
            // Arrange
            var oldNode = MakeVisibleNode(true);
            var newNode = MakeVisibleNode(false);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[] { oldNode });

            // Act
            Reconciler.Reconcile(Root, new VNode[] { oldNode }, new VNode[] { newNode });

            // Assert
            Assert.That(Root.ElementAt(0).ClassListContains(HiddenClass), Is.True);
        }

        [Test]
        public void Given_FieldValueChanged_When_Patched_Then_SliderValueUpdated()
        {
            // Arrange
            var oldTree = new VNode[] { V.Slider(value: 0f) };
            var newTree = new VNode[] { V.Slider(value: 0.75f) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((Slider)Root.ElementAt(0)).value, Is.EqualTo(0.75f).Within(0.001f));
        }

        [Test]
        public void Given_FieldValueClearedToNull_When_Patched_Then_IntegerFieldResetToDefault()
        {
            // A controlled field reflects its declared value, so clearing the value prop to null resets the
            // element to its type default instead of stranding the prior value (label keeps props non-null on
            // both sides so the FieldValue diff fires).
            // Arrange
            var oldTree = new VNode[] { V.IntegerField(value: 5, label: "n") };
            var newTree = new VNode[] { V.IntegerField(value: null, label: "n") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            Assume.That(((IntegerField)Root.ElementAt(0)).value, Is.EqualTo(5), "Precondition: the field holds the prior value");

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((IntegerField)Root.ElementAt(0)).value, Is.EqualTo(0));
        }

        [Test]
        public void Given_FieldValueClearedToNull_When_Patched_Then_TextFieldResetToEmpty()
        {
            // The PII-sensitive case: a controlled TextField cleared to null must blank rather than retain the
            // prior text.
            // Arrange
            var oldTree = new VNode[] { V.TextField(value: "secret", label: "n") };
            var newTree = new VNode[] { V.TextField(value: null, label: "n") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            Assume.That(((TextField)Root.ElementAt(0)).value, Is.EqualTo("secret"), "Precondition: the field holds the prior value");

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((TextField)Root.ElementAt(0)).value, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Given_NullText_When_PatchedToNonNull_Then_TextApplied()
        {
            // Arrange
            var oldTree = new VNode[] { V.Label() };
            var newTree = new VNode[] { V.Label(text: "hello") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((Label)Root.ElementAt(0)).text, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_NonNullText_When_PatchedToNull_Then_TextCleared()
        {
            // Arrange
            var oldTree = new VNode[] { V.Label(text: "hello") };
            var newTree = new VNode[] { V.Label() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((Label)Root.ElementAt(0)).text, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Given_NonNullTooltip_When_PatchedToNull_Then_TooltipCleared()
        {
            // Arrange
            var oldTree = new VNode[] { V.Button(text: "btn", tooltip: "tip") };
            var newTree = new VNode[] { V.Button(text: "btn") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(Root.ElementAt(0).tooltip, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Given_BothPropsNull_When_Patched_Then_NoOp()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div() };
            var newTree = new VNode[] { V.Div() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act + Assert
            Assert.DoesNotThrow(() => Reconciler.Reconcile(Root, oldTree, newTree));
        }

        [Test]
        public void Given_ChoicesChanged_When_Patched_Then_DropdownChoicesReplaced()
        {
            // Arrange
            var oldTree = new VNode[] { V.DropdownField(choices: new List<string> { "A", "B" }) };
            var newTree = new VNode[] { V.DropdownField(choices: new List<string> { "X", "Y", "Z" }) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((DropdownField)Root.ElementAt(0)).choices,
                Is.EqualTo(new List<string> { "X", "Y", "Z" }));
        }

        [Test]
        public void Given_ChoicesChanged_When_Patched_Then_RadioButtonGroupChoicesReplaced()
        {
            // Arrange
            var oldTree = new VNode[] { V.RadioButtonGroup(choices: new List<string> { "A", "B" }) };
            var newTree = new VNode[] { V.RadioButtonGroup(choices: new List<string> { "X", "Y", "Z" }) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((RadioButtonGroup)Root.ElementAt(0)).choices,
                Is.EqualTo(new List<string> { "X", "Y", "Z" }));
        }

        [Test]
        public void Given_ChoicesClearedToNull_When_Patched_Then_DropdownChoicesEmptied()
        {
            // Arrange
            var oldTree = new VNode[] { V.DropdownField(choices: new List<string> { "A", "B" }) };
            var newTree = new VNode[] { V.DropdownField(choices: null) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((DropdownField)Root.ElementAt(0)).choices, Is.Empty);
        }

        [Test]
        public void Given_ChoicesClearedToNull_When_Patched_Then_RadioButtonGroupChoicesEmptied()
        {
            // Arrange
            var oldTree = new VNode[] { V.RadioButtonGroup(choices: new List<string> { "A", "B" }) };
            var newTree = new VNode[] { V.RadioButtonGroup(choices: null) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((RadioButtonGroup)Root.ElementAt(0)).choices, Is.Empty);
        }

        [Test]
        public void Given_VerticalScrollerVisibilityChanged_When_Patched_Then_Applied()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.ScrollView(key: "sv", verticalScrollerVisibility: ScrollerVisibility.AlwaysVisible),
            };
            var newTree = new VNode[]
            {
                V.ScrollView(key: "sv", verticalScrollerVisibility: ScrollerVisibility.Hidden),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((ScrollView)Root.ElementAt(0)).verticalScrollerVisibility,
                Is.EqualTo(ScrollerVisibility.Hidden));
        }

        [Test]
        public void Given_HorizontalScrollerVisibilityChanged_When_Patched_Then_Applied()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.ScrollView(key: "sv", horizontalScrollerVisibility: ScrollerVisibility.AlwaysVisible),
            };
            var newTree = new VNode[]
            {
                V.ScrollView(key: "sv", horizontalScrollerVisibility: ScrollerVisibility.Hidden),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((ScrollView)Root.ElementAt(0)).horizontalScrollerVisibility,
                Is.EqualTo(ScrollerVisibility.Hidden));
        }

        [Test]
        public void Given_UnchangedClassList_When_Patched_Then_AllClassesRetained()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div(className: "a b c") };
            var newTree = new VNode[] { V.Div(className: "a b c") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            var el = Root.ElementAt(0);
            Assert.That(
                (el.ClassListContains("a"), el.ClassListContains("b"), el.ClassListContains("c")),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_ClassListChangedWithinLinearThreshold_When_Patched_Then_SymmetricDiffApplied()
        {
            // Arrange — both sides hold at most eight classes, so the linear-compare path runs
            var oldTree = new VNode[] { V.Div(className: "a b c") };
            var newTree = new VNode[] { V.Div(className: "b c d") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            var el = Root.ElementAt(0);
            Assert.That(
                (el.ClassListContains("a"), el.ClassListContains("b"),
                    el.ClassListContains("c"), el.ClassListContains("d")),
                Is.EqualTo((false, true, true, true)));
        }

        [Test]
        public void Given_ClassListChangedBeyondLinearThreshold_When_Patched_Then_SymmetricDiffApplied()
        {
            // Arrange — more than eight classes per side, so the HashSet path runs
            var oldTree = new VNode[] { V.Div(className: "c1 c2 c3 c4 c5 c6 c7 c8 c9") };
            var newTree = new VNode[] { V.Div(className: "c2 c3 c4 c5 c6 c7 c8 c9 c10") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            var el = Root.ElementAt(0);
            Assert.That(
                (el.ClassListContains("c1"), el.ClassListContains("c5"), el.ClassListContains("c10")),
                Is.EqualTo((false, true, true)));
        }

        private static ElementNode MakeVisibleNode(bool visible)
            => new()
            {
                ElementType = typeof(VisualElement),
                Props = new FiberElementProps { Visible = visible },
                Children = Array.Empty<VNode>(),
                Events = Array.Empty<FiberEventBinding>(),
            };
    }
}
