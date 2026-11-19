using NUnit.Framework;
using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <see cref="SearchParams.Empty"/> hands a reader.
    /// <list type="bullet">
    /// <item><see cref="SearchParams.Append"/> mutates the instance it is called on, so a reader that
    /// appends to what <see cref="SearchParams.Empty"/> returned must not reach any other reader.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class SearchParamsTests
    {
        [Test]
        public void Given_AppendedToOneReadOfEmpty_When_EmptyIsReadAgain_Then_OnlyTheAppendedOneCarriesTheValue()
        {
            // Arrange
            var appended = SearchParams.Empty;

            // Act
            appended.Append("q", "velvet");

            // Assert
            Assert.That(
                (appended: appended.Get("q"), reread: SearchParams.Empty.Get("q")),
                Is.EqualTo((appended: "velvet", reread: (string?)null)));
        }
    }
}
