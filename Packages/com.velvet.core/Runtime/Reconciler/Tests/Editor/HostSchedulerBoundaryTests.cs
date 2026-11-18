using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when the render core schedules on the panel itself rather than through <see cref="IHostScheduler"/>: a
    /// call, in <see cref="FiberBatchScheduler"/>, <see cref="FiberWorkLoop"/>, <see cref="FiberEffects"/>,
    /// <see cref="FiberRenderer"/> or <see cref="FiberCommitWork"/> or in a type the compiler generated inside one of
    /// them, that reads <c>VisualElement.schedule</c> or <c>VisualElement.panel</c>, or a mention of
    /// <c>IPanel</c> or <c>PanelSchedulerCallback</c>. Read from the compiled assembly, so a call is found by what it
    /// calls and not by its spelling.
    /// </summary>
    [TestFixture]
    internal sealed class HostSchedulerBoundaryTests
    {
        private static readonly string[] CoreTypes =
        {
            typeof(FiberBatchScheduler).FullName!,
            typeof(FiberWorkLoop).FullName!,
            typeof(FiberEffects).FullName!,
            typeof(FiberRenderer).FullName!,
            typeof(FiberCommitWork).FullName!,
        };

        private static readonly HashSet<string> PanelTypes = new()
        {
            "UnityEngine.UIElements.IPanel",
            typeof(PanelSchedulerCallback).FullName!,
        };

        private static string? CoreTypeOf(TypeDefinition type)
        {
            for (var t = type; t != null; t = t.DeclaringType)
            {
                if (CoreTypes.Contains(t.FullName)) return t.FullName;
            }
            return null;
        }

        private static bool ReachesThePanel(object? operand)
        {
            switch (operand)
            {
                case MethodReference method:
                    return PanelTypes.Contains(method.DeclaringType.FullName)
                        || (method.DeclaringType.FullName == "UnityEngine.UIElements.VisualElement"
                            && (method.Name == "get_schedule" || method.Name == "get_panel"));
                case FieldReference field:
                    return PanelTypes.Contains(field.DeclaringType.FullName) || PanelTypes.Contains(field.FieldType.FullName);
                case TypeReference type:
                    return PanelTypes.Contains(type.FullName);
                default:
                    return false;
            }
        }

        [Test]
        public void Given_TheCoreTypesThatScheduleAPass_When_TheirCallsAreRead_Then_NoneReachesThePanelItself()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            var types = runtime.GetTypes().Where(type => CoreTypeOf(type) != null).ToList();

            // Act
            var missing = CoreTypes.Where(core => types.All(type => CoreTypeOf(type) != core))
                .Select(core => "missing: " + core);
            var offenders = types
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions
                    .Where(instruction => ReachesThePanel(instruction.Operand))
                    .Select(instruction => $"{method.DeclaringType.FullName}.{method.Name} reaches {instruction.Operand}")
                    .Concat(method.Body.Variables
                        .Where(variable => PanelTypes.Contains(variable.VariableType.FullName))
                        .Select(variable => $"{method.DeclaringType.FullName}.{method.Name} holds {variable.VariableType.Name}")))
                .Distinct()
                .OrderBy(line => line);

            // Assert — a core type that moved or was renamed would scan as clean, so its absence is reported in the
            // same text.
            Assert.That(string.Join("\n", missing.Concat(offenders)), Is.Empty,
                "schedule through IHostScheduler");
        }
    }
}
