using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the invariant every state transition of <see cref="MutationResult{TVariables, TData}"/> shares:
    /// <c>Data</c> is what one call produced and <c>Error</c> is how one call failed, so neither stands under a
    /// <see cref="MutationStatus"/> that disowns it. <see cref="UseMutationHookTests"/> reads that off the paths
    /// a mutation takes today; this reads it off the transitions themselves, so a path added later answers for
    /// it without being enumerated anywhere.
    /// </summary>
    [TestFixture]
    internal sealed class MutationStateTransitionTests
    {
        private const string Committed = "committed";
        private static readonly Exception Stale = new InvalidOperationException("stale");

        [Test]
        public void Given_AHandleShowingBothOutcomes_When_EveryDeclaredTransitionRuns_Then_NeitherStandsUnderTheOtherStatus()
        {
            // Arrange — the setters are what make the sweep the whole question rather than part of it: with
            // no writer outside the type, every path that moves the observed four is one of these methods.
            var type = typeof(MutationResult<string, string>);
            var writableFromOutside = string.Join(", ", new[] { "Status", "Data", "Error", "Variables" }
                .Where(name => type.GetProperty(name)!.SetMethod is not { IsPrivate: true }));

            // Act — the arranged handle carries a result and a failure at once, so a transition that writes
            // only the field it is named for leaves the other one behind for the sweep to find.
            var findings = new List<string>();
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
                if (handle.Data is not null && handle.Status != MutationStatus.Success)
                {
                    findings.Add($"{transition.Name} left Data standing under {handle.Status}");
                }
                if (handle.Error is not null && handle.Status != MutationStatus.Error)
                {
                    findings.Add($"{transition.Name} left Error standing under {handle.Status}");
                }
            }

            // A sweep over transitions that all leave the handle alone reports the same empty finding as one
            // over transitions that all keep the invariant.
            if (moved == 0) findings.Add("no declared transition moved the arranged state");

            // Assert
            Assert.That((writableFromOutside, string.Join("; ", findings)), Is.EqualTo(("", "")),
                "Nothing outside the handle writes the observed state, and every transition of it leaves a whole outcome behind");
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
