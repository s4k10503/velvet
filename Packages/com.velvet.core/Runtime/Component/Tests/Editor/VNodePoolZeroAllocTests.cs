using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Pins what three <c>V.*</c> factory calls allocate: the node and each part it carries, built for that node,
    /// so no part of a tree that was disposed is handed to them.
    /// <list type="bullet">
    /// <item><c>V.Label(text:)</c> allocates the node and a props bag.</item>
    /// <item><c>V.Button(onClick:)</c> allocates the node, the click binding and the one-slot array holding it.</item>
    /// <item><c>V.List</c> over nodes it has keyed before allocates the child array and nothing else.</item>
    /// </list>
    /// <para>
    /// Each case but the canary first mounts and disposes a tree holding eight nodes of the kind it measures, so a pool of parts,
    /// were one put back, would have that tree's parts to hand the measured call. <see cref="GCAllocationProbe"/>
    /// counts the blocks one call charges; the canary is what shows that probe counting at all.
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class VNodePoolZeroAllocTests
    {
        private const int HeldNodes = 8;
        private const string LabelText = "held";

        private static readonly Action OnClick = () => { };
        private static readonly int[] Items = { 0, 1, 2, 3 };
        private static readonly string[] Keys = { "a", "b", "c", "d" };
        private static readonly VNode[] Rows = { V.Div(), V.Div(), V.Div(), V.Div() };

        // Static, so the measured delegates capture nothing and allocate no closure of their own.
        private static readonly Action BuildLabel = () => GC.KeepAlive(V.Label(text: LabelText));

        private static readonly Action BuildButton = () => GC.KeepAlive(V.Button(onClick: OnClick));

        private static readonly Action BuildList = () => GC.KeepAlive(V.List(Items, item => Keys[item], item => Rows[item]));

        private static void MountAndDispose(Func<VNode> build)
        {
            var children = new VNode?[HeldNodes];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = build();
            }
            V.Mount(new VisualElement(), V.Div(children: children)).Dispose();
        }

        // A probe stuck at zero satisfies every guard below without measuring anything, and nothing else
        // in the repository would notice. This is the only case here that asserts a NON-zero count, and it
        // is what makes the three that follow mean something.
        // GREEN_ON_BASE(refactor): the probe already counts a 16-byte array allocation on the base, which is all this case asserts.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        // The cases below run the measured DELEGATE once before measuring it: the first execution of a code
        // path can charge one-time runtime work (JIT, lazy statics) to the measuring scope, which
        // surfaced as a rare order-sensitive false red on this guard.

        [Test]
        public void Given_ADisposedMountThatHeldLabelsWithText_When_AFactoryBuildsAnother_Then_ItAllocatesTheNodeAndAPropsBag()
        {
            // Arrange
            MountAndDispose(() => V.Label(text: LabelText));
            BuildLabel();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(BuildLabel);

            // Assert
            Assert.That(blocks, Is.EqualTo(2));
        }

        [Test]
        public void Given_ADisposedMountThatHeldClickHandlers_When_AFactoryBuildsAnotherButton_Then_ItAllocatesTheNodeTheBindingAndItsArray()
        {
            // Arrange
            MountAndDispose(() => V.Button(onClick: OnClick));
            BuildButton();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(BuildButton);

            // Assert
            Assert.That(blocks, Is.EqualTo(3));
        }

        [Test]
        public void Given_ADisposedMountThatHeldListsOfTheSameLength_When_VListMapsNodesItKeyedBefore_Then_ItAllocatesOnlyTheChildArray()
        {
            // Arrange
            MountAndDispose(() => V.Div(children: V.List(Items, item => Keys[item], _ => V.Div())));
            BuildList();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(BuildList);

            // Assert
            Assert.That(blocks, Is.EqualTo(1));
        }
    }
}
