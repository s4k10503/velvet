#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the result a component's first render reads over an entry that failed without ever holding
    /// data, TanStack Query's <c>getOptimisticResult</c>: the render whose subscription will fetch reports
    /// that fetch rather than the failure it is about to replace.
    /// <list type="bullet">
    /// <item>The result is pending with no error, and its failure count and reason are cleared.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// A first component fails the request; a second one then mounts over the entry, and its first render is
    /// what each case reads. The query function returns a completion source the case settles itself.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryOptimisticResultTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private VisualElement _secondRoot = null!;
        private static QueryClient s_client = null!;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_second = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _secondRoot = new VisualElement();
            s_client = new QueryClient();
            s_sources.Clear();
            s_second.Clear();
        }

        // GREEN_ON_BASE(characterization): the base already reports the fetch a mount over a failed entry starts.
        [Test]
        public void Given_AnEntryThatFailedWithoutData_When_AComponentMountsOverIt_Then_ItIsPending()
        {
            // Arrange
            using var first = MountFailed();

            // Act
            using var second = V.Mount(_secondRoot, V.Component(Second, key: "second"));

            // Assert
            Assert.That(s_second[0].Status, Is.EqualTo(QueryStatus.Pending), "The render reports the fetch that will replace the failure");
        }

        // GREEN_ON_BASE(characterization): the base already reports the fetch a mount over a failed entry starts.
        [Test]
        public void Given_AnEntryThatFailedWithoutData_When_AComponentMountsOverIt_Then_ItHasNoError()
        {
            // Arrange
            using var first = MountFailed();

            // Act
            using var second = V.Mount(_secondRoot, V.Component(Second, key: "second"));

            // Assert
            Assert.That(s_second[0].Error, Is.Null, "The fetch the render reports has not failed");
        }

        // GREEN_ON_BASE(characterization): the base already reports the fetch a mount over a failed entry starts.
        [Test]
        public void Given_AnEntryThatFailedWithoutData_When_AComponentMountsOverIt_Then_ItsFailureCountIsZero()
        {
            // Arrange
            using var first = MountFailed();

            // Act
            using var second = V.Mount(_secondRoot, V.Component(Second, key: "second"));

            // Assert
            Assert.That(s_second[0].FailureCount, Is.EqualTo(0), "The fetch the render reports starts counting failures afresh");
        }

        // GREEN_ON_BASE(characterization): the base already reports the fetch a mount over a failed entry starts.
        [Test]
        public void Given_AnEntryThatFailedWithoutData_When_AComponentMountsOverIt_Then_ItHasNoFailureReason()
        {
            // Arrange
            using var first = MountFailed();

            // Act
            using var second = V.Mount(_secondRoot, V.Component(Second, key: "second"));

            // Assert
            Assert.That(s_second[0].FailureReason, Is.Null, "The fetch the render reports has no failure to give a reason for");
        }

        #region Components and helpers

        private MountedTree MountFailed()
        {
            var mounted = V.Mount(_root, V.Component(First, key: "first"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetException(new InvalidOperationException("failed"));
            mounted.FlushStateForTest();
            return mounted;
        }

        private static QueryOptions<int> Options()
            => new(Todos, Fetch) { Retry = 0 };

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode First()
        {
            Hooks.UseQuery(Options(), s_client);
            return V.Label(text: "first");
        }

        [Component]
        private static VNode Second()
        {
            s_second.Add(Hooks.UseQuery(Options(), s_client));
            return V.Label(text: "second");
        }

        #endregion
    }
}
