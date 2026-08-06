using System;
using System.Linq;

namespace Velvet.CohesionReport
{
    internal static class Program
    {
        private const int OutlierCount = 15;

        private static int Main()
        {
            var types = PackageTypeMetrics.MeasureTypes().ToList();
            Console.WriteLine(
                "Ca and Ce are syntax-level simple-name matches across files, not semantic references.");
            Console.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "LCOM1", t => t.Lcom1, OutlierCount));
            Console.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "LCOM HS", t => t.LcomHs, OutlierCount));
            Console.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "RFC", t => t.Rfc, OutlierCount));
            Console.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "Ce (syntax)", t => t.Ce, OutlierCount));
            Console.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "Ca (syntax)", t => t.Ca, OutlierCount));
            Console.WriteLine(PackageTypeMetrics.FormatTypeOutliers(
                types, "instability", t => t.Instability, OutlierCount));
            Console.WriteLine(PackageTypeMetrics.FormatTypeOutliers(types, "lines", t => t.Lines, OutlierCount));

            var assemblies = PackageTypeMetrics.MeasureAssemblies().ToList();
            Console.WriteLine("Assembly Ca/Ce/instability from asmdef reference edges only.");
            Console.WriteLine(PackageTypeMetrics.FormatAssemblyTable(assemblies));
            return 0;
        }
    }
}
