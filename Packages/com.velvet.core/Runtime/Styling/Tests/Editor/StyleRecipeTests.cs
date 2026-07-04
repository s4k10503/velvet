using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="StyleRecipe"/>, the class-name builder with named variant axes.
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
    internal sealed class StyleRecipeTests
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
