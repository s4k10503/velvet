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
    /// and the contract of <see cref="StyleRecipe"/>, the single-class-string sibling with named variant axes:
    /// <list type="bullet">
    /// <item>Output is composed as base class, then each selected axis value's classes, then matching compound
    /// classes, then the optional trailing extra string.</item>
    /// <item>An axis the caller does not select falls back to its default variant value; with no default it
    /// contributes nothing.</item>
    /// <item>A repeated axis keeps only the last selected value: every earlier occurrence is dropped and the
    /// default for that axis is not re-injected, even when the winning value has no classes.</item>
    /// <item>A compound variant emits its class only when every condition matches the deduplicated (last-wins)
    /// selection, so a compound keyed on an overridden value does not match.</item>
    /// <item>An unknown axis or unknown value contributes nothing rather than raising.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StyleSlotRecipeTests
    {
        private StyleRecipe _sut;

        [SetUp]
        public void SetUp()
        {
            _sut = new StyleRecipe(
                "action-btn",
                new Dictionary<string, Dictionary<string, string>>
                {
                    ["visual"] = new()
                    {
                        ["primary"] = "btn-primary border-0",
                        ["secondary"] = "btn-secondary",
                        ["custom"] = "btn-custom",
                    },
                    ["size"] = new()
                    {
                        ["md"] = "border text-xl px-5",
                        ["lg"] = "h-24 rounded-3xl text-2xl",
                    }
                },
                defaultVariants: new Dictionary<string, string>
                {
                    ["visual"] = "primary",
                    ["size"] = "md"
                });
        }

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

        [Test]
        public void Given_NamedVariants_When_Applied_Then_ExpandsToBasePlusEachAxisClasses()
        {
            // Act
            var result = _sut.Apply(("visual", "secondary"), ("size", "lg"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn btn-secondary h-24 rounded-3xl text-2xl"));
        }

        [Test]
        public void Given_UnspecifiedAxes_When_Applied_Then_DefaultVariantsFillThem()
        {
            // Act
            var result = _sut.Apply();

            // Assert
            Assert.That(result, Is.EqualTo("action-btn btn-primary border-0 border text-xl px-5"));
        }

        [Test]
        public void Given_OneAxisSelected_When_Applied_Then_DefaultUsedForTheOther()
        {
            // Act
            var result = _sut.Apply(("visual", "custom"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn btn-custom border text-xl px-5"));
        }

        [Test]
        public void Given_ExtraString_When_Applied_Then_AppendedAfterVariantClasses()
        {
            // Act
            var result = _sut.Apply("mr-auto", ("visual", "custom"), ("size", "lg"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn btn-custom h-24 rounded-3xl text-2xl mr-auto"));
        }

        [Test]
        public void Given_DuplicatedAxis_When_Applied_Then_OnlyLastValueClassesEmitted()
        {
            // Act
            var result = _sut.Apply(("visual", "primary"), ("visual", "secondary"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn btn-secondary border text-xl px-5"));
        }

        [Test]
        public void Given_AxisDuplicatedThreeTimes_When_Applied_Then_EveryEarlierValueDropped()
        {
            // Act
            var result = _sut.Apply(("visual", "primary"), ("visual", "secondary"), ("visual", "custom"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn btn-custom border text-xl px-5"));
        }

        [Test]
        public void Given_DuplicatedAxisWithUnknownLastValue_When_Applied_Then_AxisEmitsNothing()
        {
            // Act
            var result = _sut.Apply(("visual", "primary"), ("visual", "bogus"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn border text-xl px-5"));
        }

        [Test]
        public void Given_DuplicatedDefaultedAxis_When_Applied_Then_LastValueWinsAndDefaultSuppressed()
        {
            // Act
            var result = _sut.Apply(("size", "md"), ("size", "lg"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn h-24 rounded-3xl text-2xl btn-primary border-0"));
        }

        [Test]
        public void Given_CompoundKeyedOnOverriddenValue_When_AxisDuplicated_Then_CompoundDoesNotMatch()
        {
            // Arrange
            var sut = new StyleRecipe(
                "btn",
                new Dictionary<string, Dictionary<string, string>>
                {
                    ["visual"] = new() { ["primary"] = "v-primary", ["secondary"] = "v-secondary" },
                },
                compoundVariants: new[]
                {
                    new StyleRecipe.CompoundVariant(
                        new Dictionary<string, string> { ["visual"] = "primary" },
                        "compound-primary"),
                });

            // Act
            var result = sut.Apply(("visual", "primary"), ("visual", "secondary"));

            // Assert
            Assert.That(result, Is.EqualTo("btn v-secondary"));
        }

        [Test]
        public void Given_CompoundVariant_When_AllConditionsMatch_Then_CompoundClassAppended()
        {
            // Arrange
            var sut = new StyleRecipe(
                "btn",
                new Dictionary<string, Dictionary<string, string>>
                {
                    ["visual"] = new() { ["primary"] = "bg-blue", ["danger"] = "bg-red" },
                    ["size"] = new() { ["sm"] = "text-sm", ["lg"] = "text-lg" },
                },
                compoundVariants: new[]
                {
                    new StyleRecipe.CompoundVariant(
                        new Dictionary<string, string> { ["visual"] = "primary", ["size"] = "lg" },
                        "uppercase font-bold")
                });

            // Act
            var result = sut.Apply(("visual", "primary"), ("size", "lg"));

            // Assert
            Assert.That(result, Is.EqualTo("btn bg-blue text-lg uppercase font-bold"));
        }

        [Test]
        public void Given_CompoundVariant_When_OnlySomeConditionsMatch_Then_CompoundClassOmitted()
        {
            // Arrange
            var sut = new StyleRecipe(
                "btn",
                new Dictionary<string, Dictionary<string, string>>
                {
                    ["visual"] = new() { ["primary"] = "bg-blue", ["danger"] = "bg-red" },
                    ["size"] = new() { ["sm"] = "text-sm", ["lg"] = "text-lg" },
                },
                compoundVariants: new[]
                {
                    new StyleRecipe.CompoundVariant(
                        new Dictionary<string, string> { ["visual"] = "primary", ["size"] = "lg" },
                        "uppercase font-bold")
                });

            // Act
            var result = sut.Apply(("visual", "primary"), ("size", "sm"));

            // Assert
            Assert.That(result, Is.EqualTo("btn bg-blue text-sm"));
        }

        [Test]
        public void Given_UnknownAxis_When_Applied_Then_IgnoredAndDefaultsStillApply()
        {
            // Act
            var result = _sut.Apply(("unknown", "value"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn btn-primary border-0 border text-xl px-5"));
        }

        [Test]
        public void Given_UnknownValueForKnownAxis_When_Applied_Then_AxisContributesNothing()
        {
            // Act
            var result = _sut.Apply(("visual", "nonexistent"), ("size", "md"));

            // Assert
            Assert.That(result, Is.EqualTo("action-btn border text-xl px-5"));
        }

        [Test]
        public void Given_NoDefaultVariants_When_AppliedWithoutSelections_Then_ReturnsBaseOnly()
        {
            // Arrange
            var sut = new StyleRecipe(
                "bare",
                new Dictionary<string, Dictionary<string, string>>
                {
                    ["color"] = new() { ["red"] = "text-red" },
                });

            // Act
            var result = sut.Apply();

            // Assert
            Assert.That(result, Is.EqualTo("bare"));
        }
    }
}
