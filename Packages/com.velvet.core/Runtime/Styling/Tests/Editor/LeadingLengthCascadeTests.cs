using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// An em or percentage <c>leading-[…]</c> is a length CSS computes on the element that declares it,
    /// while a unitless one is a number every descendant multiplies by its own font size. A descendant whose
    /// size differs therefore gets the declaring element's pixels where that element has an inline pixel
    /// size of its own, and the em tag where nothing between them changes the size. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class LeadingLengthCascadeTests
    {
        private static string MountAndReadText(VNode tree)
        {
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { tree });
            return scope.Root.Q<Label>().text;
        }

        [Test]
        public void Given_APercentLeadingOverALargerLabel_When_Reconciled_Then_TheLabelGetsTheDeclaringElementsPixels()
        {
            // Act
            var text = MountAndReadText(
                V.Div(className: "text-[20px] leading-[150%]", V.Label(className: "text-[40px]", text: "hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=30px>hi</line-height>"));
        }

        [Test]
        public void Given_AnEmLeadingOverALabelOfAScaleSize_When_Reconciled_Then_TheLabelGetsTheDeclaringElementsPixels()
        {
            // Act
            var text = MountAndReadText(
                V.Div(className: "text-[20px] leading-[1.5em]", V.Label(className: "text-lg", text: "hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=30px>hi</line-height>"));
        }

        [Test]
        public void Given_APercentLeadingOnAnElementInheritingAnInlineSize_When_Reconciled_Then_TheLabelGetsTheEmTag()
        {
            // Act — only the declaring element's own inline size is read.
            var text = MountAndReadText(
                V.Div(className: "text-[20px]",
                    V.Div(className: "leading-[150%]", V.Label(className: "text-[40px]", text: "hi"))));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }

        [Test]
        public void Given_AnEmLeadingWithNoSizeChangeBelow_When_Reconciled_Then_TheTextGetsTheEmTag()
        {
            // Act
            var text = MountAndReadText(V.Div(className: "text-[20px] leading-[1.5em]", V.Text("hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }

        [Test]
        public void Given_AUnitlessLeadingOverALargerLabel_When_Reconciled_Then_TheLabelGetsTheFactor()
        {
            // Act
            var text = MountAndReadText(
                V.Div(className: "text-[20px] leading-[1.5]", V.Label(className: "text-[40px]", text: "hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }

        [Test]
        public void Given_APercentLeadingOnAnElementWithAPercentInlineSize_When_Reconciled_Then_TheLabelGetsTheEmTag()
        {
            // Act — a percentage font size is relative to a size further up, which is not read either.
            var text = MountAndReadText(
                V.Div(className: "text-[150%] leading-[150%]", V.Label(className: "text-[40px]", text: "hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }

        [Test]
        public void Given_APercentLeadingOnAnElementSizedByAScaleClass_When_Reconciled_Then_TheLabelGetsTheEmTag()
        {
            // Act — the declaring element's size comes from a stylesheet variable, which is not read.
            var text = MountAndReadText(
                V.Div(className: "text-sm leading-[150%]", V.Label(className: "text-[40px]", text: "hi")));

            // Assert
            Assert.That(text, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }
    }
}
