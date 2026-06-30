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
    }
}
