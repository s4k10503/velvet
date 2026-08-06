using System;
using System.Linq;
using Velvet.CohesionReport;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Guards the cohesion scanner and its formatters. Rankings are printed by
    /// <c>scripts/test_quality/cohesion_report.py</c>, not here.
    /// </summary>
    public sealed class PackageCohesionReportTests
    {
        private const int OutlierCount = 15;
        private const int MinimumTypeCount = 400;

        [Fact]
        public void Given_ThePackageSources_When_TypeMetricsAreMeasured_Then_TheScanFoundTypesToReport()
        {
            // Arrange
            var types = PackageTypeMetrics.MeasureTypes();

            // Act
            var count = types.Count;

            // Assert
            Assert.True(count >= MinimumTypeCount, $"Expected at least {MinimumTypeCount} types, found {count}.");
        }

        [Fact]
        public void Given_TypeMetrics_When_OutliersAreFormatted_Then_TheLineNamesTheMetric()
        {
            // Arrange
            var types = PackageTypeMetrics.MeasureTypes().ToList();

            // Act
            var formatted = PackageTypeMetrics.FormatTypeOutliers(types, "LCOM HS", t => t.LcomHs, OutlierCount);

            // Assert
            Assert.StartsWith("LCOM HS:", formatted);
        }

        [Fact]
        public void Given_EnoughTypes_When_OutliersAreFormatted_Then_TheTopNListHasNEntries()
        {
            // Arrange
            var types = PackageTypeMetrics.MeasureTypes().ToList();
            Assume.NotEmpty(types, "package types were located");

            // Act
            var formatted = PackageTypeMetrics.FormatTypeOutliers(types, "lines", t => t.Lines, OutlierCount);
            var entries = formatted[(formatted.IndexOf(':') + 1)..]
                .Split("; ", StringSplitOptions.RemoveEmptyEntries);

            // Assert
            Assert.Equal(OutlierCount, entries.Length);
        }

        [Fact]
        public void Given_ThePackageAsmdefs_When_AssemblyMetricsAreMeasured_Then_TheScanFoundAssembliesToReport()
        {
            // Arrange
            var assemblies = PackageTypeMetrics.MeasureAssemblies();

            // Act
            var count = assemblies.Count;

            // Assert
            Assert.True(count >= 5, $"Expected at least 5 package assemblies, found {count}.");
        }

        [Fact]
        public void Given_AssemblyMetrics_When_TheTableIsFormatted_Then_EveryAssemblyHasALine()
        {
            // Arrange
            var assemblies = PackageTypeMetrics.MeasureAssemblies().ToList();
            Assume.NotEmpty(assemblies, "package assemblies were located");

            // Act
            var formatted = PackageTypeMetrics.FormatAssemblyTable(assemblies);
            var lines = formatted.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            // Assert
            Assert.Equal(assemblies.Count, lines.Length);
        }
    }
}
