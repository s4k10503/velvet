using NUnit.Framework;
using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="SearchParams"/> as the web's <c>URLSearchParams</c>: an ordered list of entries, which
    /// <c>keys()</c>, <c>set</c>, <c>delete</c>, <c>sort</c> and <c>toString</c> read and edit in that order.
    /// </summary>
    [TestFixture]
    internal sealed class UrlSearchParamsParityTests
    {
        private static SearchParams ABA()
        {
            var searchParams = new SearchParams();
            searchParams.Append("a", "1");
            searchParams.Append("b", "2");
            searchParams.Append("a", "3");
            return searchParams;
        }

        [Test]
        public void Given_ARepeatedName_When_TheKeysAreRead_Then_EachEntryNamesItselfInOrder()
        {
            // Arrange
            var searchParams = ABA();

            // Act
            var keys = searchParams.Keys;

            // Assert
            Assert.That(keys, Is.EqualTo(new[] { "a", "b", "a" }));
        }

        [Test]
        public void Given_ARepeatedName_When_Serialized_Then_TheEntriesKeepTheirOrder()
        {
            // Arrange
            var searchParams = ABA();

            // Act
            var text = searchParams.ToString();

            // Assert
            Assert.That(text, Is.EqualTo("a=1&b=2&a=3"));
        }

        [Test]
        public void Given_ARepeatedName_When_Set_Then_TheFirstEntryTakesTheValueAndTheOthersGo()
        {
            // Arrange
            var searchParams = ABA();

            // Act
            searchParams.Set("a", "9");

            // Assert
            Assert.That(searchParams.ToString(), Is.EqualTo("a=9&b=2"));
        }

        [Test]
        public void Given_ANameAndAValue_When_Deleted_Then_OnlyTheEntryHoldingThatValueGoes()
        {
            // Arrange
            var searchParams = ABA();

            // Act
            searchParams.Delete("a", "3");

            // Assert
            Assert.That(searchParams.ToString(), Is.EqualTo("a=1&b=2"));
        }

        [Test]
        public void Given_EntriesOutOfOrder_When_Sorted_Then_TheyOrderByNameKeepingEachNamesOrder()
        {
            // Arrange
            var searchParams = new SearchParams();
            searchParams.Append("b", "1");
            searchParams.Append("a", "2");
            searchParams.Append("b", "0");

            // Act
            searchParams.Sort();

            // Assert
            Assert.That(searchParams.ToString(), Is.EqualTo("a=2&b=1&b=0"));
        }

        [Test]
        public void Given_ASpace_When_Serialized_Then_ItIsAPlus()
        {
            // Arrange
            var searchParams = new SearchParams();
            searchParams.Append("q", "a b");

            // Act
            var text = searchParams.ToString();

            // Assert
            Assert.That(text, Is.EqualTo("q=a+b"));
        }

        [Test]
        public void Given_ARepeatedName_When_TheSizeIsRead_Then_ItCountsEveryEntry()
        {
            // Arrange
            var searchParams = ABA();

            // Act
            var size = searchParams.Size;

            // Assert
            Assert.That(size, Is.EqualTo(3));
        }
    }
}
