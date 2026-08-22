using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a relational (<c>group-*</c> / <c>peer-*</c>) binding keeps one payload slot per
    /// <c>StyleVariantClass.RelationalState</c>, filled by the reconciler's own token grouping. Two sites used
    /// to decide that set where the compiler could not check it — the binding enumerated the states by hand
    /// when deciding whether to hook its source, and the grouping allocated its buckets at a literal five — so
    /// a state added to the enum would land nowhere in the first and overflow the second. An exhaustive switch
    /// cannot size an array; deriving the length from the enum can, and this fails when the binding stops
    /// holding one slot per state or the grouping stops filling them all.
    /// <para>
    /// The class list carries a distinct payload per state so a slot filled from the wrong bucket is visible,
    /// and the peer relation is used because <c>checked</c> has no group spelling.
    /// </para>
    /// GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalStateSlotTests
    {
        private const string ClassName =
            "peer-hover:h-on peer-focus:f-on peer-focus-within:w-on peer-active:a-on peer-checked:c-on";

        // The binding is a private nested type of the manipulator, and its payload store is a private field:
        // production types carry no test-only members, so both are reached by reflection. A missing member
        // yields an empty result rather than an exception, so the failure reads as a mismatch against the
        // expected slots.
        private static string[] PayloadsPerState(StyleRelationalVariantManipulator manipulator)
        {
            var bindings = (System.Collections.IList)typeof(StyleRelationalVariantManipulator)
                .GetField("_bindings", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(manipulator)!;
            var binding = bindings[0]!;
            var slots = binding.GetType()
                .GetField("_payloads", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(binding) as string[][];
            if (slots == null)
            {
                return Array.Empty<string>();
            }
            var states = Enum.GetValues(typeof(StyleVariantClass.RelationalState));
            var perState = new string[states.Length];
            for (var i = 0; i < states.Length; i++)
            {
                perState[i] = i < slots.Length ? string.Join("+", slots[i]) : "<no slot>";
            }
            return perState;
        }

        [Test]
        public void Given_OneRelationalTokenPerState_When_TheBindingIsBuilt_Then_EveryStateHasItsOwnPayloadSlot()
        {
            // Arrange — a peer consumer declaring exactly one payload for each relational state, mounted so
            // the reconciler groups the tokens and configures the manipulator.
            var root = new VisualElement();
            using var mounted = V.Mount(root,
                V.Div("flex", V.Div("peer"), V.Label(name: "consumer", className: ClassName)));
            var consumer = root.Q<Label>("consumer");
            var manipulator = mounted.Root.Reconciler.Context.RelationalVariantManipulators[consumer];

            // Act — read what the binding stored, slot by slot, over the whole state enum.
            var perState = PayloadsPerState(manipulator);

            // Assert — one slot per state, each holding the payload declared for it.
            Assert.That(perState, Is.EqualTo(new[] { "h-on", "f-on", "w-on", "a-on", "c-on" }));
        }
    }
}
