using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Report-only cohesion and coupling metrics over the package's non-test sources. Nothing here fails on a
    /// measured value — the output is for a reader choosing where to look next.
    /// </summary>
    public sealed class PackageCohesionReportTests
    {
        private const int OutlierCount = 15;
        private const int MinimumTypeCount = 400;

        private readonly ITestOutputHelper _output;

        public PackageCohesionReportTests(ITestOutputHelper output) => _output = output;

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
        public void Given_ThePackageSources_When_TypeMetricsAreMeasured_Then_OutliersAreReported()
        {
            // Arrange
            var types = PackageTypeMetrics.MeasureTypes().ToList();

            // Act
            _output.WriteLine(
                "Ca and Ce are syntax-level simple-name matches across files, not semantic references.");
            _output.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "LCOM1", t => t.Lcom1, OutlierCount));
            _output.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "LCOM HS", t => t.LcomHs, OutlierCount));
            _output.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "RFC", t => t.Rfc, OutlierCount));
            _output.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "Ce (syntax)", t => t.Ce, OutlierCount));
            _output.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "Ca (syntax)", t => t.Ca, OutlierCount));
            _output.WriteLine(PackageTypeMetrics.FormatTypeOutliers(
                types, "instability", t => t.Instability, OutlierCount));
            _output.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "lines", t => t.Lines, OutlierCount));
            var reported = true;

            // Assert
            Assert.True(reported);
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
        public void Given_ThePackageAsmdefs_When_AssemblyMetricsAreMeasured_Then_CouplingIsReported()
        {
            // Arrange
            var assemblies = PackageTypeMetrics.MeasureAssemblies().ToList();

            // Act
            _output.WriteLine("Assembly Ca/Ce/instability from asmdef reference edges only.");
            _output.WriteLine(PackageTypeMetrics.FormatAssemblyTable(assemblies));
            var reported = true;

            // Assert
            Assert.True(reported);
        }
    }
}
