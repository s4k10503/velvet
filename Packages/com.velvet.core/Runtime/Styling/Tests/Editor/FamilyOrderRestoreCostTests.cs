using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what restoring an element's live class order costs where its families are already in the order
    /// Tailwind emits them, which the reconciler asks whenever a class list changes: each token's family is read once,
    /// and the restore builds no list or sort to find that out.
    /// </summary>
    /// <remarks>
    /// <see cref="FamilyOrderTests"/> and <see cref="LiveClassListOrderTests"/> hold what the restore puts where.
    /// </remarks>
    [TestFixture]
    internal sealed class FamilyOrderRestoreCostTests
    {
        private static VisualElement Carrying(string[] classes)
        {
            var element = new VisualElement();
            foreach (var cls in classes)
            {
                element.AddToClassList(cls);
            }
            return element;
        }

        // The reads of a token's family during run, counted at the first family's test: SlotOf asks it of every token
        // with a core and no variant, before any other family's.
        private static int FamilyReadsDuring(Action run)
        {
            var families = (ValueTuple<Func<string, bool>, Func<string, int>>[])typeof(StyleFamilyOrder)
                .GetField("s_families", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null);
            var first = families[0];
            var reads = 0;
            families[0] = (core =>
            {
                reads++;
                return first.Item1(core);
            }, first.Item2);
            try
            {
                run();
            }
            finally
            {
                families[0] = first;
            }
            return reads;
        }

        [Test]
        public void Given_AClassListWhoseFamilyTokensAreInOrder_When_ItsLiveOrderIsRestored_Then_EachTokensFamilyIsReadOnce()
        {
            // Arrange — flex belongs to no family; z-10 sorts ahead of z-20, so neither needs moving
            var tokens = new[] { "flex", "z-10", "z-20" };
            var element = Carrying(tokens);

            // Act
            var reads = FamilyReadsDuring(() => StyleFamilyOrder.RestoreLiveOrder(element, tokens));

            // Assert
            Assert.That(reads, Is.EqualTo(tokens.Length));
        }

        [Test]
        public void Given_AClassListWhoseOneFamilyTokenIsLive_When_ItsLiveOrderIsRestoredAgain_Then_NothingIsAllocated()
        {
            // Arrange — uppercase is the first family's by its spelling alone, so reading its family parses nothing
            var tokens = new[] { "uppercase" };
            var element = Carrying(tokens);
            Action restore = () => StyleFamilyOrder.RestoreLiveOrder(element, tokens);
            Action canary = () => GC.KeepAlive(new object());
            restore();
            canary();

            // Act
            var canaryBlocks = GCAllocationProbe.MedianBlocksDuring(canary);
            var blocks = GCAllocationProbe.MedianBlocksDuring(restore);

            // Assert — a probe that counts nothing for the canary cannot vouch for a zero
            Assert.That(canaryBlocks > 0 ? blocks : -1, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base reads a class list that holds no family token without allocating.
        // What this pins is that the families of 256 tokens, as many as the restore keeps on the stack, are read there;
        // a bound that sends a list of that length to the heap, `<` for `<=` at MaxSlotsOnStack, is what reddens it.
        [Test]
        public void Given_256VariantClasses_When_TheirLiveOrderIsRestored_Then_NothingIsAllocated()
        {
            // Arrange — a token behind a variant belongs to no family, so nothing past reading the families runs
            const int length = 256;
            var tokens = new string[length];
            for (var i = 0; i < length; i++)
            {
                tokens[i] = "hover:w-" + i;
            }
            var element = new VisualElement();
            Action restore = () => StyleFamilyOrder.RestoreLiveOrder(element, tokens);
            Action canary = () => GC.KeepAlive(new object());
            restore();
            canary();

            // Act
            var canaryBlocks = GCAllocationProbe.MedianBlocksDuring(canary);
            var blocks = GCAllocationProbe.MedianBlocksDuring(restore);

            // Assert — a probe that counts nothing for the canary cannot vouch for a zero
            Assert.That(canaryBlocks > 0 ? blocks : -1, Is.EqualTo(0));
        }
    }
}
