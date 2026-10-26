using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a child fiber holds the link before it in its parent's chain, so removing a child does not
    /// walk the chain from the head, and that the link stays right through every other change to the chain.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentFiberPreviousLinkTests
    {
        // Whether the field exists is part of the reading, so a fiber that keeps no such link answers wrongly
        // rather than reading as a fiber whose link is null.
        private static (bool Tracked, ComponentFiber? Previous) PreviousOf(ComponentFiber fiber)
        {
            var field = typeof(ComponentFiber).GetField("_previousSibling", BindingFlags.Instance | BindingFlags.NonPublic);
            return (field != null, field?.GetValue(fiber) as ComponentFiber);
        }

        [Test]
        public void Given_ParentWithAppendedChildren_When_TheSecondChildsPreviousLinkIsRead_Then_ItIsTheFirstChild()
        {
            // Arrange
            var parent = new ComponentFiber();
            var first = new ComponentFiber();
            var second = new ComponentFiber();
            parent.AppendChild(first);
            parent.AppendChild(second);

            // Act
            var previous = PreviousOf(second);

            // Assert
            Assert.That(previous, Is.EqualTo((true, first)));
        }

        [Test]
        public void Given_ParentWhoseMiddleChildWasRemoved_When_TheLastChildsPreviousLinkIsRead_Then_ItIsTheFirstChild()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            var c = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.AppendChild(c);
            parent.RemoveChild(b);

            // Act
            var previous = PreviousOf(c);

            // Assert
            Assert.That(previous, Is.EqualTo((true, a)));
        }

        [Test]
        public void Given_ParentWhoseFirstChildWasRemoved_When_TheNewFirstChildsPreviousLinkIsRead_Then_ItIsNull()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.RemoveChild(a);

            // Act
            var previous = PreviousOf(b);

            // Assert
            Assert.That(previous, Is.EqualTo((true, (ComponentFiber?)null)));
        }

        [Test]
        public void Given_ParentWhoseChildrenWereReordered_When_TheThirdChildsPreviousLinkIsRead_Then_ItIsTheSecondInTheNewOrder()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            var c = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.AppendChild(c);
            parent.CommitChildOrder(new[] { c, a, b });

            // Act
            var previous = PreviousOf(b);

            // Assert
            Assert.That(previous, Is.EqualTo((true, a)));
        }

        [Test]
        public void Given_ParentWhoseChildrenWereReordered_When_TheMiddleChildIsRemoved_Then_TheChainSkipsIt()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            var c = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.AppendChild(c);
            parent.CommitChildOrder(new[] { c, a, b });

            // Act
            parent.RemoveChild(a);

            // Assert
            Assert.That((parent.Child, c.Sibling, PreviousOf(b)), Is.EqualTo((c, b, (true, c))));
        }

        [Test]
        public void Given_ChildRemovedFromItsParent_When_ItsPreviousLinkIsRead_Then_ItIsNull()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.RemoveChild(b);

            // Act
            var previous = PreviousOf(b);

            // Assert
            Assert.That(previous, Is.EqualTo((true, (ComponentFiber?)null)));
        }
    }
}
