// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the old instances a container rents at its entry (<see cref="OldSlotOccupants"/>) go
    /// back to <see cref="ReconcilerBufferPool"/> when the pass that rented them ends or a park holding them is
    /// discarded, come back holding no element, name none where nothing was read into them, and leave
    /// <see cref="ReconcilerContext.OpenOldInstances"/> with the walk that opened them.
    /// </summary>
    [TestFixture]
    internal sealed class OldSlotOccupantsPoolingTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            _reconciler = null!;
            _root = null!;
        }

        private static VNode[] Labels(string prefix, int count, bool keyed = false)
            => Enumerable.Range(0, count)
                .Select(i => (VNode)V.Label(text: prefix + i, key: keyed ? "k" + i : null))
                .ToArray();

        // The instance the next container rents, since the pool hands back what was returned last.
        private OldSlotOccupants PutOnTopOfThePool()
        {
            var pool = _reconciler.Context.BufferPool;
            var borrowed = pool.RentOldSlotOccupants();
            pool.ReturnOldSlotOccupants(borrowed);
            return borrowed;
        }

        [Test]
        public void Given_AnIndexedPass_When_ItEnds_Then_ItsOccupantsAreBackInThePool()
        {
            // Arrange
            var mounted = Labels("a", 3);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);
            var borrowed = PutOnTopOfThePool();

            // Act
            _reconciler.Reconcile(_root, mounted, Labels("b", 3));

            // Assert
            Assert.That(ReferenceEquals(_reconciler.Context.BufferPool.RentOldSlotOccupants(), borrowed), Is.True);
        }

        [Test]
        public void Given_AKeyedPass_When_ItEnds_Then_ItsOccupantsAreBackInThePool()
        {
            // Arrange
            var mounted = Labels("a", 3, keyed: true);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);
            var borrowed = PutOnTopOfThePool();

            // Act
            _reconciler.Reconcile(_root, mounted, Labels("b", 3, keyed: true));

            // Assert
            Assert.That(ReferenceEquals(_reconciler.Context.BufferPool.RentOldSlotOccupants(), borrowed), Is.True);
        }

        [Test]
        public void Given_AGeneralPathPass_When_ItEnds_Then_ItsOccupantsAreBackInThePool()
        {
            // Arrange — a Fragment on both sides sends the pass through the walk.
            var mounted = new VNode[] { V.Fragment(Labels("a", 3)) };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);
            var borrowed = PutOnTopOfThePool();

            // Act
            _reconciler.Reconcile(_root, mounted, new VNode[] { V.Fragment(Labels("b", 3)) });

            // Assert
            Assert.That(ReferenceEquals(_reconciler.Context.BufferPool.RentOldSlotOccupants(), borrowed), Is.True);
        }

        [Test]
        public void Given_AGeneralPathPass_When_ItEnds_Then_ItsOccupantsAreNoLongerOpen()
        {
            // Arrange
            var mounted = new VNode[] { V.Fragment(Labels("a", 3)) };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);

            // Act
            _reconciler.Reconcile(_root, mounted, new VNode[] { V.Fragment(Labels("b", 3)) });

            // Assert
            Assert.That(_reconciler.Context.OpenOldInstances, Is.Empty);
        }

        [Test]
        public void Given_AParkedIndexedPass_When_AFreshPassDiscardsIt_Then_ItsOccupantsAreBackInThePool()
        {
            // Arrange — a microsecond budget parks the diff, which keeps what it rented.
            var mounted = Labels("a", 100);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);
            var borrowed = PutOnTopOfThePool();
            var changed = Labels("b", 100);
            _reconciler.Reconcile(_root, mounted, changed, frameBudgetMs: 0.001);
            var parked = _reconciler.HasPendingWork;

            // Act
            _reconciler.Reconcile(_root, changed, changed);

            // Assert — null where nothing parked, which the expected value cannot equal.
            Assert.That(
                parked ? ReferenceEquals(_reconciler.Context.BufferPool.RentOldSlotOccupants(), borrowed) : (bool?)null,
                Is.True);
        }

        [Test]
        public void Given_AParkedKeyedPass_When_ItsLastSliceFinishesIt_Then_ItsOccupantsAreBackInThePool()
        {
            // Arrange — a microsecond budget parks the keyed diff, which keeps what it rented.
            var mounted = Labels("a", 100, keyed: true);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);
            var borrowed = PutOnTopOfThePool();
            _reconciler.Reconcile(_root, mounted, Labels("b", 100, keyed: true), frameBudgetMs: 0.001);
            var parked = _reconciler.HasPendingWork;

            // Act
            while (_reconciler.HasPendingWork) _reconciler.ContinueReconcile(frameBudgetMs: 0.001);

            // Assert — null where nothing parked, which the expected value cannot equal.
            Assert.That(
                parked ? ReferenceEquals(_reconciler.Context.BufferPool.RentOldSlotOccupants(), borrowed) : (bool?)null,
                Is.True);
        }

        // GREEN_ON_BASE(characterization): the base names no element from occupants nothing was read into.
        // The general walk over a null parent reads nothing into its instance, and its removals still ask it for each
        // old leaf's.
        [Test]
        public void Given_OccupantsNothingWasReadInto_When_TheFirstEntryIsAsked_Then_ItNamesNoElement()
        {
            // Arrange
            var occupants = _reconciler.Context.BufferPool.RentOldSlotOccupants();

            // Act
            var first = occupants.Occupant(0);

            // Assert
            Assert.That(first, Is.Null);
        }

        [Test]
        public void Given_FilledOccupants_When_ReturnedToThePool_Then_NoFieldHoldsAnElement()
        {
            // Arrange — every field is read by reflection, so a field added later is held to the same reset.
            var pool = _reconciler.Context.BufferPool;
            var parent = new VisualElement();
            for (var i = 0; i < 3; i++) parent.Add(new VisualElement());
            var patcher = (FiberNodePatcher)typeof(Reconciler)
                .GetProperty("NodePatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(_reconciler);
            var occupants = pool.RentOldSlotOccupants();
            occupants.Resolve(parent, 1, 2, int.MaxValue, patcher);
            var fields = typeof(OldSlotOccupants)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var unfilled = fields.Where(f => Held(f.GetValue(occupants)) == 0).Select(f => f.Name).ToList();

            // Act
            pool.ReturnOldSlotOccupants(occupants);

            // Assert — a field the resolve left empty proves nothing about the reset, so it is reported too.
            var report = unfilled.Select(name => "never filled: " + name)
                .Concat(fields.Where(f => Held(f.GetValue(occupants)) != 0).Select(f => "still holds: " + f.Name));
            Assert.That(string.Join("\n", report), Is.Empty);
        }

        // A collection holds its entries; any other field holds something unless it is its type's default.
        private static int Held(object? value)
            => value switch
            {
                null => 0,
                ICollection collection => collection.Count,
                _ when value.GetType().IsValueType => value.Equals(Activator.CreateInstance(value.GetType())) ? 0 : 1,
                _ => 1,
            };
    }
}
