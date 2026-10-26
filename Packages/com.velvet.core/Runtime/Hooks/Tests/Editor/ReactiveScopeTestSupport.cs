using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.Tests
{
    /// <summary>
    /// Reads back what the weaver did to a component method of a test fixture: the report it left on the method and
    /// the IL it wrote, from the assembly on disk the fixture was compiled into.
    /// </summary>
    internal static class ReactiveScopeTestSupport
    {
        internal static MemoizationReportAttribute ReportOf(Type fixture, string methodName)
        {
            var method = fixture.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return method!.GetCustomAttribute<MemoizationReportAttribute>()!;
        }

        internal static AssemblyDefinition ReadWoven(Type fixture)
            => AssemblyDefinition.ReadAssembly(fixture.Assembly.Location);

        internal static MethodDefinition MethodOf(AssemblyDefinition assembly, Type fixture, string methodName)
            => assembly.MainModule.GetType(fixture.FullName.Replace('+', '/')).Methods
                .Single(each => each.Name == methodName);

        internal static bool IsHookCall(Instruction instruction)
            => instruction.Operand is MethodReference callee
                && instruction.OpCode.Code is Code.Call or Code.Callvirt
                && callee.DeclaringType.FullName == typeof(Hooks).FullName
                && callee.Name.StartsWith("Use", StringComparison.Ordinal);

        internal static bool IsGateCall(Instruction instruction)
            => instruction.Operand is MethodReference callee
                && instruction.OpCode.Code is Code.Call or Code.Callvirt
                && callee.DeclaringType.FullName == typeof(Hooks).FullName
                && (callee.Name == nameof(Hooks.TryGetMemoizedVNode) || callee.Name == nameof(Hooks.TryGetScope));

        /// <summary>The instruction list index of the first gate call, which is the gate on the body where there is one.</summary>
        internal static int FirstGateIndex(MethodDefinition method)
            => method.Body.Instructions.ToList().FindIndex(IsGateCall);

        /// <summary>
        /// The number of entries in each key array the weaver built, in the order they sit in the method: the length of
        /// every array of objects that is created and then filled or handed on empty.
        /// </summary>
        internal static List<int> KeySizes(MethodDefinition method)
        {
            var instructions = method.Body.Instructions.ToList();
            var sizes = new List<int>();
            for (var i = 1; i + 1 < instructions.Count; i++)
            {
                if (instructions[i].OpCode.Code != Code.Newarr
                    || ((TypeReference)instructions[i].Operand).FullName != typeof(object).FullName)
                {
                    continue;
                }
                var next = instructions[i + 1].OpCode.Code;
                if (next != Code.Dup && next != Code.Stloc && next != Code.Stloc_S) continue;
                sizes.Add(IntegerOf(instructions[i - 1]));
            }
            return sizes;
        }

        private static int IntegerOf(Instruction instruction)
            => instruction.OpCode.Code switch
            {
                Code.Ldc_I4_0 => 0,
                Code.Ldc_I4_1 => 1,
                Code.Ldc_I4_2 => 2,
                Code.Ldc_I4_3 => 3,
                Code.Ldc_I4_4 => 4,
                Code.Ldc_I4_5 => 5,
                Code.Ldc_I4_6 => 6,
                Code.Ldc_I4_7 => 7,
                Code.Ldc_I4_8 => 8,
                Code.Ldc_I4_S => (sbyte)instruction.Operand,
                _ => (int)instruction.Operand,
            };

        /// <summary>
        /// Whether some instruction inside a protected region branches out of it by anything but <c>leave</c>, which
        /// is the only way out of a try, catch or finally block that is valid.
        /// </summary>
        internal static bool BranchesOutOfAProtectedRegion(MethodDefinition method)
        {
            var instructions = method.Body.Instructions.ToList();
            int IndexOf(Instruction? instruction) => instruction == null ? instructions.Count : instructions.IndexOf(instruction);
            var regions = method.Body.ExceptionHandlers.SelectMany(handler => new[]
            {
                (From: IndexOf(handler.TryStart), To: IndexOf(handler.TryEnd)),
                (From: handler.FilterStart == null ? 0 : IndexOf(handler.FilterStart), To: handler.FilterStart == null ? 0 : IndexOf(handler.HandlerStart)),
                (From: IndexOf(handler.HandlerStart), To: IndexOf(handler.HandlerEnd)),
            }).ToList();
            foreach (var (from, to) in regions)
            {
                for (var i = from; i < to; i++)
                {
                    if (instructions[i].OpCode.Name.StartsWith("leave", StringComparison.Ordinal)) continue;
                    var targets = instructions[i].Operand switch
                    {
                        Instruction one => new[] { one },
                        Instruction[] many => many,
                        _ => Array.Empty<Instruction>(),
                    };
                    if (targets.Any(target => IndexOf(target) < from || IndexOf(target) >= to)) return true;
                }
            }
            return false;
        }

        internal static IEnumerable<int> HookCallIndices(MethodDefinition method)
        {
            var instructions = method.Body.Instructions.ToList();
            for (var i = 0; i < instructions.Count; i++)
            {
                if (IsHookCall(instructions[i])) yield return i;
            }
        }
    }
}
