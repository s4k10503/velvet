using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="StyleSlotRecipe"/>, the slot-recipe builder that resolves class names for
    /// every slot of a multi-part UI pattern in one place.
    /// <list type="bullet">
    /// <item>With no selections each slot resolves to its base class only.</item>
    /// <item>A selected axis appends the axis value's per-slot override after that slot's base class.</item>
    /// <item>Multiple selected axes append their per-slot overrides in selection order.</item>
    /// <item>A repeated axis keeps only the last selected value, so the final per-slot classes reflect the last write.</item>
    /// <item>Default variants supply a value for any axis the caller did not select.</item>
    /// <item>A compound variant appends its per-slot classes only when every condition value matches the selection.</item>
    /// <item>Indexing a slot that the pattern does not declare returns the empty string, as does indexing the
    /// default (uninitialized) <see cref="StyleSlotClasses"/>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StyleSlotRecipeTests
    {
        [Test]
        public void Given_NoVariants_When_Applied_Then_EachSlotResolvesToItsBaseClass()
        {
            // Arrange
            var sut = new StyleSlotRecipe(new Dictionary<string, string>
            {
                ["root"] = "bg-neutral rounded-md",
                ["title"] = "text-base font-bold",
            });

            // Act
            var s = sut.Apply();

            // Assert
            Assert.That(s["root"], Is.EqualTo("bg-neutral rounded-md"));
            // The title slot's base class is asserted in its own test.
        }

        [Test]
        public void Given_NoVariants_When_Applied_Then_SecondSlotResolvesToItsBaseClass()
        {
            // Arrange
            var sut = new StyleSlotRecipe(new Dictionary<string, string>
            {
                ["root"] = "bg-neutral rounded-md",
                ["title"] = "text-base font-bold",
            });

            // Act
            var s = sut.Apply();

            // Assert
            Assert.That(s["title"], Is.EqualTo("text-base font-bold"));
        }

        [Test]
        public void Given_SelectedAxis_When_Applied_Then_AppendsPerSlotOverrideAfterBase()
        {
            // Arrange
            var sut = new StyleSlotRecipe(
                new Dictionary<string, string>
                {
                    ["root"] = "p-4",
                    ["title"] = "text-base",
                },
                new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                {
                    ["size"] = new()
                    {
                        ["lg"] = new()
                        {
                            ["root"] = "p-8",
                            ["title"] = "text-xl",
                        }
                    }
                });

            // Act
            var s = sut.Apply(("size", "lg"));

            // Assert
            Assert.That(s["root"], Is.EqualTo("p-4 p-8"));
            // The title slot override is asserted in its own test.
        }

        [Test]
        public void Given_SelectedAxis_When_Applied_Then_AppendsOverrideToEverySlotItTargets()
        {
            // Arrange
            var sut = new StyleSlotRecipe(
                new Dictionary<string, string>
                {
                    ["root"] = "p-4",
                    ["title"] = "text-base",
                },
                new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                {
                    ["size"] = new()
                    {
                        ["lg"] = new()
                        {
                            ["root"] = "p-8",
                            ["title"] = "text-xl",
                        }
                    }
                });

            // Act
            var s = sut.Apply(("size", "lg"));

            // Assert
            Assert.That(s["title"], Is.EqualTo("text-base text-xl"));
        }

        [Test]
        public void Given_RepeatedAxis_When_Applied_Then_LastSelectedValueWins()
        {
            // Arrange
            var sut = new StyleSlotRecipe(
                new Dictionary<string, string>
                {
                    ["root"] = "base",
                },
                new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                {
                    ["size"] = new()
                    {
                        ["lg"] = new() { ["root"] = "p-8" },
                        ["sm"] = new() { ["root"] = "p-2" },
                    }
                });

            // Act
            var s = sut.Apply(("size", "lg"), ("size", "sm"));

            // Assert
            Assert.That(s["root"], Is.EqualTo("base p-2"));
        }

        [Test]
        public void Given_DefaultVariants_When_AxisNotSelected_Then_DefaultValueIsApplied()
        {
            // Arrange
            var sut = new StyleSlotRecipe(
                new Dictionary<string, string>
                {
                    ["root"] = "base",
                },
                new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                {
                    ["theme"] = new()
                    {
                        ["dark"] = new() { ["root"] = "bg-black" },
                        ["light"] = new() { ["root"] = "bg-white" },
                    }
                },
                defaultVariants: new Dictionary<string, string> { ["theme"] = "dark" });

            // Act
            var s = sut.Apply();

            // Assert
            Assert.That(s["root"], Is.EqualTo("base bg-black"));
        }

        [Test]
        public void Given_UndeclaredSlot_When_Indexed_Then_ReturnsEmptyString()
        {
            // Arrange
            var sut = new StyleSlotRecipe(new Dictionary<string, string>
            {
                ["root"] = "p-4",
            });

            // Act
            var s = sut.Apply();

            // Assert
            Assert.That(s["nonexistent"], Is.EqualTo(""));
        }

        [Test]
        public void Given_MultipleSelectedAxes_When_Applied_Then_AppendsEachOverrideInSelectionOrder()
        {
            // Arrange
            var sut = new StyleSlotRecipe(
                new Dictionary<string, string>
                {
                    ["root"] = "base",
                },
                new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                {
                    ["size"] = new()
                    {
                        ["lg"] = new() { ["root"] = "text-lg" },
                    },
                    ["color"] = new()
                    {
                        ["red"] = new() { ["root"] = "text-red" },
                    }
                });

            // Act
            var s = sut.Apply(("size", "lg"), ("color", "red"));

            // Assert
            Assert.That(s["root"], Is.EqualTo("base text-lg text-red"));
        }

        [Test]
        public void Given_CompoundVariant_When_AllConditionsMatch_Then_AppendsItsPerSlotClasses()
        {
            // Arrange
            var sut = new StyleSlotRecipe(
                new Dictionary<string, string>
                {
                    ["root"] = "base",
                    ["label"] = "text-sm",
                },
                new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                {
                    ["size"] = new() { ["lg"] = new() { ["root"] = "p-8" } },
                    ["color"] = new() { ["red"] = new() { ["root"] = "bg-red" } },
                },
                compoundVariants: new[]
                {
                    new StyleSlotRecipe.SlotCompoundVariant(
                        new Dictionary<string, string> { ["size"] = "lg", ["color"] = "red" },
                        new Dictionary<string, string> { ["label"] = "font-bold" })
                });

            // Act
            var s = sut.Apply(("size", "lg"), ("color", "red"));

            // Assert
            Assert.That(s["label"], Is.EqualTo("text-sm font-bold"));
        }

        [Test]
        public void Given_DefaultSlotClasses_When_Indexed_Then_ReturnsEmptyString()
        {
            // Arrange
            var s = default(StyleSlotClasses);

            // Act + Assert
            Assert.That(s["anything"], Is.EqualTo(""));
        }

        [Test]
        public void Given_AnEarlierSlotWithMoreOverrides_When_ALaterSlotFillsFewer_Then_TheLaterSlotDoesNotInheritTheEarlierSlotsClasses()
        {
            // Arrange — the root slot is targeted by both axes while the title slot is targeted by neither,
            // so a per-slot class buffer reused across slots would carry root's overrides into title unless
            // its unused tail is scrubbed between slots.
            var sut = new StyleSlotRecipe(
                new Dictionary<string, string>
                {
                    ["root"] = "base-root",
                    ["title"] = "base-title",
                },
                new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                {
                    ["size"] = new() { ["lg"] = new() { ["root"] = "p-8" } },
                    ["color"] = new() { ["red"] = new() { ["root"] = "bg-red" } },
                });

            // Act
            var s = sut.Apply(("size", "lg"), ("color", "red"));

            // Assert — title resolves to its base class only, with no ghost of root's p-8 / bg-red.
            Assert.That(s["title"], Is.EqualTo("base-title"));
        }

        [Test]
        public void Given_ADefaultForASelectedAxis_When_Applied_Then_TheExplicitSelectionWinsOverTheDefault()
        {
            // Arrange — the theme axis is both explicitly selected AND carries a default, so the default
            // must be skipped rather than appended as a second value for the same axis.
            var sut = new StyleSlotRecipe(
                new Dictionary<string, string>
                {
                    ["root"] = "base",
                },
                new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
                {
                    ["theme"] = new()
                    {
                        ["dark"] = new() { ["root"] = "bg-black" },
                        ["light"] = new() { ["root"] = "bg-white" },
                    }
                },
                defaultVariants: new Dictionary<string, string> { ["theme"] = "dark" });

            // Act
            var s = sut.Apply(("theme", "light"));

            // Assert — only the selected value's classes appear; the default's bg-black does not.
            Assert.That(s["root"], Is.EqualTo("base bg-white"));
        }
    }
}
