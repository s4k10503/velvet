using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the append log a reader opens on a parent fiber: it names the children appended after the mark the
    /// reader took and no earlier one, only the ones still children of the parent, and it holds nothing once the
    /// last reader has closed it.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentFiberAppendLogTests
    {
        [Test]
        public void Given_ChildrenAppendedBeforeAndAfterAMark_When_AppendedSinceIsRead_Then_OnlyTheOnesAfterItAreReturned()
        {
            // Arrange
            var parent = new ComponentFiber();
            var before = new ComponentFiber();
            var after = new ComponentFiber();
            parent.BeginAppendLog();
            parent.AppendChild(before);
            var mark = parent.BeginAppendLog();
            parent.AppendChild(after);

            // Act
            var appended = parent.AppendedSince(mark).ToList();

            // Assert
            Assert.That(appended, Is.EqualTo(new[] { after }));
        }

        [Test]
        public void Given_AChildAppendedAfterAMarkAndThenRemoved_When_AppendedSinceIsRead_Then_ItIsNotReturned()
        {
            // Arrange
            var parent = new ComponentFiber();
            var mark = parent.BeginAppendLog();
            var removed = new ComponentFiber();
            var kept = new ComponentFiber();
            parent.AppendChild(removed);
            parent.AppendChild(kept);
            parent.RemoveChild(removed);

            // Act
            var appended = parent.AppendedSince(mark).ToList();

            // Assert
            Assert.That(appended, Is.EqualTo(new[] { kept }));
        }

        [Test]
        public void Given_TwoReadersOnOneParent_When_TheInnerOneCloses_Then_TheOuterOneStillReadsItsChildren()
        {
            // Arrange
            var parent = new ComponentFiber();
            var outerMark = parent.BeginAppendLog();
            var first = new ComponentFiber();
            parent.AppendChild(first);
            parent.BeginAppendLog();
            var second = new ComponentFiber();
            parent.AppendChild(second);
            parent.EndAppendLog();

            // Act
            var appended = parent.AppendedSince(outerMark).ToList();

            // Assert
            Assert.That(appended, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void Given_TheLastReaderHasClosedTheLog_When_AChildIsAppended_Then_TheParentKeepsNoLog()
        {
            // Arrange
            var parent = new ComponentFiber();
            parent.BeginAppendLog();
            parent.EndAppendLog();
            parent.AppendChild(new ComponentFiber());
            var field = typeof(ComponentFiber).GetField("_appendLog", BindingFlags.Instance | BindingFlags.NonPublic);

            // Act
            var held = field?.GetValue(parent);

            // Assert
            Assert.That(held, Is.Null);
        }
    }
}
