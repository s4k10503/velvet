using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that composing an element's class source while a play withholds one of its tokens builds nothing
    /// once the cached composition matches, as composing one with no token withheld does.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class VariantClassCompositionAllocationTests
    {
        // A direct call, so the probe counts the composer alone.
        private static readonly Func<VisualElement, string[], VariantGateState, string[]?, string[]> Compose =
            (Func<VisualElement, string[], VariantGateState, string[]?, string[]>)Delegate.CreateDelegate(
                typeof(Func<VisualElement, string[], VariantGateState, string[]?, string[]>),
                typeof(FiberNodePatcher).GetMethod("ComposeVariantClasses",
                    BindingFlags.NonPublic | BindingFlags.Static)!);

        [Test]
        public void Given_APlayWithholdingAToken_When_TheSameClassesComposeAgain_Then_NothingIsBuilt()
        {
            // Arrange — a fresh array of the same classes, as a render hands the patcher.
            var element = new VisualElement();
            var state = new VariantGateState();
            state.Withhold("shadow-lg", on: true);
            var reuse = Compose(element, new[] { "w-4", "shadow-lg" }, state, null);
            var classNames = new[] { "w-4", "shadow-lg" };
            void Once() => Compose(element, classNames, state, reuse);
            for (var i = 0; i < 64; i++)
            {
                Once();
            }
            var canary = GCAllocationProbe.MedianBlocksDuring(() => GC.KeepAlive(new string[1]));

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(Once);

            // Assert — the canary rides along, because a zero says nothing if the probe measured nothing at all.
            Assert.That((canary > 0, blocks), Is.EqualTo((true, 0)));
        }
    }
}
