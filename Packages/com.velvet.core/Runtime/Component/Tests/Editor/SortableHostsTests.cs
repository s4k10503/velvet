using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the compiler transform leaves V.SortableContext's and V.Sortable's components unwoven.
    /// </summary>
    [TestFixture]
    internal sealed class SortableHostsTests
    {
        // GREEN_ON_BASE(characterization): the base already leaves the body unwoven; writing `true` for the `Compiler = false` above SortableContextHost.Render reddens it.
        [Test]
        public void Given_TheSortableContextsComponent_When_TheCompilerTransformHasRun_Then_ItLeftTheBodyUnwoven()
        {
            // Arrange
            var render = typeof(SortableContextHost).GetMethod(nameof(SortableContextHost.Render),
                BindingFlags.Static | BindingFlags.Public);

            // Act
            var report = render!.GetCustomAttribute<MemoizationReportAttribute>();

            // Assert
            Assert.That(report, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already leaves the body unwoven; writing `true` for the `Compiler = false` above SortableHost.Render reddens it.
        [Test]
        public void Given_TheSortablesComponent_When_TheCompilerTransformHasRun_Then_ItLeftTheBodyUnwoven()
        {
            // Arrange
            var render = typeof(SortableHost).GetMethod(nameof(SortableHost.Render), BindingFlags.Static | BindingFlags.Public);

            // Act
            var report = render!.GetCustomAttribute<MemoizationReportAttribute>();

            // Assert
            Assert.That(report, Is.Null);
        }
    }
}
