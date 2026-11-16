using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The <see cref="ChildListWrites"/> cases that need a child on a panel: an event UI Toolkit sends from inside
    /// the call a write makes, and a write made between two passes.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerRowsPanelTests : PanelTestBase
    {
        [Test]
        public void Given_AHandlerThatWritesTheDestinationFromTheChildsDetach_When_TheChildIsMovedThere_Then_TheModelHoldsBothChildren()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var source = new VisualElement();
            var destination = new VisualElement();
            ChildListWrites.Add(root, source);
            ChildListWrites.Add(root, destination);
            var child = new VisualElement();
            ChildListWrites.Add(source, child);
            child.RegisterCallback<DetachFromPanelEvent>(_ => ChildListWrites.Add(destination, new VisualElement()));

            // Act
            ChildListWrites.Add(destination, child);

            // Assert
            Assert.That(RowModelAccess.Peek(destination)!.TotalCount, Is.EqualTo(destination.childCount));
        }

        [Test]
        public void Given_ANestedInsertAtTheSameIndexFromTheChildsDetach_When_TheChildIsInsertedThere_Then_TheModelHoldsTheLiveOrder()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var source = new VisualElement();
            var destination = new VisualElement();
            ChildListWrites.Add(root, source);
            ChildListWrites.Add(root, destination);
            ChildListWrites.Add(destination, new VisualElement());
            ChildListWrites.Add(destination, new VisualElement());
            var child = new VisualElement();
            ChildListWrites.Add(source, child);
            child.RegisterCallback<DetachFromPanelEvent>(_ => ChildListWrites.Insert(destination, 1, new VisualElement()));

            // Act
            ChildListWrites.Insert(destination, 1, child);

            // Assert
            Assert.That(ContainerRowsTests.CompareOrder(destination, RowModelAccess.Peek(destination)!), Is.Null);
        }

        [Test]
        public void Given_AWriteBetweenPassesThenAForeignReorder_When_APassBegins_Then_TheDriftIsLogged()
        {
            // Arrange
            var container = new VisualElement();
            _window.rootVisualElement.Add(container);
            ChildListWrites.Add(container, new VisualElement());
            ChildListWrites.Add(container, new VisualElement());
            container[0].BringToFront();
            LogAssert.Expect(LogType.Assert, new Regex("ContainerRows drifted"));

            // Act
            ChildListWrites.BeginPass();

            // Assert
            Assert.That(RowModelAccess.Peek(container)!.ElementAt(0), Is.SameAs(container[0]));
        }

        [Test]
        public void Given_AForeignReorderThatWasAnnounced_When_ThePassEnds_Then_NothingIsLogged()
        {
            // Arrange
            var container = new VisualElement();
            _window.rootVisualElement.Add(container);
            ChildListWrites.Add(container, new VisualElement());
            ChildListWrites.Add(container, new VisualElement());
            container[0].BringToFront();
            ChildListWrites.NoteHierarchyChange(container[0]);

            // Act
            ChildListWrites.VerifyTouched();

            // Assert
            Assert.That(RowModelAccess.Peek(container)!.ElementAt(0), Is.SameAs(container[0]));
        }

        // GREEN_ON_BASE(characterization): the base watches the panel a pass reconciles onto. Drop
        // `_ctx.RelationalTreeWatch.Watch(parent?.panel);` and no pass registers the watch.
        [Test]
        public void Given_APassOnAPanel_When_ItBegins_Then_TheContextWatchesThatPanel()
        {
            // Arrange
            var panel = _window.rootVisualElement.panel;

            // Act
            using var mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card"));

            // Assert
            var watched = (System.Collections.IDictionary)typeof(RelationalTreeWatch)
                .GetField("_watched", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(mounted.Root.Reconciler.Context.RelationalTreeWatch);
            Assert.That(watched.Contains(panel), Is.True);
        }

        // The row count the model of container holds at the moment child leaves the panel, which is inside the call of
        // the write that detaches it; -2 where the model is gone and -1 where child never left.
        private static Func<int> RowsSeenOnDetach(VisualElement container, VisualElement child)
        {
            var seen = -1;
            child.RegisterCallback<DetachFromPanelEvent>(_ => seen = RowModelAccess.Peek(container)?.TotalCount ?? -2);
            return () => seen;
        }

        private VisualElement Attached(VisualElement first, VisualElement second)
        {
            var container = new VisualElement();
            ChildListWrites.Add(_window.rootVisualElement, container);
            ChildListWrites.Add(container, first);
            ChildListWrites.Add(container, second);
            return container;
        }

        // GREEN_ON_BASE(characterization): a hierarchy change announced inside a write marks an element that write does not touch for comparison.
        [Test]
        public void Given_AnElementOutsideTheWriteWhoseListChangedBehindItsModel_When_ItsChangeIsAnnouncedFromTheWritesDetach_Then_ItsModelIsRebuilt()
        {
            // Arrange
            var root = _window.rootVisualElement;
            var source = new VisualElement();
            var destination = new VisualElement();
            var bystander = new VisualElement();
            ChildListWrites.Add(root, source);
            ChildListWrites.Add(root, destination);
            ChildListWrites.Add(bystander, new VisualElement());
            ChildListWrites.Add(bystander, new VisualElement());
            bystander.Add(new VisualElement());
            var child = new VisualElement();
            ChildListWrites.Add(source, child);
            child.RegisterCallback<DetachFromPanelEvent>(_ => ChildListWrites.NoteHierarchyChange(bystander));

            // Act
            ChildListWrites.Add(destination, child);

            // Assert
            Assert.That(RowModelAccess.Peek(bystander)!.TotalCount, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(characterization): the model drops a removed child before the call that detaches it.
        [Test]
        public void Given_AChildOfTwo_When_ItIsRemoved_Then_TheModelHoldsOneWhileItLeaves()
        {
            // Arrange
            var child = new VisualElement();
            var container = Attached(new VisualElement(), child);
            var seen = RowsSeenOnDetach(container, child);

            // Act
            ChildListWrites.Remove(container, child);

            // Assert
            Assert.That(seen(), Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the model drops the first child by index before the call that detaches it.
        [Test]
        public void Given_AFirstChildOfTwo_When_ItIsRemovedByIndex_Then_TheModelHoldsOneWhileItLeaves()
        {
            // Arrange
            var child = new VisualElement();
            var container = Attached(child, new VisualElement());
            var seen = RowsSeenOnDetach(container, child);

            // Act
            ChildListWrites.RemoveAt(container, 0);

            // Assert
            Assert.That(seen(), Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the model drops a child removed from its own parent before the call that detaches it.
        [Test]
        public void Given_AChildOfTwo_When_ItIsRemovedFromItsHierarchy_Then_TheModelHoldsOneWhileItLeaves()
        {
            // Arrange
            var child = new VisualElement();
            var container = Attached(new VisualElement(), child);
            var seen = RowsSeenOnDetach(container, child);

            // Act
            ChildListWrites.RemoveFromHierarchy(child);

            // Assert
            Assert.That(seen(), Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the model is emptied before the call that detaches the children.
        [Test]
        public void Given_AContainerOfTwo_When_ItIsCleared_Then_TheModelHoldsNothingWhileTheChildrenLeave()
        {
            // Arrange
            var child = new VisualElement();
            var container = Attached(child, new VisualElement());
            var seen = RowsSeenOnDetach(container, child);

            // Act
            ChildListWrites.Clear(container);

            // Assert
            Assert.That(seen(), Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base compares a write's own container child for child once a write
        // nested in it has run. Delete `Settle(container, nestedRan);` from ChildListWrites.Exit and the model keeps
        // the inserted child a slot early.
        [Test]
        public void Given_AHandlerThatMovesTheFirstChildOutFromTheInsertedChildsDetach_When_TheChildIsInsertedMidList_Then_TheModelHoldsTheLiveOrder()
        {
            // Arrange — the nested write takes the first child out after the model has placed the inserted one, which
            // leaves the model holding it a slot early with its count, first child and last child agreeing with the
            // live list.
            var root = _window.rootVisualElement;
            var source = new VisualElement();
            var destination = new VisualElement();
            ChildListWrites.Add(root, source);
            ChildListWrites.Add(root, destination);
            var leaving = new VisualElement();
            ChildListWrites.Add(destination, leaving);
            for (var i = 0; i < 4; i++) ChildListWrites.Add(destination, new VisualElement());
            var child = new VisualElement();
            ChildListWrites.Add(source, child);
            child.RegisterCallback<DetachFromPanelEvent>(_ => ChildListWrites.Add(new VisualElement(), leaving));

            // Act
            ChildListWrites.Insert(destination, 2, child);

            // Assert
            Assert.That(ContainerRowsTests.CompareOrder(destination, RowModelAccess.Peek(destination)!), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base empties a model before it fills it from the sorted list. Delete
        // `rows.Clear();` from ChildListWrites.HierarchySort and a sort nested in another write leaves the model
        // holding each child twice until the outer write ends.
        [Test]
        public void Given_AHandlerThatSortsAContainerFromAChildsDetach_When_TheChildIsRemoved_Then_AReadInTheHandlerCountsEachSortedChildOnce()
        {
            // Arrange
            var sorted = new VisualElement();
            ChildListWrites.Add(sorted, new VisualElement { name = "b" });
            ChildListWrites.Add(sorted, new VisualElement { name = "a" });
            var child = new VisualElement();
            var container = Attached(new VisualElement(), child);
            var counted = -1;
            child.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ChildListWrites.HierarchySort(sorted, (x, y) => string.CompareOrdinal(x.name, y.name));
                counted = ChildListWrites.RenderedCount(sorted);
            });

            // Act
            ChildListWrites.Remove(container, child);

            // Assert
            Assert.That(counted, Is.EqualTo(2));
        }
    }
}
