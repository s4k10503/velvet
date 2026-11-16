// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a component a pass removes is not paired again by a later walk of the same pass: where a
    /// different component arrives at the key it held, the arrival mounts fresh rather than taking the
    /// departed one's place in the registry.
    /// </summary>
    [TestFixture]
    internal sealed class HiddenFiberPairingTests
    {
        private VisualElement _root = null!;
        private static SwapStore s_swap;
        private static List<string> s_log = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            s_swap?.Dispose();
            s_swap = null;
            _root = null!;
        }

        private readonly record struct SwapState(bool First);

        private sealed class SwapStore : Store<SwapState>
        {
            public SwapStore() : base(new SwapState(true)) { }
            public void ShowSecond() => SetState(_ => new SwapState(false));
            protected override void ResetCore() => SetState(_ => new SwapState(true));
        }

        // GREEN_ON_BASE(characterization): the base already unregisters a component as it disposes it, so a later walk of the pass cannot pair it; this pins that outcome for the step that splits the unregister.
        [Test]
        public void Given_AComponentReplacedAtItsKey_When_TheArrivalRendersInTheSamePass_Then_ItMountsAfterTheDepartedOneCleansUp()
        {
            // Arrange
            s_swap = new SwapStore();
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();

            // Act
            s_swap.ShowSecond();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("first:cleanup,second:mount"));
        }

        [Component]
        private static VNode HostRender()
        {
            var first = Hooks.UseStore(s_swap, state => state.First);
            return V.Div(name: "host", children: new VNode[]
            {
                first ? V.Component(FirstRender, key: "slot") : V.Component(SecondRender, key: "slot"),
            });
        }

        [Component]
        private static VNode FirstRender()
        {
            Hooks.UseEffect(() =>
            {
                s_log.Add("first:mount");
                return () => s_log.Add("first:cleanup");
            }, Array.Empty<object>());
            return V.Label(text: "first");
        }

        [Component]
        private static VNode SecondRender()
        {
            Hooks.UseEffect(() =>
            {
                s_log.Add("second:mount");
                return () => s_log.Add("second:cleanup");
            }, Array.Empty<object>());
            return V.Label(text: "second");
        }
    }
}
