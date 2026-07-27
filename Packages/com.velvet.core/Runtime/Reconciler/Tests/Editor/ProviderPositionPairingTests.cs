using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the inline-expansion walk compares a Provider against the Provider that held the same
    /// tree position on the previous render, so a value change always reaches the consumers below it — even
    /// when the number of Providers the walk emits before it differs between the two sides.
    /// <list type="bullet">
    /// <item>A conditional Provider appearing earlier among its siblings does not stop a later Provider's own
    /// value change from reaching its consumers.</item>
    /// <item>A Provider placed after a suspended Suspense keeps notifying its consumers while the boundary's
    /// primary subtree — which contributes its own Providers to the walk before suspending — stays pending.</item>
    /// <item>A Provider inside a Suspense fallback keeps notifying its consumers while the primary subtree
    /// also declares one.</item>
    /// <item>With two Providers in a fallback, the last one keeps notifying its consumers.</item>
    /// <item>A Provider whose own sibling index moves — a preceding child appearing in a longer children
    /// array — still notifies its consumers: a position with no counterpart on the old side falls back to
    /// pairing in walk order rather than treating the Provider as newly mounted.</item>
    /// <item>Two Providers each at index 0 of their own unkeyed Fragment hold distinct positions, so one of
    /// those Fragments appearing does not displace the other Provider's comparison. Neither Fragment reaches
    /// the fiber-keying scope chain — it stays null for both Providers — so this is the case that requires
    /// the position to be a structural path of its own rather than that scope.</item>
    /// <item>A Provider matched by position takes its place in walk order all the same, so a later Provider
    /// that falls back to walk order lands on its own counterpart rather than on the one already matched
    /// ahead of it.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Each host
    /// drives every value it changes from a single <c>UseState</c> tick, so one setter call produces exactly
    /// one re-render in which the Provider sequence and the observed Provider's value change together. The
    /// consumer is <c>[Component(Memoize = true)]</c> with no props: it re-renders only when the context
    /// notification marks it dirty, so a missed notification is visible as a stale Label in the tree — a plain
    /// consumer would be re-rendered by the parent walk regardless and hide the defect. The suspending child
    /// is driven by a <see cref="UniTaskCompletionSource{T}"/> that is deliberately left pending.
    /// </remarks>
    [TestFixture]
    internal sealed class ProviderPositionPairingTests
    {
        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("theme-default");
        private static readonly ComponentContext<string> OtherContext = ComponentContext<string>.Create("other-default");

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setTick = null;
            s_pendingFactory = null;
        }

        [Test]
        public void Given_ProviderAfterConditionalSibling_When_SiblingAppearsAsValueChanges_Then_ConsumerRendersNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ConditionalSiblingHostRender, key: "host"));
            Assume.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("initial"),
                "Precondition: the consumer mounted with the initial value");

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("updated"),
                "A Provider appearing before it in the same render does not cost the second Provider its own change notification");
        }

        [Test]
        public void Given_ProviderAfterSuspendedBoundary_When_ItsValueChanges_Then_ConsumerRendersNewValue()
        {
            // Arrange
            var pending = new UniTaskCompletionSource<string>();
            s_pendingFactory = _ => pending.Task;
            using var mounted = V.Mount(_root, V.Component(SuspenseThenProviderHostRender, key: "host"));
            Assume.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("initial"),
                "Precondition: the consumer mounted with the initial value beside the suspended boundary");

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("updated"),
                "A Provider following a still-pending Suspense observes its own previous value, not the boundary's");
        }

        [Test]
        public void Given_ProvidersInBothSuspenseBranches_When_FallbackProviderValueChanges_Then_ConsumerRendersNewValue()
        {
            // Arrange
            var pending = new UniTaskCompletionSource<string>();
            s_pendingFactory = _ => pending.Task;
            using var mounted = V.Mount(_root, V.Component(BothBranchesProviderHostRender, key: "host"));
            Assume.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("initial"),
                "Precondition: the fallback's consumer mounted with the initial value");

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("updated"),
                "The fallback's Provider is compared against the fallback's previous Provider, not the primary's");
        }

        [Test]
        public void Given_TwoProvidersInSuspenseFallback_When_LastProviderValueChanges_Then_ConsumerRendersNewValue()
        {
            // Arrange
            var pending = new UniTaskCompletionSource<string>();
            s_pendingFactory = _ => pending.Task;
            using var mounted = V.Mount(_root, V.Component(TwoFallbackProvidersHostRender, key: "host"));
            Assume.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("initial"),
                "Precondition: the consumer under the fallback's second Provider mounted with the initial value");

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("updated"),
                "Each fallback Provider keeps its own position, so the last one is not pushed past the end of the old side");
        }

        [Test]
        public void Given_ProviderPrecededByAppearingSibling_When_ItsValueChanges_Then_ConsumerRendersNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ShiftedSiblingIndexHostRender, key: "host"));
            Assume.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("initial"),
                "Precondition: the consumer mounted with the initial value as the only child");

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("updated"),
                "A non-Provider child appearing ahead of it moves the Provider's sibling index but not its place in walk order");
        }

        [Test]
        public void Given_ProvidersInSeparateFragments_When_OneFragmentAppearsAsValueChanges_Then_ConsumerRendersNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SeparateFragmentsHostRender, key: "host"));
            Assume.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("initial"),
                "Precondition: the consumer mounted with the initial value under the second Fragment");

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("updated"),
                "Each Fragment gives the Provider under it a position of its own, which the fiber-keying scope alone does not");
        }

        [Test]
        public void Given_ProviderMatchedByPosition_When_ALaterProviderFallsBackToWalkOrder_Then_ConsumerRendersNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PositionHitThenMissHostRender, key: "host"));
            Assume.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("initial"),
                "Precondition: the consumer mounted with the initial value under the second Provider");

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("consumer")?.text, Is.EqualTo("updated"),
                "The Provider matched by position still takes walk-order slot 0, leaving slot 1 for the one that falls back");
        }

        #region Consumer

        // Memoized with no props: only a context notification can re-render it, so its Label goes stale
        // exactly when the notification is missed.
        [Component(Memoize = true)]
        private static VNode MemoizedConsumerRender()
            => V.Label(name: "consumer", text: Hooks.UseContext(ThemeContext));

        #endregion

        #region Suspending child

        private static Func<CancellationToken, UniTask<string>> s_pendingFactory;

        // The factory lambda captures only a static field, so its delegate identity is stable across renders
        // and the resource is not re-fetched per render.
        [Component]
        private static VNode PendingChildRender()
            => V.Label(text: Hooks.Use(ct => s_pendingFactory(ct)));

        #endregion

        #region Hosts (one tick drives both the Provider sequence and the observed value)

        private static Action<int> s_setTick;

        [Component]
        private static VNode ConditionalSiblingHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "host", children: new VNode[]
            {
                tick == 0
                    ? null
                    : V.Provider(OtherContext, "leading", new VNode[] { V.Label(text: "leading") }),
                V.Provider(ThemeContext, tick == 0 ? "initial" : "updated", new VNode[]
                {
                    V.Component(MemoizedConsumerRender, key: "consumer"),
                }),
            });
        }

        // The children array grows by a non-Provider child ahead of the Provider, so the Provider keeps its
        // place in walk order (still the first and only one) while its sibling index moves from 0 to 1.
        [Component]
        private static VNode ShiftedSiblingIndexHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var themeProvider = V.Provider(ThemeContext, tick == 0 ? "initial" : "updated", new VNode[]
            {
                V.Component(MemoizedConsumerRender, key: "consumer"),
            });
            return V.Div(name: "host", children: tick == 0
                ? new VNode[] { themeProvider }
                : new VNode[] { V.Label(text: "banner"), themeProvider });
        }

        // Each Provider is the only child of its own unkeyed Fragment, so both sit at node index 0 and neither
        // Fragment contributes to the fiber-keying scope (it stays null throughout). Only a position that
        // descends through the Fragments themselves tells the two apart.
        [Component]
        private static VNode SeparateFragmentsHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "host", children: new VNode[]
            {
                tick == 0
                    ? null
                    : V.Fragment(new VNode[]
                    {
                        V.Provider(OtherContext, "leading", new VNode[] { V.Label(text: "leading") }),
                    }),
                V.Fragment(new VNode[]
                {
                    V.Provider(ThemeContext, tick == 0 ? "initial" : "updated", new VNode[]
                    {
                        V.Component(MemoizedConsumerRender, key: "consumer"),
                    }),
                }),
            });
        }

        // The first Provider keeps its index and so is matched by position; the second is pushed along by the
        // appearing banner and falls back to walk order. Both provide the same context, and the first one's
        // value is what the second one is changing TO — so pairing the second against the first reads as "no
        // change" and notifies nobody, which is what makes the ordinal a position hit consumes observable.
        [Component]
        private static VNode PositionHitThenMissHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var pinned = V.Provider(ThemeContext, "updated", new VNode[] { V.Label(text: "pinned") });
            var observed = V.Provider(ThemeContext, tick == 0 ? "initial" : "updated", new VNode[]
            {
                V.Component(MemoizedConsumerRender, key: "consumer"),
            });
            return V.Div(name: "host", children: tick == 0
                ? new VNode[] { pinned, observed }
                : new VNode[] { pinned, V.Label(text: "banner"), observed });
        }

        [Component]
        private static VNode SuspenseThenProviderHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "host", children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Provider(OtherContext, "primary", new VNode[]
                        {
                            V.Component(PendingChildRender, key: "async"),
                        }),
                    }),
                V.Provider(ThemeContext, tick == 0 ? "initial" : "updated", new VNode[]
                {
                    V.Component(MemoizedConsumerRender, key: "consumer"),
                }),
            });
        }

        [Component]
        private static VNode BothBranchesProviderHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Suspense(
                fallback: V.Provider(ThemeContext, tick == 0 ? "initial" : "updated", new VNode[]
                {
                    V.Component(MemoizedConsumerRender, key: "consumer"),
                }),
                children: new VNode[]
                {
                    V.Provider(OtherContext, "primary", new VNode[]
                    {
                        V.Component(PendingChildRender, key: "async"),
                    }),
                });
        }

        [Component]
        private static VNode TwoFallbackProvidersHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Suspense(
                fallback: V.Fragment(new VNode[]
                {
                    V.Provider(OtherContext, tick == 0 ? "first" : "first-updated", new VNode[]
                    {
                        V.Label(text: "sibling"),
                    }),
                    V.Provider(ThemeContext, tick == 0 ? "initial" : "updated", new VNode[]
                    {
                        V.Component(MemoizedConsumerRender, key: "consumer"),
                    }),
                }),
                children: new VNode[]
                {
                    V.Provider(OtherContext, "primary", new VNode[]
                    {
                        V.Component(PendingChildRender, key: "async"),
                    }),
                });
        }

        #endregion
    }
}
