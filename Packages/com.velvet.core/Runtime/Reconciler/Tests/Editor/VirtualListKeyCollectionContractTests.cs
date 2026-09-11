// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the two BCL collections a <see cref="V.VirtualList{T}"/> range update tracks item keys in to
    /// what each does with a null key, which is why the controller keeps one out of both.
    /// <list type="bullet">
    /// <item>A <c>Dictionary&lt;string, …&gt;</c> lookup or removal by a null key throws, against an empty
    /// dictionary as much as a populated one.</item>
    /// <item>A <c>HashSet&lt;string&gt;</c> takes a null as a member, so it reports the first null it is
    /// handed as newly added rather than refusing it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListKeyCollectionContractTests
    {
        // GREEN_ON_BASE(characterization): the throw the null-key guard exists for.
        // Nothing of this repository's is on either side, so no change here could redden it; what it
        // exists to catch is the day the framework stops throwing and the guard reads as dead weight.
        [Test]
        public void Given_AnEmptyStringKeyedDictionary_When_LookedUpByNull_Then_ItThrows()
        {
            // Arrange
            var byKey = new Dictionary<string, int>();

            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => byKey.TryGetValue(null, out _));
        }

        // GREEN_ON_BASE(characterization): removing by a null key throws as the lookup does.
        // A range update makes this call with the key it looked a row up with, once the row's slot holds
        // an element.
        [Test]
        public void Given_AnEmptyStringKeyedDictionary_When_RemovedFromByNull_Then_ItThrows()
        {
            // Arrange
            var byKey = new Dictionary<string, int>();

            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => byKey.Remove(null));
        }

        // GREEN_ON_BASE(characterization): the set takes what the dictionary refuses.
        // The asymmetry is why a null key handed to both would surface at the dictionary rather than at
        // the set, which is the throw the controller's guard is placed ahead of.
        [Test]
        public void Given_AnEmptyStringKeyedSet_When_ANullIsAdded_Then_ItIsTakenAsANewMember()
        {
            // Arrange
            var claimed = new HashSet<string>();

            // Act
            var added = claimed.Add(null);

            // Assert
            Assert.That(added, Is.True);
        }
    }
}
