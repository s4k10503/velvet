using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="Hooks.UseErrorBoundaryReset"/> and <see cref="Hooks.UseErrorBoundary"/>,
    /// react-error-boundary's <c>resetErrorBoundary</c>, <c>resetKeys</c>, <c>onReset</c> and
    /// <c>useErrorBoundary()</c>.
    /// <list type="bullet">
    /// <item>Invoked after a <see cref="Hooks.Use{T}(Func{System.Threading.CancellationToken, VelvetTask{T}}, object)"/>
    /// load failed into the boundary, the action renders the children again and the load runs again.</item>
    /// <item>A reset key that changes while the boundary shows its fallback does the same, a key array of a new
    /// length included; a render passing the same keys keeps the fallback, after a reset of either kind as before
    /// one.</item>
    /// <item>A key that changes in the render in which a child throws does not undo that catch.</item>
    /// <item>Invoked while the boundary shows its children, the action schedules no render.</item>
    /// <item>The reset is reference-stable and works converted to an <see cref="Action"/>, and the hook throws for a
    /// component that is not a boundary.</item>
    /// <item><c>onReset</c> is handed the arguments of an invoked reset, and both key arrays of a keys reset.</item>
    /// <item><c>ShowBoundary</c> makes the nearest boundary catch the error; <c>ResetBoundary</c>, called from the
    /// fallback's content, resets that boundary and calls its <c>onReset</c>; <c>UseErrorBoundary</c> throws with
    /// no boundary above. <c>ResetBoundary</c> right after <c>ShowBoundary</c> withdraws the error, and
    /// <c>ShowBoundary</c> refuses null.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class UseErrorBoundaryResetTests
    {
        private sealed record BoundaryProps(object[] ResetKeys, int Tick);

        private static List<VelvetTaskCompletionSource<string>> s_sources;
        private static ErrorBoundaryReset s_reset;
        private static ErrorBoundaryApi s_readerApi;
        private static ErrorBoundaryApi s_fallbackApi;
        private static List<string> s_resetDetails;
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
            s_readerApi = default;
            s_fallbackApi = default;
            s_resetDetails = new List<string>();
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
            // Flushed again so that a render a layout effect scheduled is taken as well.
            mounted.FlushStateForTest();
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
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsResetIsInvokedWithArguments_Then_OnResetIsHandedThem()
        {
            // Arrange
            using var mounted = MountFailedLoad();

            // Act
            Settle(mounted, () => s_reset.Invoke("retry", 2));

            // Assert
            Assert.That(string.Join(";", s_resetDetails), Is.EqualTo("ImperativeApi|retry,2|null|null"));
        }

        [Test]
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsResetKeyChanges_Then_OnResetIsHandedBothKeyArrays()
        {
            // Arrange
            using var mounted = MountFailedLoad();

            // Act
            Settle(mounted, () => s_setResetKey.Invoke(1));

            // Assert
            Assert.That(string.Join(";", s_resetDetails), Is.EqualTo("Keys||0|1"));
        }

        [Test]
        public void Given_ABoundaryShowingItsChildren_When_ADescendantShowsAnError_Then_TheBoundaryCatchesIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            Settle(mounted, () => LatestLoad().TrySetResult("data"));
            var shown = Texts();

            // Act
            Settle(mounted, () => s_readerApi.ShowBoundary(new InvalidOperationException("shown")));

            // Assert — the value is folded in, so the case also reads the reader as showing its value before the error
            Assert.That(shown + "|" + Texts(), Is.EqualTo("loaded:data|failed:shown"));
        }

        [Test]
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsFallbackContentResetsTheBoundary_Then_TheLoadRunsAgainAndItsValueRenders()
        {
            // Arrange
            using var mounted = MountFailedLoad();

            // Act
            Settle(mounted, () => s_fallbackApi.ResetBoundary());
            Settle(mounted, () => LatestLoad().TrySetResult("data"));

            // Assert
            Assert.That(Texts(), Is.EqualTo("loaded:data"));
        }

        [Test]
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsFallbackContentResetsTheBoundary_Then_OnResetIsHandedNoArguments()
        {
            // Arrange
            using var mounted = MountFailedLoad();

            // Act
            Settle(mounted, () => s_fallbackApi.ResetBoundary());

            // Assert
            Assert.That(string.Join(";", s_resetDetails), Is.EqualTo("ImperativeApi||null|null"));
        }

        [Test]
        public void Given_NoErrorBoundaryAboveAComponent_When_ItCallsUseErrorBoundary_Then_ItThrowsInvalidOperationException()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception,
                new Regex("InvalidOperationException.*UseErrorBoundary must be called below"));

            // Act
            using var mounted = V.Mount(_root, V.Component(NoBoundaryAboveRender, key: "child"));

            // Assert — the expected log is what fails the case when nothing throws
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_ABoundaryThatCaughtAFailedLoad_When_ItsResetIsInvokedAsAnAction_Then_TheLoadRunsAgainAndItsValueRenders()
        {
            // Arrange
            using var mounted = MountFailedLoad();
            Action onClick = s_reset;

            // Act
            Settle(mounted, () => onClick());
            Settle(mounted, () => LatestLoad().TrySetResult("data"));

            // Assert
            Assert.That(Texts(), Is.EqualTo("loaded:data"));
        }

        [Test]
        public void Given_ABoundaryResetByItsKeysWhoseLoadFailsAgain_When_ItsParentRendersAgain_Then_ItKeepsItsFallbackAndLoadsNothing()
        {
            // Arrange
            using var mounted = MountFailedLoad();
            Settle(mounted, () => s_setResetKey.Invoke(1));
            Settle(mounted, () => LatestLoad().TrySetException(new InvalidOperationException("still offline")));

            // Act
            Settle(mounted, () => s_setTick.Invoke(1));

            // Assert
            Assert.That((Texts(), s_sources.Count), Is.EqualTo(("failed:still offline", 2)));
        }

        [Test]
        public void Given_ADescendantThatShowsAnError_When_ItResetsTheBoundaryInTheSameHandler_Then_TheBoundaryKeepsShowingItsChildren()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            Settle(mounted, () => LatestLoad().TrySetResult("data"));

            // Act
            Settle(mounted, () =>
            {
                s_readerApi.ShowBoundary(new InvalidOperationException("shown"));
                s_readerApi.ResetBoundary();
            });

            // Assert
            Assert.That(Texts(), Is.EqualTo("loaded:data"));
        }

        [Test]
        public void Given_ADescendantBelowABoundary_When_ItShowsANullError_Then_ItThrowsArgumentNullException()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);

            // Act
            TestDelegate show = () => s_readerApi.ShowBoundary(null);

            // Assert
            Assert.That(show, Throws.ArgumentNullException);
        }

        [Test]
        public void Given_AComponentThatIsNotAnErrorBoundary_When_ItCallsTheHook_Then_ItThrowsInvalidOperationException()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(OuterBoundaryRender, key: "outer"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo(nameof(InvalidOperationException)));
        }

        private static string Describe(ErrorBoundaryResetDetails details)
            => details.Reason + "|" + string.Join(",", details.Args) + "|" + Join(details.Prev) + "|" + Join(details.Next);

        private static string Join(IReadOnlyList<object> keys) => keys == null ? "null" : string.Join(",", keys);

        [Component(Compiler = false)]
        private static VNode ReaderRender()
        {
            s_readerApi = Hooks.UseErrorBoundary();
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
            s_reset = Hooks.UseErrorBoundaryReset(props.ResetKeys, details => s_resetDetails.Add(Describe(details)));
            Hooks.UseFallback(ex => V.Component(FallbackRender, ex.Message, key: "fallback"));
            return V.Suspense(V.Label(text: "loading"), new VNode[] { V.Component(ReaderRender, key: "reader") });
        }

        [Component(Compiler = false)]
        private static VNode FallbackRender(string message)
        {
            s_fallbackApi = Hooks.UseErrorBoundary();
            return V.Label(text: "failed:" + message);
        }

        [Component(Compiler = false)]
        private static VNode NoBoundaryAboveRender()
        {
            Hooks.UseErrorBoundary();
            return V.Label(text: "child");
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
