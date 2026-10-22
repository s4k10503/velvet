using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Reading the translate and scale an element's rules declare, which a layoutId projection can do on every pass.
    /// GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class StyleCascadeTransformReadTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Test]
        public void Given_AnElementTranslatedAndScaledByItsRules_When_TheyAreReadAgain_Then_NothingIsAllocated()
        {
            // Arrange — the bundled sheet translating the leaf 20px up and scaling it by one and a half; one read to warm
            // the readers, and a canary allocating a known array.
            var leaf = MountAndResolve("anim-slide-up-exit-to scale-150");
            Action read = () =>
            {
                StyleCascade.Translate(leaf);
                StyleCascade.Scale(leaf);
            };
            read();
            Action canary = () => GC.KeepAlive(new byte[16]);
            canary();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(read);
            var counted = GCAllocationProbe.MedianBlocksDuring(canary) > 0;

            // Assert — none, where the canary's are counted and both readers give what the rules declare.
            var readsRules = StyleCascade.Translate(leaf)?.y.value == -20f && StyleCascade.Scale(leaf)?.value.x == 1.5f;
            Assert.That(counted && readsRules ? blocks : -1, Is.Zero);
        }
    }
}
