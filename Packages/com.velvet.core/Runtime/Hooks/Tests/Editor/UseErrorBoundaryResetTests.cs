using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="Hooks.UseErrorBoundaryReset"/>, react-error-boundary's <c>resetErrorBoundary</c> and
    /// <c>resetKeys</c>.
    /// <list type="bullet">
    /// <item>Invoked after a <see cref="Hooks.Use{T}(Func{System.Threading.CancellationToken, VelvetTask{T}}, object)"/>
    /// load failed into the boundary, the action renders the children again and the load runs again.</item>
    /// <item>A reset key that changes while the boundary shows its fallback does the same, a key array of a new
    /// length included; a render passing the same keys keeps the fallback, after a reset as before one.</item>
    /// <item>A key that changes in the render in which a child throws does not undo that catch.</item>
    /// <item>Invoked while the boundary shows its children, the action schedules no render.</item>
    /// <item>The action is reference-stable, and the hook throws for a component that is not a boundary.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class UseErrorBoundaryResetTests
    {
        private sealed record BoundaryProps(object[] ResetKeys, int Tick);

        private static List<VelvetTaskCompletionSource<string>> s_sources;
        private static Action s_reset;
        private static Action<int> s_setResetKey;
        private static Action<bool> s_setExtraKey;
        private static Action<int> s_setTick;
        private static int s_boundaryRenders;
        private static bool s_readerThrows;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_sources = new List<VelvetTaskCompletionSource<string>>();
            s_reset = null;
            s_setResetKey = null;
            s_setExtraKey = null;
            s_setTick = null;
            s_boundaryRenders = 0;
            s_readerThrows = false;
            FiberStrictMode.Enabled = false;
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private static void Settle(MountedTree mounted, Action completion)
        {
            completion();
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static VelvetTaskCompletionSource<string> LatestLoad() => s_sources[s_sources.Count - 1];

        private MountedTree MountFailedLoad()
        {
            var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            Settle(mounted, () => LatestLoad().TrySetException(new InvalidOperationException("offline")));
            return mounted;
        }

        [Test]
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsResetIsInvoked_Then_TheLoadRunsAgainAndItsValueRenders()
        {
            // Arrange
            using var mounted = MountFailedLoad();
            var caught = Texts();

            // Act
            Settle(mounted, () => s_reset.Invoke());
            Settle(mounted, () => LatestLoad().TrySetResult("data"));

            // Assert — the catch is folded in, since a load that never failed renders its value as well
            Assert.That(caught + "|" + Texts(), Is.EqualTo("failed:offline|loaded:data"));
        }

        [Test]
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsResetKeyChanges_Then_TheLoadRunsAgainAndItsValueRenders()
        {
            // Arrange
            using var mounted = MountFailedLoad();
            var caught = Texts();

            // Act
            Settle(mounted, () => s_setResetKey.Invoke(1));
            Settle(mounted, () => LatestLoad().TrySetResult("data"));

            // Assert — the catch is folded in, since a load that never failed renders its value as well
            Assert.That(caught + "|" + Texts(), Is.EqualTo("failed:offline|loaded:data"));
        }

        [Test]
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsResetKeysGainAnElement_Then_TheLoadRunsAgainAndItsValueRenders()
        {
            // Arrange
            using var mounted = MountFailedLoad();
            var caught = Texts();

            // Act
            Settle(mounted, () => s_setExtraKey.Invoke(true));
            Settle(mounted, () => LatestLoad().TrySetResult("data"));

            // Assert — the catch is folded in, since a load that never failed renders its value as well
            Assert.That(caught + "|" + Texts(), Is.EqualTo("failed:offline|loaded:data"));
        }

        [Test]
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsParentRendersAgainWithTheSameResetKeys_Then_ItKeepsItsFallbackAndLoadsNothing()
        {
            // Arrange
            using var mounted = MountFailedLoad();

            // Act
            Settle(mounted, () => s_setTick.Invoke(1));

            // Assert
            Assert.That((Texts(), s_sources.Count), Is.EqualTo(("failed:offline", 1)));
        }

        [Test]
        public void Given_ABoundaryResetOnceWhoseLoadFailsAgain_When_ItsParentRendersAgain_Then_ItKeepsItsFallbackAndLoadsNothing()
        {
            // Arrange
            using var mounted = MountFailedLoad();
            Settle(mounted, () => s_reset.Invoke());
            Settle(mounted, () => LatestLoad().TrySetException(new InvalidOperationException("still offline")));

            // Act
            Settle(mounted, () => s_setTick.Invoke(1));

            // Assert
            Assert.That((Texts(), s_sources.Count), Is.EqualTo(("failed:still offline", 2)));
        }

        [Test]
        public void Given_AResetKeyThatChangesInTheRenderInWhichAChildThrows_When_ItsParentRendersAgain_Then_TheBoundaryKeepsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            Settle(mounted, () => LatestLoad().TrySetResult("data"));
            s_readerThrows = true;
            Settle(mounted, () => s_setResetKey.Invoke(1));
            var caught = Texts();
            s_readerThrows = false;

            // Act
            Settle(mounted, () => s_setTick.Invoke(1));

            // Assert — the catch is folded in, since without the throw both readings show the value
            Assert.That(caught + "|" + Texts(), Is.EqualTo("failed:thrown|failed:thrown"));
        }

        [Test]
        public void Given_ABoundaryShowingItsChildren_When_ItsResetIsInvoked_Then_ItDoesNotRenderAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            Settle(mounted, () => LatestLoad().TrySetResult("data"));
            var rendersBefore = s_boundaryRenders;

            // Act
            Settle(mounted, () => s_reset.Invoke());

            // Assert
            Assert.That(s_boundaryRenders - rendersBefore, Is.EqualTo(0));
        }

        [Test]
        public void Given_ABoundary_When_ItRendersAgain_Then_ItReturnsTheSameResetAction()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            var first = s_reset;

            // Act
            Settle(mounted, () => s_setTick.Invoke(1));

            // Assert
            Assert.That(s_reset, Is.SameAs(first));
        }

        [Test]
        public void Given_AComponentThatIsNotAnErrorBoundary_When_ItCallsTheHook_Then_ItThrowsInvalidOperationException()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(OuterBoundaryRender, key: "outer"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo(nameof(InvalidOperationException)));
        }

        [Component(Compiler = false)]
        private static VNode ReaderRender()
        {
            if (s_readerThrows) throw new InvalidOperationException("thrown");
            var value = Hooks.Use(_ =>
            {
                var source = new VelvetTaskCompletionSource<string>();
                s_sources.Add(source);
                return source.Task;
            }, resourceKey: "data");
            return V.Label(text: "loaded:" + value);
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode ResettableBoundaryRender(BoundaryProps props)
        {
            s_boundaryRenders++;
            s_reset = Hooks.UseErrorBoundaryReset(props.ResetKeys);
            Hooks.UseFallback(ex => V.Label(text: "failed:" + ex.Message));
            return V.Suspense(V.Label(text: "loading"), new VNode[] { V.Component(ReaderRender, key: "reader") });
        }

        [Component(Compiler = false)]
        private static VNode HostRender()
        {
            var (resetKey, setResetKey) = Hooks.UseState(0);
            var (extraKey, setExtraKey) = Hooks.UseState(false);
            var (tick, setTick) = Hooks.UseState(0);
            s_setResetKey = setResetKey;
            s_setExtraKey = setExtraKey;
            s_setTick = setTick;
            var keys = extraKey ? new object[] { resetKey, "extra" } : new object[] { resetKey };
            return V.Div(children: new VNode[]
            {
                V.Component(ResettableBoundaryRender, new BoundaryProps(keys, tick), key: "boundary"),
            });
        }

        [Component(Compiler = false)]
        private static VNode NotABoundaryRender()
        {
            Hooks.UseErrorBoundaryReset();
            return V.Label(text: "child");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OuterBoundaryRender()
        {
            Hooks.UseFallback(ex => V.Label(text: ex.GetType().Name));
            return V.Component(NotABoundaryRender, key: "child");
        }
    }
}
