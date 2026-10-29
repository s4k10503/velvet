using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which class tokens <c>FiberElementFactory.ApplyClassNames</c> puts on the class list of an element
    /// it creates: a plain class is added, and a token another pass owns (a variant, a custom property, an
    /// arbitrary font or line height) is not, whatever mark it carries and with an important modifier too.
    /// </summary>
    [TestFixture]
    internal sealed class ApplyClassNamesTokenOwnershipTests
    {
        // GREEN_ON_BASE(characterization): the base skips each of these tokens by a check of its own, and
        // ApplyClassNames now reaches those checks only for a token carrying one of the marks it looks for; this
        // holds every kind of token to still reaching its check.
        [TestCase("hover:bg-red-500")]
        [TestCase("dark:p-2")]
        [TestCase("[&>*]:p-2")]
        [TestCase("first:p-2")]
        [TestCase("[&:nth-child(2)]:p-2")]
        [TestCase("has-[:checked]:p-2")]
        [TestCase("data-[state=open]:p-2")]
        [TestCase("aria-[expanded=true]:p-2")]
        [TestCase("supports-[display:grid]:p-2")]
        [TestCase("[--brand:red]")]
        [TestCase("w-(--w)")]
        [TestCase("font-[Arial]")]
        [TestCase("leading-[1.5]")]
        [TestCase("!font-[Arial]")]
        [TestCase("leading-[1.5]!")]
        public void Given_ATokenAnotherPassOwns_When_ClassesAreApplied_Then_NoClassIsAdded(string token)
        {
            // Arrange
            var element = new VisualElement();

            // Act
            FiberElementFactory.ApplyClassNames(element, new[] { token });

            // Assert
            Assert.IsEmpty(element.GetClasses().ToArray());
        }

        // GREEN_ON_BASE(characterization): the base adds each plain class by the same routing; this holds the
        // tokens that now skip the checks to it.
        [Test]
        public void Given_PlainClasses_When_ClassesAreApplied_Then_EachIsOnTheClassList()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            FiberElementFactory.ApplyClassNames(element, new[] { "flex", "flex-row", "bg-red-500" });

            // Assert
            CollectionAssert.AreEquivalent(new[] { "flex", "flex-row", "bg-red-500" }, element.GetClasses().ToArray());
        }
    }
}
