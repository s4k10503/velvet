using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the concise positional-className + params-children overloads on the <c>V.*</c> DSL builders.
    /// <list type="bullet">
    /// <item>The first positional argument is parsed as the className and the trailing params arguments
    /// become the children, in order.</item>
    /// <item>Passing an empty children array produces an element node with the className applied and no
    /// children.</item>
    /// <item><c>V.Custom&lt;T&gt;</c> preserves its generic type argument as the element type while applying
    /// the same positional-className + params-children shape.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VParamsOverloadTests
    {
        [Test]
        public void Given_PositionalClassNameAndParamsChildren_When_Div_Then_AppliesClassNameAndChildren()
        {
            // Act
            var node = V.Div("flex-col",
                V.Label(text: "a"),
                V.Label(text: "b"));

            // Assert
            Assert.That((node is ElementNode, node.ElementType, node.Children.Length),
                Is.EqualTo((true, typeof(VisualElement), 2)));
            Assert.That(node.ClassNames, Is.EqualTo(new[] { "flex-col" }));
        }

        [Test]
        public void Given_PositionalClassNameAndEmptyChildren_When_Div_Then_AppliesClassNameWithNoChildren()
        {
            // Act
            var node = V.Div("empty", Array.Empty<VNode>());

            // Assert
            Assert.That(node.ClassNames, Is.EqualTo(new[] { "empty" }));
            Assert.That(node.Children.Length, Is.EqualTo(0));
        }

        [Test]
        public void Given_PositionalClassNameAndParamsChildren_When_Custom_Then_PreservesGenericElementType()
        {
            // Act
            var node = V.Custom<ScrollView>("custom-scroll",
                V.Label(text: "child"));

            // Assert
            Assert.That((node.ElementType, node.Children.Length), Is.EqualTo((typeof(ScrollView), 1)));
            Assert.That(node.ClassNames, Is.EqualTo(new[] { "custom-scroll" }));
        }

        [Test]
        public void Given_PositionalClassNameAndParamsChildren_When_ScrollView_Then_AppliesScrollViewElementType()
        {
            // Act
            var node = V.ScrollView("scroll",
                V.Label(text: "x"));

            // Assert
            Assert.That((node.ElementType, node.Children.Length), Is.EqualTo((typeof(ScrollView), 1)));
            Assert.That(node.ClassNames, Is.EqualTo(new[] { "scroll" }));
        }

        [Test]
        public void Given_PositionalClassNameAndParamsChildren_When_Button_Then_AppliesButtonElementTypeAndChildren()
        {
            // Act — icon + label children, no onClick (decorative / handled elsewhere).
            var node = V.Button("btn",
                V.Label(text: "icon"),
                V.Label(text: "label"));

            // Assert
            Assert.That((node.ElementType, node.Children.Length), Is.EqualTo((typeof(Button), 2)));
            Assert.That(node.ClassNames, Is.EqualTo(new[] { "btn" }));
        }

        [Test]
        public void Given_PositionalClassNameAndTextSecondArg_When_Button_Then_ResolvesToLongFormText()
        {
            // Act — a positional string second arg must bind the long-form `text` param (no implicit
            // string→VNode exists), not the params-children overload.
            var node = V.Button("btn", "press");

            // Assert — no children; the overload resolution picked the long-form (text-bearing) Button.
            Assert.That(node.Children.Length, Is.EqualTo(0));
        }
    }
}
