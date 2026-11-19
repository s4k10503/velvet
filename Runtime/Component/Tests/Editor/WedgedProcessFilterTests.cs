using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which STAT values <c>.claude/hooks/lib/wedged.py</c> counts as a process that cannot be
    /// reaped. The report is gated on total memory, so a spelling the filter drops does not merely go
    /// unlisted — it keeps the set under the threshold that would have reported the rest of it.
    /// </summary>
    /// <remarks>
    /// `ps` composes STAT as a run-state letter followed by flags whose order the manual page does not
    /// fix, which is why the filter looks for `E` among them rather than at a position, and why the
    /// answer is asserted here against a supplied table rather than against whatever the machine
    /// running the tests happens to hold.
    /// </remarks>
    [TestFixture]
    internal sealed class WedgedProcessFilterTests
    {
        private const string FilterPath = ".claude/hooks/lib/wedged.py";

        private static readonly (string Stat, bool Counts)[] Table =
        {
            ("UE", true),
            ("UNE", true),
            ("U<E", true),
            ("UWE", true),
            ("UXE", true),
            ("UEs", true),

            ("U", false),
            ("S", false),
            ("SN", false),
            ("Ss", false),
            ("R", false),
            ("Z", false),
            ("IE", false),
            ("TE", false),
        };

        [Test]
        public void Given_TheStatTable_When_TheWedgeFilterReadsEach_Then_ItCountsExactlyTheExitingUninterruptibleOnes()
        {
            // Arrange
            var filter = Path.GetFullPath(FilterPath);
            Assume.That(File.Exists(filter), Is.True, $"Precondition: {FilterPath} exists");
            Assume.That(Counted(filter, "UE"), Is.True, "Precondition: python3 ran the filter");

            // Act
            var disagreements = Table
                .Where(row => Counted(filter, row.Stat) != row.Counts)
                .Select(row => $"{row.Stat} should {(row.Counts ? "count" : "not count")}")
                .ToList();

            // Assert
            Assert.That(string.Join(", ", disagreements), Is.Empty,
                "a spelling the filter drops keeps the set under the gate that would have reported it");
        }

        // One line, one megabyte, gate of zero: the row is reported if and only if it was counted.
        private static bool Counted(string filter, string stat)
        {
            var start = new ProcessStartInfo("python3")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(filter);
            start.ArgumentList.Add("--limit");
            start.ArgumentList.Add("0");

            try
            {
                using var process = Process.Start(start);
                if (process == null)
                {
                    return false;
                }

                process.StandardInput.Write($"{stat} 1024 /bin/probe\n");
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit(15000);
                return output.Contains("cannot be reaped", StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
