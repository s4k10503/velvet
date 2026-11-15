using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the order <c>FiberCommitWork</c> reads a component's slot limit by: the placed components of
    /// one container sit in their parent's child chain in the order of their recorded starts after the list
    /// changes below (a reversal, an insertion, a removal, a mix), and the limit is the first later start the chain reaches without reading what comes
    /// after it. A component no walk has placed is read from the head of the chain.
    /// </summary>
    [TestFixture]
    internal sealed class InlineSlotChainOrderTests
    {
        private static readonly MethodInfo NextSlotStart = typeof(FiberCommitWork).GetMethod(
            "NextInlineSiblingSlotStart", BindingFlags.NonPublic | BindingFlags.Static)!;

        private static Dictionary<string, ComponentFiber> s_fibers;
        private static string[] s_keys;
        private static StateUpdater<string[]> s_setKeys;

        [SetUp]
        public void SetUp()
        {
            s_fibers = new Dictionary<string, ComponentFiber>();
            s_keys = new[] { "a", "b", "c", "d", "e", "f" };
        }

        [Component(Compiler = false)]
        private static VNode ListRender()
        {
            var (keys, setKeys) = Hooks.UseState(s_keys);
            s_setKeys = setKeys;
            var rows = new VNode[keys.Length];
            for (var i = 0; i < rows.Length; i++) rows[i] = V.Component(RowRender, keys[i], key: keys[i]);
            return V.Div(children: rows);
        }

        [Component(Compiler = false)]
        private static VNode RowRender(string key)
        {
            s_fibers[key] = FiberAmbientStack.Current;
            return V.Label(name: key);
        }

        private static int LimitOf(ComponentFiber fiber) => (int)NextSlotStart.Invoke(null, new object[] { fiber })!;

        private static List<int> PlacedStartsInChainOrder(ComponentFiber member)
        {
            var starts = new List<int>();
            for (var sibling = member.Parent!.Child; sibling != null; sibling = sibling.Sibling)
            {
                if (sibling.MountSlotCount >= 0 && sibling.MountPoint == member.MountPoint)
                {
                    starts.Add(sibling.MountSlotStart);
                }
            }
            return starts;
        }

        // GREEN_ON_BASE(characterization): the base's full walk does not depend on the chain order, so this pins the order the early exit relies on and the base already holds it.
        [TestCase("f e d c b a")]
        [TestCase("a b X c d e f")]
        [TestCase("a c e")]
        [TestCase("e a f b")]
        [TestCase("X a Y f")]
        public void Given_AKeyedListChanged_When_ItCommits_Then_TheChainListsThePlacedStartsInOrder(string next)
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(ListRender, key: "host"));
            mounted.DrainForTest();

            // Act
            s_setKeys.Invoke(next.Split(' '));
            mounted.DrainForTest();

            // Assert
            Assert.That(PlacedStartsInChainOrder(s_fibers["a"]), Is.Ordered);
        }

        // GREEN_ON_BASE(characterization): the base's full walk reaches the next start too, and a limit that returned nothing would satisfy the case below.
        [Test]
        public void Given_AFiberWithAPlacedSiblingBehindIt_When_TheLimitIsRead_Then_ItIsTheNextStart()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(ListRender, key: "host"));
            mounted.DrainForTest();

            // Act
            var limit = LimitOf(s_fibers["c"]);

            // Assert
            Assert.That(limit, Is.EqualTo(s_fibers["d"].MountSlotStart));
        }

        // The tail behind the first later start is set out of order by hand, so a walk that reads past the
        // first later start returns the start between instead.
        [Test]
        public void Given_ASiblingStartingBetweenTheNextStartAndTheFiber_When_TheLimitIsRead_Then_TheChainIsNotReadPastTheNextStart()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(ListRender, key: "host"));
            mounted.DrainForTest();
            s_fibers["c"].MountSlotStart = 2;
            s_fibers["d"].MountSlotStart = 9;
            s_fibers["e"].MountSlotStart = 5;

            // Act
            var limit = LimitOf(s_fibers["c"]);

            // Assert
            Assert.That(limit, Is.EqualTo(9));
        }

        // GREEN_ON_BASE(characterization): the base reads the whole chain, so it reaches the earlier sibling too.
        [Test]
        public void Given_AnUnplacedFiberStartingBeforeAnEarlierSibling_When_TheLimitIsRead_Then_ItIsThatSiblingsStart()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(ListRender, key: "host"));
            mounted.DrainForTest();
            s_fibers["e"].MountSlotCount = -1;
            s_fibers["e"].MountSlotStart = 1;

            // Act
            var limit = LimitOf(s_fibers["e"]);

            // Assert
            Assert.That(limit, Is.EqualTo(s_fibers["c"].MountSlotStart));
        }
    }
}
