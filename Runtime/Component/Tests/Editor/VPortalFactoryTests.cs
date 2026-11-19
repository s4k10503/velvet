using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <c>V.Portal(layer:)</c> does with its <see cref="UILayer"/> argument at construction:
    /// <list type="bullet">
    /// <item>A value naming no member of the enum is refused synchronously with an
    /// <see cref="ArgumentOutOfRangeException"/> that names the parameter, carries the value, and names
    /// the API and the enum in its message.</item>
    /// <item>Every member builds a node carrying that layer.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VPortalFactoryTests
    {
        private const UILayer UnnamedLayer = (UILayer)99;

        [Test]
        public void Given_ALayerNamingNoMember_When_VPortalIsCalled_Then_ItThrowsArgumentOutOfRange()
        {
            // Act + Assert
            Assert.That(() => V.Portal(UnnamedLayer), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_ALayerNamingNoMember_When_VPortalThrows_Then_TheParamNameIsLayer()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => V.Portal(UnnamedLayer));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("layer"));
        }

        [Test]
        public void Given_ALayerNamingNoMember_When_VPortalThrows_Then_TheMessageNamesTheApiAndTheEnum()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => V.Portal(UnnamedLayer));

            // Assert
            Assert.That((ex.Message.Contains("V.Portal"), ex.Message.Contains("UILayer")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALayerNamingNoMember_When_VPortalThrows_Then_TheActualValueIsTheCast()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => V.Portal(UnnamedLayer));

            // Assert
            Assert.That(ex.ActualValue, Is.EqualTo(UnnamedLayer));
        }

        // GREEN_ON_BASE(characterization): the base builds a node for every member as this branch does.
        // What the case answers for is the check this branch adds: reverse it and every member is refused.
        [Test]
        public void Given_EveryMemberOfUILayer_When_VPortalIsCalled_Then_EachBuildsANodeCarryingIt()
        {
            // Arrange
            var members = (UILayer[])Enum.GetValues(typeof(UILayer));

            // Act
            var carried = new List<string>();
            foreach (var member in members)
            {
                carried.Add(V.Portal(member).Layer?.ToString() ?? "null");
            }

            // Assert
            Assert.That(string.Join(",", carried), Is.EqualTo(string.Join(",", members.Select(m => m.ToString()))));
        }
    }
}
