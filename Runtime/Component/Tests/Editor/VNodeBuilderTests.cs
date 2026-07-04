using NUnit.Framework;
using Velvet;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    public static class TestComponent
    {
        [Component]
        public static VNode Render() => V.Label(text: "test");
    }

    /// <summary>
    /// Specifies how the <c>V.*</c> DSL builders translate their arguments into VNode shape.
    /// <list type="bullet">
    /// <item>A className string is split on whitespace into individual class names; null yields no classes,
    /// and surrounding or repeated whitespace is collapsed.</item>
    /// <item>Children, text, names, and field values supplied to a builder land on the produced node's
    /// corresponding members.</item>
    /// <item>Field builders (Slider / Toggle / TextField) record their value, optional settings, and enabled
    /// state in props, and register a change-event binding only when a callback is supplied.</item>
    /// <item>A Button registers a click binding only when an onClick is supplied, and text and children
    /// coexist independently.</item>
    /// <item>Structural builders create their dedicated node types: <c>V.Text</c> a TextNode, <c>V.Memoized</c> a
    /// MemoNode carrying its deps, <c>V.Fragment</c> a FragmentNode, <c>V.Component</c> a ComponentNode keyed
    /// by the render method identity.</item>
    /// <item><c>V.When</c> returns the built node when the condition holds and null otherwise (V.List is
    /// specified separately by VListTests).</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VNodeBuilderTests
    {
        #region Div

        [Test]
        public void Given_ClassNameString_When_Div_Then_SplitsIntoClassNames()
        {
            // Act
            var node = V.Div("btn btn--active");

            // Assert
            Assert.That(node.ClassNames, Is.EqualTo(new[] { "btn", "btn--active" }));
        }

        [Test]
        public void Given_SurroundingAndRepeatedWhitespace_When_Div_Then_CollapsesIntoClassNames()
        {
            // Act
            var node = V.Div("  btn   btn--active  ");

            // Assert
            Assert.That(node.ClassNames, Is.EqualTo(new[] { "btn", "btn--active" }));
        }

        [Test]
        public void Given_NullClassName_When_Div_Then_HasNoClassNames()
        {
            // Act
            var node = V.Div(className: null);

            // Assert
            Assert.That(node.ClassNames, Is.Empty);
        }

        [Test]
        public void Given_Children_When_Div_Then_PreservesChildrenInOrder()
        {
            // Arrange
            var child1 = V.Label(text: "a");
            var child2 = V.Label(text: "b");

            // Act
            var node = V.Div(children: new VNode[] { child1, child2 });

            // Assert
            Assert.That(node.Children, Is.EqualTo(new[] { child1, child2 }));
        }

        #endregion

        #region Button

        [Test]
        public void Given_OnClick_When_Button_Then_RegistersSingleClickBinding()
        {
            // Act
            var node = V.Button(onClick: () => { });

            // Assert
            Assert.That(node.Events.Length, Is.EqualTo(1));
            Assert.That(node.Events[0], Is.InstanceOf<ClickedBinding>());
        }

        [Test]
        public void Given_ClickBinding_When_HandlerInvoked_Then_RunsTheOnClickCallback()
        {
            // Arrange
            var clicked = false;
            var node = V.Button(onClick: () => clicked = true);
            Assume.That(node.Events[0], Is.InstanceOf<ClickedBinding>(), "Precondition: a click binding was registered");

            // Act
            ((ClickedBinding)node.Events[0]).Handler();

            // Assert
            Assert.That(clicked, Is.True);
        }

        [Test]
        public void Given_NoOnClick_When_Button_Then_RegistersNoEvents()
        {
            // Act
            var node = V.Button(text: "No click");

            // Assert
            Assert.That(node.Events, Is.Empty);
        }

        [Test]
        public void Given_Text_When_Button_Then_RecordsElementTypeAndText()
        {
            // Act
            var node = V.Button(text: "Click me");

            // Assert
            Assert.That((node.ElementType, node.Props?.Text),
                Is.EqualTo((typeof(Button), "Click me")));
        }

        [Test]
        public void Given_NoChildren_When_Button_Then_HasEmptyChildren()
        {
            // Act
            var node = V.Button(text: "no children");

            // Assert
            Assert.That(node.Children, Is.Empty);
        }

        [Test]
        public void Given_Children_When_Button_Then_PropagatesChildrenToNode()
        {
            // Act
            var node = V.Button(
                className: "btn",
                children: new VNode[]
                {
                    V.Div(name: "icon", className: "icon"),
                    V.Label(text: "label"),
                });

            // Assert
            Assert.That(
                (node.Children.Length, ((ElementNode)node.Children[0]).Name, ((ElementNode)node.Children[1]).Props.Text),
                Is.EqualTo((2, "icon", "label")));
        }

        [Test]
        public void Given_TextAndChildren_When_Button_Then_KeepsBothIndependently()
        {
            // Act — text and children can coexist; the Button.text property and child elements are independent
            var node = V.Button(
                text: "label",
                children: new VNode[] { V.Div(name: "icon") });

            // Assert
            Assert.That(
                (node.Props.Text, node.Children.Length, ((ElementNode)node.Children[0]).Name),
                Is.EqualTo(("label", 1, "icon")));
        }

        #endregion

        #region Custom

        [Test]
        public void Given_NoChildren_When_Custom_Then_HasEmptyChildrenAndGenericElementType()
        {
            // Act
            var node = V.Custom<VisualElement>(name: "blur");

            // Assert
            Assert.That((node.Children.Length, node.ElementType),
                Is.EqualTo((0, typeof(VisualElement))));
        }

        [Test]
        public void Given_Children_When_Custom_Then_PropagatesChildrenToNode()
        {
            // Act
            var node = V.Custom<VisualElement>(
                name: "shell",
                children: new VNode[] { V.Label(text: "inside-custom") });

            // Assert
            Assert.That((node.Children.Length, ((ElementNode)node.Children[0]).Props.Text),
                Is.EqualTo((1, "inside-custom")));
        }

        #endregion

        #region Label

        [Test]
        public void Given_Text_When_Label_Then_RecordsElementTypeAndText()
        {
            // Act
            var node = V.Label(text: "Title");

            // Assert
            Assert.That((node.ElementType, node.Props?.Text),
                Is.EqualTo((typeof(Label), "Title")));
        }

        #endregion

        #region Slider

        [Test]
        public void Given_ValueAndCallback_When_Slider_Then_RecordsValueAndChangeBinding()
        {
            // Act
            var node = V.Slider(value: 0.5f, onValueChanged: _ => { });

            // Assert
            Assert.That(
                (node.ElementType, node.Props.FieldValue, node.Events.Length, node.Events[0] is ChangeEventBinding<float>),
                Is.EqualTo((typeof(Slider), (object)0.5f, 1, true)));
        }

        [Test]
        public void Given_LowAndHighValue_When_Slider_Then_RecordsRangeAndName()
        {
            // Act
            var node = V.Slider(value: 0.5f, lowValue: 0f, highValue: 1f, name: "vol");

            // Assert
            Assert.That((node.Props.Slider?.LowValue, node.Props.Slider?.HighValue, node.Name),
                Is.EqualTo(((float?)0f, (float?)1f, "vol")));
        }

        [Test]
        public void Given_Enabled_When_Slider_Then_RecordsEnabledProp()
        {
            // Act
            var node = V.Slider(enabled: false);

            // Assert
            Assert.That(node.Props?.Enabled, Is.False);
        }

        #endregion

        #region Toggle

        [Test]
        public void Given_ValueAndCallback_When_Toggle_Then_RecordsValueAndChangeBinding()
        {
            // Act
            var node = V.Toggle(value: true, onValueChanged: _ => { });

            // Assert
            Assert.That(
                (node.ElementType, node.Props.FieldValue, node.Events.Length, node.Events[0] is ChangeEventBinding<bool>),
                Is.EqualTo((typeof(Toggle), (object)true, 1, true)));
        }

        [Test]
        public void Given_LabelNameAndEnabled_When_Toggle_Then_RecordsTextEnabledAndName()
        {
            // Act
            var node = V.Toggle(label: "Dark Mode", name: "dark", enabled: true);

            // Assert
            Assert.That((node.Props.Text, node.Props.Enabled, node.Name),
                Is.EqualTo(("Dark Mode", (bool?)true, "dark")));
        }

        #endregion

        #region TextField

        [Test]
        public void Given_ValueAndCallback_When_TextField_Then_RecordsValueAndChangeBinding()
        {
            // Act
            var node = V.TextField(value: "hello", onValueChanged: _ => { });

            // Assert
            Assert.That(
                (node.ElementType, node.Props.FieldValue, node.Events.Length, node.Events[0] is ChangeEventBinding<string>),
                Is.EqualTo((typeof(TextField), (object)"hello", 1, true)));
        }

        [Test]
        public void Given_PasswordLabelAndEnabled_When_TextField_Then_RecordsPasswordTextEnabledAndName()
        {
            // Act
            var node = V.TextField(label: "Password", isPasswordField: true, enabled: false, name: "pw");

            // Assert
            Assert.That(
                (node.Props.TextField?.IsPassword, node.Props.Enabled, node.Props.Text, node.Name),
                Is.EqualTo(((bool?)true, (bool?)false, "Password", "pw")));
        }

        #endregion

        #region ScrollView

        [Test]
        public void Given_ScrollerVisibility_When_ScrollView_Then_RecordsTypeAndVisibility()
        {
            // Act
            var node = V.ScrollView(
                key: "sv",
                verticalScrollerVisibility: ScrollerVisibility.Hidden);

            // Assert
            Assert.That(
                (node.ElementType, node.Props?.ScrollView?.VerticalScrollerVisibility, node.Props?.ScrollView?.HorizontalScrollerVisibility),
                Is.EqualTo((typeof(ScrollView), (ScrollerVisibility?)ScrollerVisibility.Hidden, (ScrollerVisibility?)null)));
        }

        [Test]
        public void Given_NoVisibility_When_ScrollView_Then_PropsIsNull()
        {
            // Act
            var node = V.ScrollView(key: "sv");

            // Assert
            Assert.That(node.Props, Is.Null);
        }

        #endregion

        #region Text

        [Test]
        public void Given_Content_When_Text_Then_CreatesTextNodeWithContent()
        {
            // Act
            var node = V.Text("Hello");

            // Assert
            Assume.That(node, Is.InstanceOf<TextNode>(), "Precondition: V.Text builds a TextNode");
            Assert.That(node.Text, Is.EqualTo("Hello"));
        }

        #endregion

        // V.List behavior (selector-key mapping, empty/null source, indexed overload, null-slot
        // preservation) is specified in full by VListTests.

        #region When

        [Test]
        public void Given_TrueCondition_When_When_Then_ReturnsBuiltNode()
        {
            // Act
            var node = V.When(true, () => V.Label(text: "visible"));

            // Assert
            Assert.That(node, Is.InstanceOf<ElementNode>());
        }

        [Test]
        public void Given_FalseCondition_When_When_Then_ReturnsNull()
        {
            // Act
            var node = V.When(false, () => V.Label(text: "hidden"));

            // Assert
            Assert.That(node, Is.Null);
        }

        #endregion

        #region Memo

        [Test]
        public void Given_Deps_When_Memo_Then_CreatesMemoNodeCarryingDeps()
        {
            // Act
            var node = V.Memoized(() => V.Label(text: "cached"), "dep1", 42);

            // Assert
            Assume.That(node, Is.InstanceOf<MemoNode>(), "Precondition: V.Memoized builds a MemoNode");
            Assert.That(node.Dependencies, Is.EqualTo(new object[] { "dep1", 42 }));
        }

        #endregion

        #region Fragment

        [Test]
        public void Given_Children_When_Fragment_Then_CreatesFragmentNodeWithChildren()
        {
            // Act
            var node = V.Fragment(new[] { V.Label(text: "a"), V.Label(text: "b") });

            // Assert
            Assume.That(node, Is.InstanceOf<FragmentNode>(), "Precondition: V.Fragment builds a FragmentNode");
            Assert.That(node.Children.Length, Is.EqualTo(2));
        }

        #endregion

        #region Component

        [Test]
        public void Given_RenderMethodAndKey_When_Component_Then_CarriesKey()
        {
            // Act
            var node = V.Component(TestComponent.Render, key: "test-key");

            // Assert
            Assume.That(node, Is.InstanceOf<ComponentNode>(), "Precondition: V.Component builds a ComponentNode");
            Assert.That(node.Key, Is.EqualTo("test-key"));
        }

        [Test]
        public void Given_RenderMethodAndKey_When_Component_Then_IdentityIsTheRenderMethod()
        {
            // Act
            var node = V.Component(TestComponent.Render, key: "test-key");

            // Assert — Identity is the render method's MethodInfo (MethodInfo equality is method identity)
            Assert.That(node.Identity, Is.EqualTo(((System.Func<VNode>)TestComponent.Render).Method));
        }

        #endregion
    }
}
