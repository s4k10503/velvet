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

            // Act — the arranged handle carries a result and a failure at once, so a transition that writes
            // only the field it is named for leaves the other one behind for the sweep to find. A handle as
            // constructed is asked first and unarranged: what a seed writes there is no transition's doing,
            // and arranging over it is what would hide it.
            var findings = new List<string>();
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

            var moved = 0;
            foreach (var transition in type
                         .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .Where(method => !method.IsSpecialName))
            {
                var arguments = transition.GetParameters().Select(parameter => Argument(parameter.ParameterType)).ToArray();
                if (arguments.Any(argument => argument is null))
                {
                    findings.Add($"{transition.Name} takes an argument this sweep cannot supply");
                    continue;
                }

                var handle = ShowingBothOutcomes();
                var arranged = Snapshot(handle);
                transition.Invoke(handle, arguments);
                // One that wrote nothing answers for nothing, and a handle nothing wrote is still the
                // incoherent one arranged above.
                if (Snapshot(handle) == arranged) continue;

                moved++;
                Record(handle, transition.Name, findings);
            }

            // A sweep over transitions that all leave the handle alone reports the same empty finding as one
            // over transitions that all keep the invariant.
            if (moved == 0) findings.Add("no declared transition moved the arranged state");

            // Assert
            Assert.That((writableFromOutside, string.Join("; ", findings)), Is.EqualTo(("", "")),
                "Nothing outside the handle writes the observed state, and every writer of it leaves a whole outcome behind");
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

        private static MutationResult<string, string> ShowingBothOutcomes()
        {
            var handle = new MutationResult<string, string>();
            var type = typeof(MutationResult<string, string>);
            type.GetProperty(nameof(MutationResult<string, string>.Data))!.SetValue(handle, Committed);
            type.GetProperty(nameof(MutationResult<string, string>.Error))!.SetValue(handle, Stale);
            type.GetProperty(nameof(MutationResult<string, string>.Variables))!.SetValue(handle, "arranged");
            type.GetProperty(nameof(MutationResult<string, string>.Status))!.SetValue(handle, MutationStatus.Success);
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
