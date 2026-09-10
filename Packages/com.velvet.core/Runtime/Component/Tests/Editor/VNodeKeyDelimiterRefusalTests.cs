using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the refusal of a key holding the reconciler's scope delimiter at each construct whose key
    /// reaches a different consumer of it: <c>V.Provider</c>'s reaches the Provider child scope,
    /// <c>V.Suspense</c>'s the boundary key, <c>V.AnimatePresence</c>'s the presence key, a keyed leaf's
    /// the scoped-key override its siblings are matched under (which is also what a presence child
    /// contributes), <c>V.Component</c>'s the Component child scope and <c>V.MemoizedWithKey</c>'s the
    /// dep-cache key. A <c>V.List</c> selector reaches the same override without passing through any
    /// <c>key:</c> parameter.
    /// </summary>
    /// <remarks>
    /// What a refusal takes from the pools is <see cref="VFactoryRefusalRentalTests"/>'s question.
    /// </remarks>
    [TestFixture]
    internal sealed class VNodeKeyDelimiterRefusalTests
    {
        private const string NulKey = "a\0b";

        // "accepted" rather than a null ParamName, so a call that stops throwing is told apart from one
        // throwing an ArgumentException that names some other argument.
        private static string Refusal(Action call)
        {
            try
            {
                call();
                return "accepted";
            }
            catch (ArgumentException ex)
            {
                return ex.ParamName ?? "unnamed";
            }
        }

        [Test]
        public void Given_AProviderKeyHoldingTheDelimiter_When_TheProviderIsBuilt_Then_TheKeyIsRefused()
        {
            // Arrange + Act
            var refusal = Refusal(() =>
                V.Provider(ComponentContext<string>.Create("d"), "v", Array.Empty<VNode?>(), NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        [Test]
        public void Given_ASuspenseKeyHoldingTheDelimiter_When_TheBoundaryIsBuilt_Then_TheKeyIsRefused()
        {
            // Arrange + Act
            var refusal = Refusal(() =>
                V.Suspense(V.Div("fallback"), Array.Empty<VNode?>(), NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        [Test]
        public void Given_APresenceKeyHoldingTheDelimiter_When_ThePresenceIsBuilt_Then_TheKeyIsRefused()
        {
            // Arrange + Act
            var refusal = Refusal(() => V.AnimatePresence(Array.Empty<VNode?>(), NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        [Test]
        public void Given_ALeafKeyHoldingTheDelimiter_When_TheLeafIsBuilt_Then_TheKeyIsRefused()
        {
            // Arrange + Act — a keyed leaf under an enclosing scope, which is what a presence child is
            var refusal = Refusal(() => V.Div(className: "leaf", key: NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        [Test]
        public void Given_AComponentKeyHoldingTheDelimiter_When_TheComponentIsBuilt_Then_TheKeyIsRefused()
        {
            // Arrange + Act
            var refusal = Refusal(() => V.Component(() => V.Div("body"), NulKey));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        [Test]
        public void Given_AMemoKeyHoldingTheDelimiter_When_TheMemoIsBuilt_Then_TheKeyIsRefused()
        {
            // Arrange + Act
            var refusal = Refusal(() => V.MemoizedWithKey(NulKey, () => V.Div("inner")));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        [Test]
        public void Given_AListSelectorReturningTheDelimiter_When_TheListIsMapped_Then_TheKeyIsRefused()
        {
            // Arrange + Act — no key: parameter carries this one; the selector writes it onto the node
            var refusal = Refusal(() => V.List(new[] { 1 }, _ => NulKey, _ => V.Div("row")));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        [Test]
        public void Given_AnIndexedListSelectorReturningTheDelimiter_When_TheListIsMapped_Then_TheKeyIsRefused()
        {
            // Arrange + Act
            var refusal = Refusal(() =>
                V.List(new[] { 1 }, (item, index) => NulKey, (item, index) => V.Div("row")));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        [Test]
        public void Given_AKeyOpeningOnTheDelimiter_When_TheNodeIsBuilt_Then_TheKeyIsRefused()
        {
            // Arrange + Act — the delimiter at index 0, which a search reporting where it sits reads
            // differently from one further in
            var refusal = Refusal(() => V.Div(className: "leaf", key: "\0b"));

            // Assert
            Assert.That(refusal, Is.EqualTo("key"));
        }

        // GREEN_ON_BASE(characterization): the base keeps an accepted key on the node too. The cases
        // above assert a throw, and this is their key with the delimiter taken out: what they read as
        // the refusal of a delimiter would read the same for a key refused on some other ground.
        [Test]
        public void Given_AKeyWithoutTheDelimiter_When_TheNodeIsBuilt_Then_TheKeyIsKept()
        {
            // Arrange + Act
            var node = V.Div(className: "leaf", key: "ab");

            // Assert
            Assert.That(node.Key, Is.EqualTo("ab"));
        }
    }
}
