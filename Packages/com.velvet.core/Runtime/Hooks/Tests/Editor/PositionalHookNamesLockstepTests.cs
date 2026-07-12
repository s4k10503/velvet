using System.Collections.Generic;
using Mono.Cecil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="PositionalHookNames.All"/>, the canonical set of hook names that allocate a positional
    /// slot.
    /// <list type="bullet">
    /// <item>The set is exactly the expected canonical names — no more, no fewer. The ILPP weaver reads this
    /// list directly to decide which calls must stay in the hook section, so a positional-slot hook missing
    /// from it is silently dropped from that set.</item>
    /// <item>The set contains no duplicate names.</item>
    /// <item>Structurally: every public <c>Use*</c> hook whose implementation allocates a positional slot
    /// (touches a <c>HookIndexTable</c> cursor or advances the async resource slot cursor) is present in the
    /// list, so a newly added positional hook that is forgotten here fails this fixture instead of being
    /// silently invisible to the weaver.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class PositionalHookNamesLockstepTests
    {
        private static readonly string[] Expected =
        {
            "UseEffect",
            "UseLayoutEffect",
            "UseInsertionEffect",
            "UseCallback",
            "UseMemo",
            "UseBlocker",
            "UseState",
            "UseReducer",
            "UseOptimistic",
            "UseStore",
            "UseContext",
            "UseRef",
            "UseMutableRef",
            "UseImperativeHandle",
            "UseTransition",
            "UseId",
            "UseDeferredValue",
            "UseMutation",
            "UseService",
            "UseFallback",
            "Use",
            "UseFrame",
        };

        [Test]
        public void Given_CanonicalSet_When_Inspected_Then_MatchesTheExpectedNames()
        {
            // Act + Assert
            Assert.That(PositionalHookNames.All, Is.EquivalentTo(Expected),
                "PositionalHookNames.All drifted from the canonical set. If the change is intentional, update the" +
                " Expected list here; the ILPP weaver reads PositionalHookNames.All directly.");
        }

        [Test]
        public void Given_CanonicalSet_When_Inspected_Then_HasNoDuplicates()
        {
            // Act + Assert
            Assert.That(PositionalHookNames.All, Is.Unique);
        }

        [Test]
        public void Given_HooksAssembly_When_PositionalSlotConsumersAreEnumerated_Then_EveryOneIsInTheCanonicalList()
        {
            // Arrange
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(Hooks).Assembly.Location);
            var hooksType = assembly.MainModule.GetType(typeof(Hooks).FullName);
            Assume.That(hooksType, Is.Not.Null, "Precondition: Velvet.Hooks is present in the Velvet assembly");

            // Act
            var slotConsumers = EnumeratePositionalSlotConsumers(hooksType);

            // Assert
            Assert.That(slotConsumers, Is.SubsetOf(PositionalHookNames.All),
                "Every public hook that allocates a positional slot (a HookIndexTable cursor or an async" +
                " resource slot) must be listed in PositionalHookNames.All, or the ILPP weaver silently" +
                " treats its calls as non-hook plumbing and may skip or mis-anchor them.");
        }

        // Enumerates the public Use* hooks whose implementation allocates a positional slot: the method's
        // body — or the body of a non-hook Hooks helper it calls, transitively — touches a HookIndexTable
        // cursor field or advances the fiber's async resource slot cursor. Descent deliberately stops at
        // other public Use* hooks: those allocate their own slot and are enumerated independently, while a
        // hook that merely composes them (e.g. UseNavigation over UseState) is tracked transitively by the
        // weaver and does not need a list entry of its own.
        private static IReadOnlyCollection<string> EnumeratePositionalSlotConsumers(TypeDefinition hooksType)
        {
            var consumers = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var method in hooksType.Methods)
            {
                if (!IsPublicHookMethod(method)) continue;
                if (AllocatesPositionalSlot(method, hooksType, new HashSet<string>()))
                {
                    consumers.Add(method.Name);
                }
            }
            return consumers;
        }

        private static bool IsPublicHookMethod(MethodDefinition method)
            => method.IsPublic
                && method.IsStatic
                && method.Name.StartsWith("Use", System.StringComparison.Ordinal);

        private static bool AllocatesPositionalSlot(
            MethodDefinition method, TypeDefinition hooksType, HashSet<string> visited)
        {
            if (!method.HasBody || !visited.Add(method.FullName)) return false;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is FieldReference field
                    && field.DeclaringType.FullName == "Velvet.HookIndexTable")
                {
                    return true;
                }
                if (instruction.Operand is not MethodReference callee) continue;
                if (callee.Name == "NextAsyncSlotIndex")
                {
                    return true;
                }
                // Only same-type helpers are followed, so resolution never leaves the already-loaded
                // module (resolving foreign references would require an assembly resolver).
                if (callee.DeclaringType.FullName != hooksType.FullName) continue;
                var calleeDefinition = callee.Resolve();
                if (calleeDefinition == null) continue;
                if (IsPublicHookMethod(calleeDefinition)) continue;
                if (AllocatesPositionalSlot(calleeDefinition, hooksType, visited))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
