using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an update removing many sibling Portals costs the same per Portal from one count of them to the
    /// next, read as counts over that update at three counts: how often the target the Portals share is read, how often
    /// the Portals' rows are looked up, and how often the recorded ranges are. A cost per Portal that grows with the count
    /// shows as a difference between consecutive counts that grows too.
    /// </summary>
    [TestFixture]
    internal sealed class PortalUnmountScanTests
    {
        private const int Step = 12;

        private static int s_initial;
        private static StateUpdater<int> s_setCount;
        private static VisualElement s_target;
        private static int s_rowHashes;

        // Counts the reads the row model makes to find where the target's children are (ChildListWrites.ContentOf).
        private sealed class CountedTarget : VisualElement
        {
            public int Reads;

            public override VisualElement contentContainer
            {
                get
                {
                    Reads++;
                    return this;
                }
            }
        }

        // A Portal's row, counting each lookup of it that hashes it through its own GetHashCode.
        private sealed class CountedRow : VisualElement
        {
            public override int GetHashCode()
            {
                s_rowHashes++;
                return base.GetHashCode();
            }

            public override bool Equals(object obj) => ReferenceEquals(this, obj);
        }

        private sealed class CountedLookups : IEqualityComparer<VisualElement>
        {
            public int Hashes;

            public bool Equals(VisualElement x, VisualElement y) => ReferenceEquals(x, y);

            public int GetHashCode(VisualElement obj)
            {
                Hashes++;
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        [SetUp]
        public void SetUp() => FiberStrictMode.Enabled = false;

        [Component(Compiler = false)]
        private static VNode Child() => V.Label(text: "c");

        [Component(Compiler = false)]
        private static VNode SiblingPortals()
        {
            var (count, setCount) = Hooks.UseState(s_initial);
            s_setCount = setCount;
            var portals = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                portals[i] = V.Portal(s_target, children: new VNode[] { V.Custom<CountedRow>() }, key: "p" + i);
            }
            return V.Div(children: portals);
        }

        // The ref on the element holding the Portals is what keeps a removal's walk going past the Portals it finds
        // (DeletionCommit.TakeDownLeaving), to their rows.
        [Component(Compiler = false)]
        private static VNode SiblingPortalsUnderARef()
        {
            var (count, setCount) = Hooks.UseState(s_initial);
            s_setCount = setCount;
            var portals = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                portals[i] = V.Portal(s_target, children: new VNode[] { V.Custom<CountedRow>() }, key: "p" + i);
            }
            return V.Div(refCallback: _ => () => { }, children: portals);
        }

        // The component beside the Portals is what takes the container's reconcile off the flat diff.
        [Component(Compiler = false)]
        private static VNode SiblingPortalsBesideAComponent()
        {
            var (count, setCount) = Hooks.UseState(s_initial);
            s_setCount = setCount;
            var children = new VNode[count + 1];
            for (var i = 0; i < count; i++)
            {
                children[i] = V.Portal(s_target, children: new VNode[] { V.Label(text: "p") }, key: "p" + i);
            }
            children[count] = V.Component(Child, key: "beside");
            return V.Div(children: children);
        }

        // What the update removing every Portal of host, mounted with count of them, adds to read. arrange is handed the
        // mounted tree before the count is taken.
        private static int CountedWhileUnmounting(
            Func<VNode> host, int count, VisualElement target, Func<int> read, Action<MountedTree> arrange)
        {
            s_initial = count;
            s_target = target;
            using var mounted = V.Mount(new VisualElement(), V.Component(host));
            mounted.DrainForTest();
            arrange(mounted);
            var before = read();
            s_setCount.Invoke(0);
            mounted.DrainForTest();
            return read() - before;
        }

        private static int TargetReadsWhileUnmounting(int count)
        {
            var target = new CountedTarget();
            return CountedWhileUnmounting(SiblingPortals, count, target, () => target.Reads, _ => { });
        }

        private static int RowLookupsWhileUnmounting(int count)
            => CountedWhileUnmounting(SiblingPortals, count, new VisualElement(), () => s_rowHashes, _ => { });

        private static int RowLookupsUnderARefWhileUnmounting(int count)
            => CountedWhileUnmounting(SiblingPortalsUnderARef, count, new VisualElement(), () => s_rowHashes, _ => { });

        // The recorded ranges are read through a table keyed by placeholder, which this swaps, entries and all, for one
        // that counts its lookups; -1 where the table cannot be swapped.
        private static int RangeLookupsWhileUnmounting(int count)
        {
            var lookups = new CountedLookups();
            var swapped = false;
            var counted = CountedWhileUnmounting(SiblingPortalsBesideAComponent, count, new VisualElement(), () => lookups.Hashes, mounted =>
            {
                var store = mounted.Root.Reconciler.Context.PortalState.Stored;
                var field = typeof(PortalRangeStore).GetField("_map", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) return;
                var table = (IDictionary)Activator.CreateInstance(field.FieldType, lookups);
                foreach (DictionaryEntry entry in (IDictionary)field.GetValue(store)) table.Add(entry.Key, entry.Value);
                field.SetValue(store, table);
                swapped = true;
            });
            return swapped ? counted : -1;
        }

        // How much the count grows faster from the second count to the third than from the first to the second: zero
        // for a count that is the same per Portal. A count that did not grow from the first to the second measured
        // nothing per Portal, and reads as a value zero never equals.
        private static int GrowthOfTheGrowth(Func<int, int> measure)
        {
            var first = measure(Step);
            var second = measure(2 * Step);
            var third = measure(3 * Step);
            return second > first ? third - second - (second - first) : int.MinValue;
        }

        [Test]
        public void Given_SiblingPortalsOnOneTarget_When_AnUpdateRemovesThemAll_Then_TheTargetIsReadAsOftenPerPortalAtThreeCounts()
        {
            // Arrange
            Func<int, int> reads = TargetReadsWhileUnmounting;

            // Act
            var growth = GrowthOfTheGrowth(reads);

            // Assert
            Assert.That(growth, Is.EqualTo(0));
        }

        [Test]
        public void Given_SiblingPortalsOnOneTarget_When_AnUpdateRemovesThemAll_Then_TheirRowsAreLookedUpAsOftenPerPortalAtThreeCounts()
        {
            // Arrange
            Func<int, int> lookups = RowLookupsWhileUnmounting;

            // Act
            var growth = GrowthOfTheGrowth(lookups);

            // Assert
            Assert.That(growth, Is.EqualTo(0));
        }

        [Test]
        public void Given_SiblingPortalsUnderAnElementWithARef_When_AnUpdateRemovesThemAll_Then_TheirRowsAreLookedUpAsOftenPerPortalAtThreeCounts()
        {
            // Arrange
            Func<int, int> lookups = RowLookupsUnderARefWhileUnmounting;

            // Act
            var growth = GrowthOfTheGrowth(lookups);

            // Assert
            Assert.That(growth, Is.EqualTo(0));
        }

        [Test]
        public void Given_SiblingPortalsBesideAComponent_When_AnUpdateRemovesThePortals_Then_TheRangesAreLookedUpAsOftenPerPortalAtThreeCounts()
        {
            // Arrange
            Func<int, int> lookups = RangeLookupsWhileUnmounting;

            // Act
            var growth = GrowthOfTheGrowth(lookups);

            // Assert
            Assert.That(growth, Is.EqualTo(0));
        }
    }
}
