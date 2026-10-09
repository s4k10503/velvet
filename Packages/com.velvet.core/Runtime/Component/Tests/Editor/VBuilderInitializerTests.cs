using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.Experimental;

namespace Velvet.Tests
{
    /// <summary>
    /// PoC specs for the experimental initializer-style builders in <c>Velvet.Experimental</c>.
    /// Each builder is a thin surface over the <c>V.*</c> factories, so these specs pin that the produced
    /// <see cref="VNode"/> is equivalent to the long-form factory call, and that the declarative
    /// nested-brace authoring shape compiles and lowers to the expected tree.
    /// </summary>
    [TestFixture]
    internal sealed class VBuilderInitializerTests
    {
        [Test]
        public void Given_VDivBuilder_When_ImplicitlyConverted_Then_ProducesDivElementNode()
        {
            VNode node = new VDiv("flex-col");

            Assert.That(((ElementNode)node).ElementType, Is.EqualTo(typeof(VisualElement)));
        }

        [Test]
        public void Given_ClassNameOnBuilder_When_Built_Then_MatchesFactoryClassNames()
        {
            VNode node = new VDiv("flex-col gap-4");

            Assert.That(((ElementNode)node).ClassNames,
                Is.EqualTo(((ElementNode)V.Div("flex-col gap-4")).ClassNames));
        }

        [Test]
        public void Given_NestedBuilderChildren_When_Built_Then_ChildCountMatchesInitializerItems()
        {
            VNode node = new VDiv("root")
            {
                new VLabel("title") { Text = "a" },
                new VButton { Text = "b" },
            };

            Assert.That(((ElementNode)node).Children.Length, Is.EqualTo(2));
        }

        [Test]
        public void Given_VLabelBuilder_When_Built_Then_TextFlowsToProps()
        {
            VNode node = new VLabel("title") { Text = "Count: 0" };

            Assert.That(((ElementNode)node).Props.Text, Is.EqualTo("Count: 0"));
        }

        [Test]
        public void Given_VButtonBuilder_When_Built_Then_ProducesButtonElementType()
        {
            VNode node = new VButton("btn") { Text = "Increment" };

            Assert.That(((ElementNode)node).ElementType, Is.EqualTo(typeof(Button)));
        }

        [Test]
        public void Given_VButtonWithOnClick_When_Built_Then_BindsSingleClickEvent()
        {
            VNode node = new VButton { Text = "Increment", OnClick = () => { } };

            Assert.That(((ElementNode)node).Events.Length, Is.EqualTo(1));
        }

        [Test]
        public void Given_FactoryNodeAsChild_When_AddedToBuilder_Then_InteroperatesWithVStar()
        {
            VNode node = new VDiv("root")
            {
                V.Label(text: "from factory"),
            };

            Assert.That(((ElementNode)node).Children.Length, Is.EqualTo(1));
        }

        [Test]
        public void Given_NullChild_When_AddedToBuilder_Then_IsSkipped()
        {
            VNode node = new VDiv("root")
            {
                V.When(false, () => V.Label(text: "hidden")),
            };

            Assert.That(((ElementNode)node).Children.Length, Is.EqualTo(0));
        }

        [Test]
        public void Given_PooledNodeArrayOnTop_When_BuilderBuildsChildren_Then_ReusesPooledArray()
        {
            // Arrange — seed the length-2 bucket so a known array instance sits on top of the pool stack.
            // (track2) BuildChildren must rent this rather than allocating a fresh array via ToArray().
            var seeded = VNodePool.RentNodeArray(2);
            VNodePool.ReturnNodeArray(seeded);

            // Act
            VNode node = new VDiv("root")
            {
                V.Label(text: "a"),
                V.Label(text: "b"),
            };

            // Assert
            Assert.That(((ElementNode)node).Children, Is.SameAs(seeded));
        }

        [Test]
        public void Given_VScrollViewBuilder_When_Built_Then_ProducesScrollViewElementType()
        {
            VNode node = new VScrollView("grow");

            Assert.That(((ElementNode)node).ElementType, Is.EqualTo(typeof(ScrollView)));
        }

        [Test]
        public void Given_NestedChildren_When_VScrollViewBuilt_Then_ChildCountMatchesInitializerItems()
        {
            VNode node = new VScrollView("grow")
            {
                new VLabel("row") { Text = "a" },
                new VLabel("row") { Text = "b" },
            };

            Assert.That(((ElementNode)node).Children.Length, Is.EqualTo(2));
        }

        [Test]
        public void Given_VCustomBuilder_When_Built_Then_PreservesGenericElementType()
        {
            VNode node = new VCustom<ScrollView>("custom")
            {
                V.Label(text: "child"),
            };

            Assert.That(((ElementNode)node).ElementType, Is.EqualTo(typeof(ScrollView)));
        }

        [Test]
        public void Given_VTextFieldWithOnChange_When_Built_Then_BindsSingleChangeEvent()
        {
            VNode node = new VTextField("field") { Value = "hi", OnChange = _ => { } };

            Assert.That(((ElementNode)node).Events.Length, Is.EqualTo(1));
        }

        [Test]
        public void Given_VTextFieldWithEveryHandler_When_Built_Then_TheNodeCarriesABindingForEach()
        {
            // Arrange
            var builder = new VTextField("field")
            {
                OnChange = _ => { },
                OnSubmit = _ => { },
                OnKeyDown = _ => { },
                OnKeyUp = _ => { },
                OnFocus = _ => { },
                OnBlur = _ => { },
            };

            // Act
            var node = builder.Build();

            // Assert
            Assert.That(
                string.Join(",", Array.ConvertAll(((ElementNode)node).Events, binding => binding.EventId)),
                Is.EqualTo("change:String,textfield:submit,keydown,keyup,textfield:focus,textfield:blur"));
        }

        [Test]
        public void Given_VTextFieldWithMultilineAndKeyboardKnobs_When_Built_Then_TheSettingsCarryEach()
        {
            // Arrange
            var builder = new VTextField("field")
            {
                Multiline = true,
                KeyboardType = UnityEngine.TouchScreenKeyboardType.NumberPad,
                AutoCorrection = false,
            };

            // Act
            var node = builder.Build();

            // Assert
            Assert.That(
                ((ElementNode)node).Props!.TextField,
                Is.EqualTo(new TextFieldSettings
                {
                    Multiline = true,
                    KeyboardType = UnityEngine.TouchScreenKeyboardType.NumberPad,
                    AutoCorrection = false,
                }));
        }

        [Test]
        public void Given_VSliderBuilder_When_Built_Then_ProducesSliderElementType()
        {
            VNode node = new VSlider("slider") { Value = 0.5f, LowValue = 0f, HighValue = 1f };

            Assert.That(((ElementNode)node).ElementType, Is.EqualTo(typeof(Slider)));
        }

        [Test]
        public void Given_VSliderWithADirection_When_Built_Then_TheNodeCarriesIt()
        {
            // Arrange
            var builder = new VSlider("slider") { Direction = SliderDirection.Vertical };

            // Act
            VNode node = builder;

            // Assert
            Assert.That(((ElementNode)node).Props?.Slider?.Direction, Is.EqualTo(SliderDirection.Vertical));
        }

        [Test]
        public void Given_VSliderInverted_When_Built_Then_TheNodeCarriesIt()
        {
            // Arrange
            var builder = new VSlider("slider") { Inverted = true };

            // Act
            VNode node = builder;

            // Assert
            Assert.That(((ElementNode)node).Props?.Slider?.Inverted, Is.True);
        }

        [Test]
        public void Given_VSliderWithAStep_When_Built_Then_TheNodeCarriesIt()
        {
            // Arrange
            var builder = new VSlider("slider") { Step = 5f };

            // Act
            VNode node = builder;

            // Assert
            Assert.That(((ElementNode)node).Props?.Slider?.Step, Is.EqualTo(5f));
        }

        [Test]
        public void Given_VSliderIntWithEveryMember_When_Built_Then_TheNodeIsTheFactorysSliderInt()
        {
            // Arrange
            var builder = new VSliderInt("slider")
            {
                Value = 3, LowValue = 1, HighValue = 9, Direction = SliderDirection.Vertical, Inverted = true,
                Step = 2, Enabled = false, OnChange = _ => { },
            };

            // Act
            var node = (ElementNode)builder.Build();

            // Assert
            Assert.That(
                (node.ElementType, node.Props?.FieldValue, node.Props?.Enabled, node.Props?.SliderInt, node.Events.Length),
                Is.EqualTo((typeof(SliderInt), (object)3, (bool?)false,
                    new SliderIntSettings(1, 9, SliderDirection.Vertical, true, 2), 1)));
        }

        [Test]
        public void Given_VToggleWithOnChange_When_Built_Then_BindsSingleChangeEvent()
        {
            VNode node = new VToggle("toggle") { Value = true, OnChange = _ => { } };

            Assert.That(((ElementNode)node).Events.Length, Is.EqualTo(1));
        }

        [Test]
        public void Given_VImageBuilder_When_Built_Then_ProducesImageElementType()
        {
            VNode node = new VImage("avatar");

            Assert.That(((ElementNode)node).ElementType, Is.EqualTo(typeof(Image)));
        }
    }
}
