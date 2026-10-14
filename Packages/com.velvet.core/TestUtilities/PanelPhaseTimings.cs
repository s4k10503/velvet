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
    /// once per forced layout/styles pass made while a case is open, which runs inside the case body or its
    /// setup, so its time is also part of whichever of those it ran in; <see cref="Case"/> the whole span from
    /// the start of <c>PanelTestBase.SetUp</c> to the end of <c>PanelTestBase.TearDown</c>, which spans the
    /// other phases and does not depend on whether the runner's own duration includes setup and teardown.
    /// A layout pass in a derived <c>TearDown</c> before it calls <c>base.TearDown</c> falls inside that span;
    /// one after it finds no case open and is dropped. Only the window <c>PanelTestBase</c> creates is
    /// measured, not a further window a fixture opens itself, and a case ignored for want of a graphics
    /// device records nothing. A failure to read the test context or to write the file is swallowed rather
    /// than failing a case. <see cref="Flush()"/> writes into the directory of the file <c>-testResults</c>
    /// names. Test-only. Must not be used from production code.
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

        /// <summary>
        /// One buffer of rows with the case they are attributed to and a cursor over what has been written.
        /// The static members below act on <see cref="Shared"/>; a fixture that needs a buffer of its own
        /// takes an instance, so it neither adds to nor flushes the run's rows.
        /// </summary>
        public sealed class Recorder
        {
            private readonly List<Row> _rows = new();
            private int _flushed;
            private string? _fixture;
            private string? _caseName;

            public IReadOnlyList<Row> Rows => _rows;

            /// <summary>
            /// Starts attributing phases to the test the runner is executing. Phases recorded while no case is
            /// open are dropped, so a layout pass from a fixture that is not a panel fixture adds no row.
            /// </summary>
            public void BeginCase() => BeginCase(() =>
            {
                var test = TestContext.CurrentContext.Test;
                return (test.ClassName ?? "", test.Name ?? "");
            });

            /// <summary>As <see cref="BeginCase()"/>, naming the case through <paramref name="identify"/>; a throw leaves no case open.</summary>
            public void BeginCase(Func<(string Fixture, string CaseName)> identify)
            {
                try
                {
                    (_fixture, _caseName) = identify();
                }
                catch (Exception)
                {
                    _fixture = null;
                    _caseName = null;
                }
            }

            public void EndCase()
            {
                _fixture = null;
                _caseName = null;
            }

            public void Record(string phase, double milliseconds)
            {
                if (_fixture == null || _caseName == null)
                {
                    return;
                }
                _rows.Add(new Row(_fixture, _caseName, phase, milliseconds));
            }

            /// <summary>Removes the rows of one case that have not been written yet.</summary>
            public void Discard(string fixture, string caseName)
            {
                for (var i = _rows.Count - 1; i >= _flushed; i--)
                {
                    if (_rows[i].Fixture == fixture && _rows[i].CaseName == caseName)
                    {
                        _rows.RemoveAt(i);
                    }
                }
            }

            /// <summary>
            /// Appends the rows not yet written to the CSV in the results directory, writing the header when
            /// the file does not exist. Does nothing when <paramref name="commandLineArgs"/> carries no
            /// <c>-testResults</c> (an interactive run) or the directory it names is absent, and swallows any
            /// I/O failure: timing must not fail a test.
            /// </summary>
            public void Flush(string[] commandLineArgs)
            {
                try
                {
                    var directory = ResultsDirectory(commandLineArgs);
                    if (directory == null || _flushed >= _rows.Count)
                    {
                        return;
                    }
                    Append(directory, _rows.GetRange(_flushed, _rows.Count - _flushed));
                    _flushed = _rows.Count;
                }
                catch (Exception)
                {
                }
            }
        }

        public static Recorder Shared { get; } = new();

        /// <summary>Every row recorded in this domain, flushed or not.</summary>
        public static IReadOnlyList<Row> Rows => Shared.Rows;

        public static void BeginCase() => Shared.BeginCase();

        public static void EndCase() => Shared.EndCase();

        public static void Record(string phase, double milliseconds) => Shared.Record(phase, milliseconds);

        public static void Discard(string fixture, string caseName) => Shared.Discard(fixture, caseName);

        /// <summary>Writes the run's rows not yet written; see <see cref="Recorder.Flush"/>.</summary>
        public static void Flush() => Shared.Flush(Environment.GetCommandLineArgs());

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
