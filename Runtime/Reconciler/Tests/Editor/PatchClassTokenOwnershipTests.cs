using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which class tokens a re-render puts on, and takes off, the class list of an element it patches: a
    /// plain class is added and removed, and a token another pass owns (a variant, a custom property, an arbitrary
    /// font or line height) is neither, whatever mark it carries and with an important modifier too — the same
    /// split <see cref="ApplyClassNamesTokenOwnershipTests"/> holds the create path to.
    /// </summary>
    /// <remarks>
    /// Diverges from <see cref="ApplyClassNamesTokenOwnershipTests"/>, which applies the tokens to a bare element:
    /// these elements are mounted, so a variant that applies puts a class of its own on the list, and the owned
    /// cases name the token rather than expect the list unchanged.
    /// </remarks>
    [TestFixture]
    internal sealed class PatchClassTokenOwnershipTests
    {
        private static StateUpdater<int> s_setStep;
        private static Func<int, string> s_classFor;

        [SetUp]
        public void SetUp()
        {
            s_setStep = default;
            s_classFor = _ => string.Empty;
        }

        [Component]
        private static VNode BoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(className: s_classFor(step), name: "box");
        }

        private static string[] ClassesAfterRerender(string before, string after)
        {
            s_classFor = step => step == 0 ? before : after;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(BoxRender));
            mounted.FlushStateForTest();

            s_setStep.Invoke(1);
            mounted.FlushStateForTest();

            // The framework's own marker classes are not the classes the tokens name.
            return root.Q<VisualElement>("box").GetClasses().Where(c => !c.StartsWith("velvet-")).ToArray();
        }

        // GREEN_ON_BASE(characterization): the base adds each plain class through the same routing; this holds the
        // tokens that skip the ownership checks to it.
        [Test]
        public void Given_PlainClassesAdded_When_ItReRenders_Then_EachIsOnTheClassList()
        {
            // Act
            var classes = ClassesAfterRerender("flex", "flex flex-row bg-red-500");

            // Assert
            CollectionAssert.AreEquivalent(new[] { "flex", "flex-row", "bg-red-500" }, classes);
        }

        // GREEN_ON_BASE(characterization): the base removes each plain class through the same routing; this holds the
        // tokens that skip the ownership checks to it.
        [Test]
        public void Given_PlainClassesRemoved_When_ItReRenders_Then_NoneOfThemIsOnTheClassList()
        {
            // Act
            var classes = ClassesAfterRerender("flex flex-row bg-red-500", "flex");

            // Assert
            CollectionAssert.AreEquivalent(new[] { "flex" }, classes);
        }

        // GREEN_ON_BASE(characterization): the base skips each of these tokens by a check of its own, and AddClass now
        // reaches those checks only for a token carrying one of the marks they look for; this holds every kind of
        // token to still reaching its check.
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
        public void Given_ATokenAnotherPassOwns_When_ItIsAddedOnReRender_Then_TheTokenIsNotOnTheClassList(string token)
        {
            // Act
            var classes = ClassesAfterRerender("flex", "flex " + token);

            // Assert — the token alone: a variant's payload may add a class of its own once the variant applies.
            Assert.IsFalse(classes.Contains(token));
        }

        // GREEN_ON_BASE(characterization): the base strips the important modifier before it adds the class, and a
        // plain class carrying one has no mark but the '!', so this is what reaches it.
        [TestCase("!p-4")]
        [TestCase("p-4!")]
        public void Given_AnImportantPlainClass_When_ItIsAddedOnReRender_Then_TheTokenWithItsModifierIsNotOnTheClassList(string token)
        {
            // Act
            var classes = ClassesAfterRerender("flex", "flex " + token);

            // Assert
            Assert.IsFalse(classes.Contains(token));
        }

        // GREEN_ON_BASE(characterization): the base strips the important modifier before it removes the class, and a
        // plain class carrying one has no mark but the '!'; this holds that removal to leaving the class list as the
        // add left it.
        [TestCase("!p-4")]
        [TestCase("p-4!")]
        public void Given_AnImportantPlainClass_When_ItIsRemovedOnReRender_Then_TheClassListIsTheOneWithoutIt(string token)
        {
            // Act
            var classes = ClassesAfterRerender("flex " + token, "flex");

            // Assert
            CollectionAssert.AreEquivalent(new[] { "flex" }, classes);
        }

        // GREEN_ON_BASE(characterization): the base removes the token's class by the same routing; a token another
        // pass owns never entered the list, and this holds its removal to leaving the list alone.
        [TestCase("hover:bg-red-500")]
        [TestCase("[--brand:red]")]
        [TestCase("font-[Arial]")]
        public void Given_ATokenAnotherPassOwns_When_ItIsRemovedOnReRender_Then_TheOtherClassesStay(string token)
        {
            // Act
            var classes = ClassesAfterRerender("flex " + token, "flex");

            // Assert
            CollectionAssert.AreEquivalent(new[] { "flex" }, classes);
        }
    }
}
