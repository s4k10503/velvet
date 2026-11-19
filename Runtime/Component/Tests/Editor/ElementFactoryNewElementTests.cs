using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the <c>V.*</c> builders and <see cref="FiberElementFactory"/> support the
    /// Image / DropdownField / ListView / RadioButton / RadioButtonGroup / IntegerField element types.
    /// <list type="bullet">
    /// <item><see cref="FiberElementFactory.Create"/> instantiates the UIToolkit element matching each
    /// node's element type.</item>
    /// <item>A builder records its element type and name; a value and change callback populate the field
    /// value and register a single change-event binding, while choices populate the choices settings.</item>
    /// <item>When no value-bearing options are supplied, the node's props are null and no events are
    /// registered.</item>
    /// <item><see cref="FiberElementFactory.ApplyFieldValue"/> sets a field's value without notifying
    /// change listeners, and <see cref="FiberPropApplier.ApplyText"/> sets the element label.</item>
    /// <item><see cref="FiberPropApplier.ApplyChoices"/> sets the choices on choice-bearing elements and is a
    /// no-op for null settings.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ElementFactoryNewElementTests
    {
        private FiberElementFactory _factory;

        [SetUp]
        public void SetUp()
        {
            var eventManager = new FiberEventBindingManager();
            _factory = new FiberElementFactory(eventManager);
        }

        #region Create - element type mapping

        private static IEnumerable<TestCaseData> CreateElementTypeCases()
        {
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.Image()), typeof(Image))
                .SetName("Given_ImageNode_When_Create_Then_ReturnsImage");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.DropdownField()), typeof(DropdownField))
                .SetName("Given_DropdownFieldNode_When_Create_Then_ReturnsDropdownField");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.ListView()), typeof(ListView))
                .SetName("Given_ListViewNode_When_Create_Then_ReturnsListView");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.RadioButton()), typeof(RadioButton))
                .SetName("Given_RadioButtonNode_When_Create_Then_ReturnsRadioButton");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.RadioButtonGroup()), typeof(RadioButtonGroup))
                .SetName("Given_RadioButtonGroupNode_When_Create_Then_ReturnsRadioButtonGroup");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.IntegerField()), typeof(IntegerField))
                .SetName("Given_IntegerFieldNode_When_Create_Then_ReturnsIntegerField");
        }

        [TestCaseSource(nameof(CreateElementTypeCases))]
        public void Given_Node_When_Create_Then_ReturnsMatchingElementType(System.Func<ElementNode> makeNode, System.Type expected)
        {
            // Act
            var element = _factory.Create(makeNode());

            // Assert
            Assert.That(element, Is.InstanceOf(expected));
        }

        #endregion

        #region Builder node properties

        [Test]
        public void Given_ClassNameAndName_When_Image_Then_RecordsElementTypeNameAndClassName()
        {
            // Act
            var node = V.Image(className: "avatar", name: "img");

            // Assert
            Assert.That((node.ElementType, node.Name), Is.EqualTo((typeof(Image), "img")));
            Assert.That(node.ClassNames, Is.EqualTo(new[] { "avatar" }));
        }

        [Test]
        public void Given_ValueChoicesAndCallback_When_DropdownField_Then_RecordsValueChoicesAndBinding()
        {
            // Arrange
            var choices = new List<string> { "A", "B", "C" };

            // Act
            var node = V.DropdownField(value: "A", choices: choices, onValueChanged: _ => { });

            // Assert
            Assume.That(node.Props.Choices.Choices, Is.EqualTo(choices), "Precondition: the choices are recorded");
            Assert.That(
                (node.ElementType, node.Props.FieldValue, node.Events.Length, node.Events[0] is ChangeEventBinding<string>),
                Is.EqualTo((typeof(DropdownField), (object)"A", 1, true)));
        }

        [Test]
        public void Given_EnabledAndName_When_ListView_Then_RecordsElementTypeEnabledAndName()
        {
            // Act
            var node = V.ListView(enabled: false, name: "list");

            // Assert
            Assert.That((node.ElementType, node.Props.Enabled, node.Name),
                Is.EqualTo((typeof(ListView), (bool?)false, "list")));
        }

        [Test]
        public void Given_ValueLabelAndCallback_When_RadioButton_Then_RecordsValueTextAndBinding()
        {
            // Act
            var node = V.RadioButton(value: true, onValueChanged: _ => { }, label: "Option A");

            // Assert
            Assert.That(
                (node.ElementType, node.Props.FieldValue, node.Props.Text, node.Events.Length, node.Events[0] is ChangeEventBinding<bool>),
                Is.EqualTo((typeof(RadioButton), (object)true, "Option A", 1, true)));
        }

        [Test]
        public void Given_ValueChoicesAndCallback_When_RadioButtonGroup_Then_RecordsValueChoicesAndBinding()
        {
            // Arrange
            var choices = new List<string> { "X", "Y" };

            // Act
            var node = V.RadioButtonGroup(value: 0, choices: choices, onValueChanged: _ => { });

            // Assert
            Assume.That(node.Props.Choices.Choices, Is.EqualTo(choices), "Precondition: the choices are recorded");
            Assert.That(
                (node.ElementType, node.Props.FieldValue, node.Events.Length, node.Events[0] is ChangeEventBinding<int>),
                Is.EqualTo((typeof(RadioButtonGroup), (object)0, 1, true)));
        }

        [Test]
        public void Given_ValueLabelAndCallback_When_IntegerField_Then_RecordsValueTextAndBinding()
        {
            // Act
            var node = V.IntegerField(value: 42, onValueChanged: _ => { }, label: "Count");

            // Assert
            Assert.That(
                (node.ElementType, node.Props.FieldValue, node.Props.Text, node.Events.Length, node.Events[0] is ChangeEventBinding<int>),
                Is.EqualTo((typeof(IntegerField), (object)42, "Count", 1, true)));
        }

        private static IEnumerable<TestCaseData> NoOptionsPropsNullCases()
        {
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.DropdownField()))
                .SetName("Given_NoOptions_When_DropdownField_Then_PropsIsNull");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.ListView()))
                .SetName("Given_NoOptions_When_ListView_Then_PropsIsNull");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.RadioButton()))
                .SetName("Given_NoOptions_When_RadioButton_Then_PropsIsNull");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.IntegerField()))
                .SetName("Given_NoOptions_When_IntegerField_Then_PropsIsNull");
        }

        [TestCaseSource(nameof(NoOptionsPropsNullCases))]
        public void Given_NoOptions_When_Builder_Then_PropsIsNull(System.Func<ElementNode> makeNode)
        {
            // Act
            var node = makeNode();

            // Assert
            Assert.That(node.Props, Is.Null);
        }

        private static IEnumerable<TestCaseData> NoOptionsNoEventsCases()
        {
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.DropdownField()))
                .SetName("Given_NoOptions_When_DropdownField_Then_NoEvents");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.RadioButton()))
                .SetName("Given_NoOptions_When_RadioButton_Then_NoEvents");
            yield return new TestCaseData((System.Func<ElementNode>)(() => V.IntegerField()))
                .SetName("Given_NoOptions_When_IntegerField_Then_NoEvents");
        }

        [TestCaseSource(nameof(NoOptionsNoEventsCases))]
        public void Given_NoOptions_When_Builder_Then_NoEvents(System.Func<ElementNode> makeNode)
        {
            // Act
            var node = makeNode();

            // Assert
            Assert.That(node.Events, Is.Empty);
        }

        #endregion

        #region ApplyFieldValue

        [Test]
        public void Given_IntegerField_When_ApplyFieldValue_Then_SetsValue()
        {
            // Arrange
            var field = new IntegerField();

            // Act
            FiberElementFactory.ApplyFieldValue(field, 99);

            // Assert
            Assert.That(field.value, Is.EqualTo(99));
        }

        [Test]
        public void Given_RadioButtonGroup_When_ApplyFieldValue_Then_SetsValue()
        {
            // Arrange
            var rbg = new RadioButtonGroup { choices = new List<string> { "A", "B", "C" } };

            // Act
            FiberElementFactory.ApplyFieldValue(rbg, 2);

            // Assert
            Assert.That(rbg.value, Is.EqualTo(2));
        }

        #endregion

        #region ApplyText

        [Test]
        public void Given_RadioButton_When_ApplyText_Then_SetsLabel()
        {
            // Arrange
            var rb = new RadioButton();

            // Act
            FiberPropApplier.ApplyText(rb, "Choose me");

            // Assert
            Assert.That(rb.label, Is.EqualTo("Choose me"));
        }

        [Test]
        public void Given_RadioButtonGroup_When_ApplyText_Then_SetsLabel()
        {
            // Arrange
            var rbg = new RadioButtonGroup();

            // Act
            FiberPropApplier.ApplyText(rbg, "Pick option");

            // Assert
            Assert.That(rbg.label, Is.EqualTo("Pick option"));
        }

        [Test]
        public void Given_IntegerField_When_ApplyText_Then_SetsLabel()
        {
            // Arrange
            var intField = new IntegerField();

            // Act
            FiberPropApplier.ApplyText(intField, "Count");

            // Assert
            Assert.That(intField.label, Is.EqualTo("Count"));
        }

        #endregion

        #region ApplyChoices

        [Test]
        public void Given_DropdownField_When_ApplyChoices_Then_SetsChoices()
        {
            // Arrange
            var dd = new DropdownField();
            var settings = new ChoicesSettings(new List<string> { "A", "B" });

            // Act
            FiberPropApplier.ApplyChoices(dd, settings);

            // Assert
            Assert.That(dd.choices, Is.EqualTo(settings.Choices));
        }

        [Test]
        public void Given_RadioButtonGroup_When_ApplyChoices_Then_SetsChoices()
        {
            // Arrange
            var rbg = new RadioButtonGroup();
            var settings = new ChoicesSettings(new List<string> { "X", "Y", "Z" });

            // Act
            FiberPropApplier.ApplyChoices(rbg, settings);

            // Assert
            Assert.That(rbg.choices, Is.EqualTo(settings.Choices));
        }

        [Test]
        public void Given_NullSettings_When_ApplyChoices_Then_DoesNotThrow()
        {
            // Arrange
            var dd = new DropdownField();

            // Act + Assert
            Assert.DoesNotThrow(() => FiberPropApplier.ApplyChoices(dd, null));
        }

        #endregion
    }
}
