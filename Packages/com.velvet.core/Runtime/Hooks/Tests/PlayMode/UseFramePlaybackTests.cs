using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the UseFrame hook's runtime contract on a real ticking panel: the callback runs once
    /// per frame with a positive elapsed-seconds delta while the component stays mounted, a re-render
    /// swaps in the latest closure without re-subscribing, and an unmount stops the ticking. Per-frame
    /// data flows entirely outside component state — no render is triggered by the callback itself.
    /// </summary>
    internal sealed class UseFramePlaybackTests
    {
        private GameObject _docGo;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private int _savedTargetFrameRate;

        private static int s_calls;
        private static float s_minDt;
        private static float s_maxDt;
        private static int s_observedValue;
        private static StateUpdater<int> s_setValue;
        private static StateUpdater<bool> s_setRemoved;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _savedTargetFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 120;
            s_calls = 0;
            s_minDt = float.MaxValue;
            s_maxDt = 0f;
            s_observedValue = -1;
            s_fallbackShown = false;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Application.targetFrameRate = _savedTargetFrameRate;
            _mounted?.Dispose();
            _mounted = null;
            if (_docGo != null) Object.Destroy(_docGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        private VisualElement CreatePanelRoot()
        {
            _docGo = new GameObject("UseFramePanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            return doc.rootVisualElement;
        }

        private static IEnumerator WaitRealtime(double seconds)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }
        }

        [Component]
        private static VNode CountingHost()
        {
            Hooks.UseFrame(dt =>
            {
                s_calls++;
                s_minDt = Mathf.Min(s_minDt, dt);
                s_maxDt = Mathf.Max(s_maxDt, dt);
            });
            return V.Div(className: "w-[10px] h-[10px]");
        }

        [UnityTest]
        public IEnumerator Given_AUseFrameComponent_When_FramesAdvance_Then_TheCallbackTicksWithPositiveDeltas()
        {
            // Arrange
            var root = CreatePanelRoot();
            yield return null;

            // Act
            _mounted = V.Mount(root, V.Component(CountingHost, key: "root"));
            yield return WaitRealtime(0.5);

            // Assert — many ticks, every delta positive and frame-sized (not wall-clock accumulations).
            Assert.That((s_calls > 5, s_minDt > 0f, s_maxDt < 0.5f), Is.EqualTo((true, true, true)),
                $"calls={s_calls} minDt={s_minDt} maxDt={s_maxDt}");
        }

        [Component]
        private static VNode ClosureHost()
        {
            var (value, setValue) = Hooks.UseState(1);
            s_setValue = setValue;
            Hooks.UseFrame(_ => s_observedValue = value);
            return V.Div(className: "w-[10px] h-[10px]");
        }

        [UnityTest]
        public IEnumerator Given_ARerender_When_TheClosureChanges_Then_TheTickInvokesTheLatestClosure()
        {
            // Arrange
            var root = CreatePanelRoot();
            yield return null;
            _mounted = V.Mount(root, V.Component(ClosureHost, key: "root"));
            yield return WaitRealtime(0.3);
            Assume.That(s_observedValue, Is.EqualTo(1), "Precondition: the first render's closure ticks");

            // Act — a re-render must swap the invoked closure (no stale capture), without re-subscribing.
            s_setValue.Invoke(42);
            yield return WaitRealtime(0.3);

            // Assert
            Assert.That(s_observedValue, Is.EqualTo(42));
        }

        [Component]
        private static VNode RemovableHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setRemoved = setRemoved;
            return V.Div(children: new VNode[]
            {
                removed ? null : V.Component(CountingHost, key: "inner"),
            });
        }

        [Component]
        private static VNode ReorderFrameHost()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            s_setRemoved = setSwapped;
            var counting = V.Component(CountingHost, key: "cnt");
            var spacer = V.Div(key: "sp", className: "w-[1px] h-[1px]");
            return V.Div(className: "flex-col", children: swapped
                ? new VNode[] { spacer, counting }
                : new VNode[] { counting, spacer });
        }

        [UnityTest]
        public IEnumerator Given_AKeyedReorder_When_TheHostMoves_Then_TheCallbackKeepsTicking()
        {
            // Arrange — a keyed reorder re-inserts the host element. A recurring tick survives that
            // detach/re-attach on its own (UI Toolkit pauses and reschedules it), but this pins the
            // OBSERVABLE contract directly: the frame driver must keep ticking across the move.
            var root = CreatePanelRoot();
            yield return null;
            _mounted = V.Mount(root, V.Component(ReorderFrameHost, key: "root"));
            yield return WaitRealtime(0.3);
            Assume.That(s_calls, Is.GreaterThan(0), "Precondition: the hook ticked before the reorder");

            // Act
            s_setRemoved.Invoke(true);
            yield return null;
            yield return null;
            var callsAfterMove = s_calls;
            yield return WaitRealtime(0.3);

            // Assert
            Assert.That(s_calls, Is.GreaterThan(callsAfterMove));
        }

        [UnityTest]
        public IEnumerator Given_AnUnmount_When_FramesAdvance_Then_TheCallbackStopsFiring()
        {
            // Arrange
            var root = CreatePanelRoot();
            yield return null;
            _mounted = V.Mount(root, V.Component(RemovableHost, key: "root"));
            yield return WaitRealtime(0.3);
            Assume.That(s_calls, Is.GreaterThan(0), "Precondition: the hook ticked while mounted");

            // Act
            s_setRemoved.Invoke(true);
            yield return null;
            yield return null;
            var callsAtUnmount = s_calls;
            yield return WaitRealtime(0.3);

            // Assert
            Assert.That(s_calls, Is.EqualTo(callsAtUnmount));
        }

        private static bool s_fallbackShown;

        [Component]
        private static VNode ThrowingFrameHost()
        {
            Hooks.UseFrame(_ => throw new System.InvalidOperationException("frame boom"));
            return V.Div(className: "w-[10px] h-[10px]");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode FrameBoundary()
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Div(name: "frame-fallback");
            });
            return V.Component(ThrowingFrameHost, key: "thrower");
        }

        [UnityTest]
        public IEnumerator Given_AThrowingFrameCallback_When_ABoundaryWraps_Then_TheFallbackRenders()
        {
            // Arrange
            var root = CreatePanelRoot();
            yield return null;

            // Act
            _mounted = V.Mount(root, V.Component(FrameBoundary, key: "root"));
            yield return WaitRealtime(0.5);

            // Assert — a frame-callback exception routes to the nearest error boundary the way effect
            // exceptions do, instead of escaping into the panel's scheduler update.
            Assert.That(s_fallbackShown, Is.True);
        }
    }
}
