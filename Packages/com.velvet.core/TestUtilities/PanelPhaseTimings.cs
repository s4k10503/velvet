using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Wall-clock cost of the phases a <c>PanelTestBase</c> case spends outside its body, written as a CSV
    /// (<c>fixture,case,phase,milliseconds</c>) beside the run's results file so one EditMode run says what
    /// window creation, layout passes and teardown cost apart from the case itself.
    /// </summary>
    /// <remarks>
    /// Phases: <see cref="CreateShow"/> (window creation and <c>Show</c>) and <see cref="Teardown"/> (window
    /// <c>Close</c> and <c>DestroyImmediate</c>, not the unmount before it) once per case; <see cref="Layout"/>
    /// once per forced layout/styles pass, which runs inside the case body or its setup, so its time is also
    /// part of whichever of those it ran in; <see cref="Case"/> the whole span from the start of
    /// <c>PanelTestBase.SetUp</c> to the end of <c>PanelTestBase.TearDown</c>, which spans the other phases
    /// and does not depend on whether the runner's own duration includes setup and teardown.
    /// A failure to read the test context or to write the file is swallowed rather than failing a case, and
    /// <see cref="Flush()"/> writes only into the directory the <c>-testResults</c> argument names. Test-only. Must not be used from production code.
    /// </remarks>
    public static class PanelPhaseTimings
    {
        public const string CreateShow = "create_show";
        public const string Layout = "layout";
        public const string Teardown = "teardown";
        public const string Case = "case";

        public const string FileName = "panel-phase-timings.csv";
        public const string Header = "fixture,case,phase,milliseconds";

        private const string ResultsFlag = "-testResults";

        public readonly struct Row
        {
            public Row(string fixture, string caseName, string phase, double milliseconds)
            {
                Fixture = fixture;
                CaseName = caseName;
                Phase = phase;
                Milliseconds = milliseconds;
            }

            public string Fixture { get; }
            public string CaseName { get; }
            public string Phase { get; }
            public double Milliseconds { get; }
        }

        private static readonly List<Row> s_rows = new();
        private static int s_flushed;
        private static string? s_fixture;
        private static string? s_caseName;

        /// <summary>Every row recorded in this domain, flushed or not.</summary>
        public static IReadOnlyList<Row> Rows => s_rows;

        /// <summary>
        /// Starts attributing phases to the test the runner is executing. Phases recorded while no case is
        /// open are dropped, so a layout pass from a fixture that is not a panel fixture adds no row.
        /// </summary>
        public static void BeginCase()
        {
            try
            {
                var test = TestContext.CurrentContext.Test;
                s_fixture = test.ClassName ?? "";
                s_caseName = test.Name ?? "";
            }
            catch (Exception)
            {
                s_fixture = null;
                s_caseName = null;
            }
        }

        public static void EndCase()
        {
            s_fixture = null;
            s_caseName = null;
        }

        public static void Record(string phase, double milliseconds)
        {
            if (s_fixture == null || s_caseName == null)
            {
                return;
            }
            s_rows.Add(new Row(s_fixture, s_caseName, phase, milliseconds));
        }

        /// <summary>
        /// Appends the rows not yet written to the CSV in the results directory, writing the header when the
        /// file does not exist. Does nothing when the editor was not given <c>-testResults</c> (an interactive
        /// run) or the directory it names is absent, and swallows any I/O failure: timing must not fail a test.
        /// </summary>
        public static void Flush() => Flush(Environment.GetCommandLineArgs());

        public static void Flush(string[] commandLineArgs)
        {
            try
            {
                var directory = ResultsDirectory(commandLineArgs);
                if (directory == null || s_flushed >= s_rows.Count)
                {
                    return;
                }
                Append(directory, s_rows.GetRange(s_flushed, s_rows.Count - s_flushed));
                s_flushed = s_rows.Count;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Appends the rows to the CSV in the directory, writing the header when the file is absent.</summary>
        public static void Append(string directory, IReadOnlyList<Row> rows)
        {
            var path = Path.Combine(directory, FileName);
            var text = new StringBuilder();
            if (!File.Exists(path))
            {
                text.Append(Header).Append('\n');
            }
            foreach (var row in rows)
            {
                text.Append(Line(row)).Append('\n');
            }
            File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// The existing directory holding the file <c>-testResults</c> names, or null when the flag is absent,
        /// has no value or names a directory that does not exist. Nothing is created: the run directory is the
        /// caller's.
        /// </summary>
        public static string? ResultsDirectory(string[] commandLineArgs)
        {
            var at = Array.IndexOf(commandLineArgs, ResultsFlag);
            if (at < 0 || at + 1 >= commandLineArgs.Length)
            {
                return null;
            }
            var directory = Path.GetDirectoryName(Path.GetFullPath(commandLineArgs[at + 1]));
            return directory != null && Directory.Exists(directory) ? directory : null;
        }

        public static string Line(Row row) =>
            string.Join(",",
                Quote(row.Fixture),
                Quote(row.CaseName),
                row.Phase,
                row.Milliseconds.ToString("F3", CultureInfo.InvariantCulture));

        private static string Quote(string field) =>
            field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0
                ? field
                : "\"" + field.Replace("\"", "\"\"") + "\"";
    }
}
