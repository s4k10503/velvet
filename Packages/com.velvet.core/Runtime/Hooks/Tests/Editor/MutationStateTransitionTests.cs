using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the invariant every write to a <see cref="MutationResult{TVariables, TData}"/> shares: <c>Data</c>
    /// is what one call produced and <c>Error</c> is how one call failed, so neither stands under a
    /// <see cref="MutationStatus"/> that disowns it. <see cref="UseMutationHookTests"/> reads that off the paths
    /// a mutation takes today; this reads it off the writers — each constructor and each declared method — so
    /// one added later answers for it without being enumerated anywhere.
    /// </summary>
    [TestFixture]
    internal sealed class MutationStateTransitionTests
    {
        private const string Committed = "committed";
        private static readonly Exception Stale = new InvalidOperationException("stale");

        [Test]
        public void Given_AHandleShowingBothOutcomes_When_EveryDeclaredWriterRuns_Then_NeitherStandsUnderTheOtherStatus()
        {
            // Arrange — the setters are what make the sweep the whole question rather than part of it: with
            // no writer outside the type, what moves the observed four is declared on it. A property with
            // no setter at all is the stricter case, not a laxer one, so it is not counted here.
            var type = typeof(MutationResult<string, string>);
            var writableFromOutside = string.Join(", ", new[] { "Status", "Data", "Error", "Variables" }
                .Where(name => type.GetProperty(name)!.SetMethod is { IsPrivate: false }));

            // Act
            var findings = new List<string>();
            SweepConstructors(type, findings);
            // Two arrangements, differing in the status they hold. A writer that only sets Status moves
            // nothing under the arrangement already holding that status, and a writer that moves nothing is
            // what the sweep has to pass over; under the other arrangement the same writer moves.
            var moved = SweepWriters(type, MutationStatus.Success, findings)
                        + SweepWriters(type, MutationStatus.Error, findings);
            // A sweep over writers that all leave the handle alone reports the same empty finding as one
            // over writers that all keep the invariant.
            if (moved == 0) findings.Add("no declared writer moved the arranged state");

            // Assert
            Assert.That((writableFromOutside, string.Join("; ", findings)), Is.EqualTo(("", "")),
                "Nothing outside the handle writes the observed state, and every writer of it leaves a whole outcome behind");
        }

        /// <summary>
        /// A handle as constructed, unarranged: what a seed writes there is no transition's doing, and
        /// arranging over it is what would hide it.
        /// </summary>
        private static void SweepConstructors(Type type, List<string> findings)
        {
            foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var seeds = constructor.GetParameters().Select(parameter => Argument(parameter.ParameterType)).ToArray();
                if (seeds.Any(seed => seed is null))
                {
                    findings.Add("a constructor takes an argument this sweep cannot supply");
                    continue;
                }

                Record((MutationResult<string, string>)constructor.Invoke(seeds), "a constructed handle", findings);
            }
        }

        /// <summary>
        /// Every declared method against a handle carrying a result and a failure at once, so a writer that
        /// writes only the field it is named for leaves the other one behind to be found. Returns how many
        /// of them wrote anything.
        /// </summary>
        private static int SweepWriters(Type type, MutationStatus arrangedStatus, List<string> findings)
        {
            var moved = 0;
            foreach (var writer in type
                         .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .Where(method => !method.IsSpecialName))
            {
                var arguments = writer.GetParameters().Select(parameter => Argument(parameter.ParameterType)).ToArray();
                if (arguments.Any(argument => argument is null))
                {
                    findings.Add($"{writer.Name} takes an argument this sweep cannot supply");
                    continue;
                }

                var handle = ShowingBothOutcomes(arrangedStatus);
                var arranged = Snapshot(handle);
                var produced = writer.Invoke(handle, arguments);
                // A handle handed back rather than written in place — a static factory's shape — answers for
                // the invariant whatever became of the arranged one.
                if (produced is MutationResult<string, string> other && !ReferenceEquals(other, handle))
                {
                    moved++;
                    Record(other, $"the handle {writer.Name} returns", findings);
                }

                // One that wrote nothing answers for nothing, and a handle nothing wrote is still the
                // incoherent one arranged above.
                if (Snapshot(handle) == arranged) continue;

                moved++;
                Record(handle, writer.Name, findings);
            }

            return moved;
        }

        private static void Record(MutationResult<string, string> handle, string what, List<string> findings)
        {
            if (handle.Data is not null && handle.Status != MutationStatus.Success)
            {
                findings.Add($"{what} leaves Data standing under {handle.Status}");
            }
            if (handle.Error is not null && handle.Status != MutationStatus.Error)
            {
                findings.Add($"{what} leaves Error standing under {handle.Status}");
            }
        }

        private static MutationResult<string, string> ShowingBothOutcomes(MutationStatus status)
        {
            var handle = new MutationResult<string, string>();
            var type = typeof(MutationResult<string, string>);
            type.GetProperty(nameof(MutationResult<string, string>.Data))!.SetValue(handle, Committed);
            type.GetProperty(nameof(MutationResult<string, string>.Error))!.SetValue(handle, Stale);
            type.GetProperty(nameof(MutationResult<string, string>.Variables))!.SetValue(handle, "arranged");
            type.GetProperty(nameof(MutationResult<string, string>.Status))!.SetValue(handle, status);
            return handle;
        }

        private static string Snapshot(MutationResult<string, string> handle) =>
            $"{handle.Status}|{handle.Data}|{handle.Error?.Message}|{handle.Variables}";

        private static object? Argument(Type parameterType) =>
            parameterType == typeof(string) ? "later"
            : parameterType == typeof(Exception) ? new InvalidOperationException("swept")
            : null;
    }
}
