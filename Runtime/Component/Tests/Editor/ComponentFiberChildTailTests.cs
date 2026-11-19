using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a parent fiber holds the last link of its child chain, so appending a child does not walk
    /// the chain, and that the link stays the last one through every other change to the chain.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentFiberChildTailTests
    {
        private static List<ComponentFiber> ChainOf(ComponentFiber parent)
        {
            var chain = new List<ComponentFiber>();
            for (var fiber = parent.Child; fiber != null; fiber = fiber.Sibling) chain.Add(fiber);
            return chain;
        }

        [Test]
        public void Given_ParentWithAppendedChildren_When_ItsLastLinkIsRead_Then_ItIsTheChildAppendedLast()
        {
            // Arrange
            var parent = new ComponentFiber();
            var first = new ComponentFiber();
            var last = new ComponentFiber();
            parent.AppendChild(first);
            parent.AppendChild(last);
            var field = typeof(ComponentFiber).GetField("_lastChild", BindingFlags.Instance | BindingFlags.NonPublic);

            // Act
            var held = field?.GetValue(parent);

            // Assert
            Assert.That(held, Is.SameAs(last));
        }

        // GREEN_ON_BASE(characterization): the chain order the base already produces by walking it, which the tail link must keep producing.
        [Test]
        public void Given_ParentWhoseLastChildWasRemoved_When_AChildIsAppended_Then_TheChainEndsWithIt()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            var c = new ComponentFiber();
            var d = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.AppendChild(c);
            parent.RemoveChild(c);

            // Act
            parent.AppendChild(d);

            // Assert
            Assert.That(ChainOf(parent), Is.EqualTo(new[] { a, b, d }));
        }

        // GREEN_ON_BASE(characterization): the chain order the base already produces by walking it, which the tail link must keep producing.
        [Test]
        public void Given_ParentWhoseOnlyChildWasRemoved_When_AChildIsAppended_Then_ItIsTheOnlyLink()
        {
            // Arrange
            var parent = new ComponentFiber();
            var removed = new ComponentFiber();
            var appended = new ComponentFiber();
            parent.AppendChild(removed);
            parent.RemoveChild(removed);

            // Act
            parent.AppendChild(appended);

            // Assert
            Assert.That(ChainOf(parent), Is.EqualTo(new[] { appended }));
        }

        // GREEN_ON_BASE(characterization): the chain order the base already produces by walking it, which the tail link must keep producing.
        [Test]
        public void Given_ParentWhoseChildrenWereReordered_When_AChildIsAppended_Then_TheChainEndsWithIt()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            var c = new ComponentFiber();
            var d = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.AppendChild(c);
            parent.CommitChildOrder(new[] { c, a, b });

            // Act
            parent.AppendChild(d);

            // Assert
            Assert.That(ChainOf(parent), Is.EqualTo(new[] { c, a, b, d }));
        }

        // GREEN_ON_BASE(characterization): the chain order the base already produces by walking it, which the tail link must keep producing.
        [Test]
        public void Given_ParentWhoseMiddleChildWasRemoved_When_AChildIsAppended_Then_TheChainEndsWithIt()
        {
            // Arrange
            var parent = new ComponentFiber();
            var a = new ComponentFiber();
            var b = new ComponentFiber();
            var c = new ComponentFiber();
            var d = new ComponentFiber();
            parent.AppendChild(a);
            parent.AppendChild(b);
            parent.AppendChild(c);
            parent.RemoveChild(b);

            // Act
            parent.AppendChild(d);

            // Assert
            Assert.That(ChainOf(parent), Is.EqualTo(new[] { a, c, d }));
        }
    }
}
