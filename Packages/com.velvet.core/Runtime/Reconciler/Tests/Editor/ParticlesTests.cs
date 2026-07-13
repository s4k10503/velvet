using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <c>V.Particles</c> — a hidden, framework-owned simulation host:
    /// <list type="bullet">
    /// <item>Mounting with an effect clones it into a hidden host (renderer disabled — only the
    /// simulation is consumed) and never mutates the SOURCE system; the host is destroyed on unmount,
    /// conditional removal, same-key type swaps and tree disposal, and recreated on an effect swap.</item>
    /// <item>A null effect mounts an inert element with no host; a world-space source warns (positions
    /// are read in local space).</item>
    /// <item>An invalid pixelsPerUnit fails fast at the factory.</item>
    /// </list>
    /// Host accounting reads through Resources.FindObjectsOfTypeAll, which sees hidden objects.
    /// </summary>
    internal sealed class ParticlesTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<Object> _spawned = new();
        private int _baselineSystems;

        private static ParticleSystem s_effect;
        private static ParticleSystem s_effectB;
        private static StateUpdater<bool> s_setFlag;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _baselineSystems = CountSystems();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            foreach (var obj in _spawned)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _spawned.Clear();
        }

        private static int CountSystems() => Resources.FindObjectsOfTypeAll<ParticleSystem>().Length;

        private ParticleSystem CreateEffectSource(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            var ps = go.AddComponent<ParticleSystem>();
            _baselineSystems = CountSystems();
            return ps;
        }

        private void MountAndLayout(VNode node)
        {
            _mounted = V.Mount(_host.Root, node);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void FlushAndLayout()
        {
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        #region Mount

        [Test]
        public void Given_AParticlesNode_When_Mounted_Then_ItCreatesTheDedicatedElement()
        {
            // Arrange
            var effect = CreateEffectSource("fx");

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]", name: "px"));

            // Assert
            Assert.That(_host.Root.Q<VisualElement>("px"), Is.InstanceOf<ParticlesElement>());
        }

        [Test]
        public void Given_AMountedEffect_When_Attached_Then_AHiddenHostCloneExists()
        {
            // Arrange
            var effect = CreateEffectSource("fx");

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));

            // Assert — exactly one new system beyond the source: the framework's hidden host.
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems + 1));
        }

        [Test]
        public void Given_AMountedEffect_When_Attached_Then_TheHostRendererIsDisabledAndTheSourceUntouched()
        {
            // Arrange
            var effect = CreateEffectSource("fx");
            var sourceRenderer = effect.GetComponent<ParticleSystemRenderer>();

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));

            // Assert — only the simulation is consumed: the CLONE's renderer is off (no camera may draw
            // it), while the source system's renderer keeps its own state.
            var host = FindHost(effect);
            Assume.That(host, Is.Not.Null, "Precondition: the hidden host clone exists");
            var hostRenderer = host.GetComponent<ParticleSystemRenderer>();
            Assert.That((hostRenderer.enabled, sourceRenderer.enabled), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ANullEffect_When_Mounted_Then_TheElementMountsInertWithNoHost()
        {
            // Arrange & Act
            MountAndLayout(V.Particles(null, className: "w-[128px] h-[128px]", name: "px"));

            // Assert
            Assert.That((CountSystems(), _host.Root.Q<VisualElement>("px") != null),
                Is.EqualTo((_baselineSystems, true)));
        }

        [Test]
        public void Given_AWorldSpaceSource_When_Mounted_Then_ItWarns()
        {
            // Arrange — particle positions are read in local space; a world-space source would misrender,
            // so mounting one must say so instead of drawing garbage silently. The advisory debounce is
            // keyed by source name, so each advisory fixture uses its own name.
            var effect = CreateEffectSource("fx-world-space");
            var main = effect.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            LogAssert.Expect(LogType.Warning, new Regex("world", RegexOptions.IgnoreCase));

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));

            // Assert — the host still exists (the warning is advisory, not a rejection).
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems + 1));
        }

        [Test]
        public void Given_AMountedEffect_When_Attached_Then_TheHostAlwaysSimulates()
        {
            // Arrange — the host's renderer is disabled and it sits far from every camera, so Unity's
            // automatic culling would PAUSE a looping simulation; the host must opt out.
            var effect = CreateEffectSource("fx");

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));

            // Assert
            var host = FindHost(effect);
            Assume.That(host, Is.Not.Null, "Precondition: the hidden host clone exists");
            Assert.That(host.main.cullingMode, Is.EqualTo(ParticleSystemCullingMode.AlwaysSimulate));
        }

        [Test]
        public void Given_AnInactiveSource_When_Mounted_Then_TheHostIsActivated()
        {
            // Arrange — cloning preserves activeSelf, and an inactive host never simulates; a pooled
            // prefab kept inactive until spawned must still drive a live element.
            var effect = CreateEffectSource("fx");
            effect.gameObject.SetActive(false);

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));

            // Assert
            var host = FindHost(effect);
            Assume.That(host, Is.Not.Null, "Precondition: the hidden host clone exists");
            Assert.That(host.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void Given_ACustomSpaceSource_When_Mounted_Then_ItWarns()
        {
            // Arrange — Custom simulation space has the same local-space read mismatch as World.
            var effect = CreateEffectSource("fx-custom-space");
            var main = effect.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            LogAssert.Expect(LogType.Warning, new Regex("simulation space", RegexOptions.IgnoreCase));

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));

            // Assert
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems + 1));
        }

        [Test]
        public void Given_ASourceBeyondTheDrawCap_When_Mounted_Then_ItWarns()
        {
            // Arrange — the draw path truncates at its particle cap; a denser effect must say so once
            // instead of silently thinning out compared to everywhere else the prefab is used.
            var effect = CreateEffectSource("fx-draw-cap");
            var main = effect.main;
            main.maxParticles = 5000;
            LogAssert.Expect(LogType.Warning, new Regex("2048"));

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));

            // Assert
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems + 1));
        }

        [Component]
        private static VNode DestroyedEffectToNullHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.Particles(removed ? null : s_effect, className: "w-[128px] h-[128px]");
        }

        [Test]
        public void Given_ASourceDestroyedWhileMounted_When_RepatchedToNull_Then_TheHostIsStillDestroyed()
        {
            // Arrange — the settings diff sees destroyed-vs-null as a change, but an engine-equality
            // compare inside the driver would see them as EQUAL and skip the teardown; the host (an
            // independent clone, unaffected by the source's death) must still be destroyed.
            s_effect = CreateEffectSource("fx");
            MountAndLayout(V.Component(DestroyedEffectToNullHost, key: "root"));
            Assume.That(CountSystems(), Is.EqualTo(_baselineSystems + 1), "Precondition: the host exists");
            Object.DestroyImmediate(s_effect.gameObject);

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — only the destroyed source is gone from the count's perspective; no host remains.
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems - 1));
        }

        [Test]
        public void Given_AnInvalidPixelsPerUnit_When_TheFactoryRuns_Then_ItThrows()
        {
            // Arrange
            var effect = CreateEffectSource("fx");

            // Act & Assert
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => V.Particles(effect, pixelsPerUnit: 0f));
        }

        #endregion

        #region Updates and teardown

        [Component]
        private static VNode SwappingHost()
        {
            var (useB, setUseB) = Hooks.UseState(false);
            s_setFlag = setUseB;
            return V.Particles(useB ? s_effectB : s_effect, className: "w-[128px] h-[128px]");
        }

        [Test]
        public void Given_AnEffectSwap_When_Repatched_Then_TheHostIsRecreatedFromTheNewEffect()
        {
            // Arrange
            s_effect = CreateEffectSource("fxA");
            s_effectB = CreateEffectSource("fxB");
            MountAndLayout(V.Component(SwappingHost, key: "root"));
            var oldHost = FindHost(s_effect);
            Assume.That(oldHost, Is.Not.Null, "Precondition: the first effect's host exists");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — still exactly one host, and the old clone is gone.
            Assert.That((CountSystems(), oldHost == null), Is.EqualTo((_baselineSystems + 1, true)));
        }

        [Component]
        private static VNode EffectToNullHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.Particles(removed ? null : s_effect, className: "w-[128px] h-[128px]");
        }

        [Test]
        public void Given_AnEffectRemoved_When_RepatchedToNull_Then_TheHostIsDestroyed()
        {
            // Arrange
            s_effect = CreateEffectSource("fx");
            MountAndLayout(V.Component(EffectToNullHost, key: "root"));
            Assume.That(CountSystems(), Is.EqualTo(_baselineSystems + 1), "Precondition: the host exists");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems));
        }

        [Test]
        public void Given_AnUnmount_When_TheTreeDisposes_Then_TheHostIsDestroyed()
        {
            // Arrange
            var effect = CreateEffectSource("fx");
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));
            Assume.That(CountSystems(), Is.EqualTo(_baselineSystems + 1), "Precondition: the host exists");

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems));
        }

        [Component]
        private static VNode ConditionalHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.Div(children: new VNode[]
            {
                removed ? null : V.Particles(s_effect, key: "px", className: "w-[128px] h-[128px]"),
            });
        }

        [Test]
        public void Given_AConditionalRemoval_When_TheParticlesLeaveTheTree_Then_TheHostIsDestroyed()
        {
            // Arrange
            s_effect = CreateEffectSource("fx");
            MountAndLayout(V.Component(ConditionalHost, key: "root"));
            Assume.That(CountSystems(), Is.EqualTo(_baselineSystems + 1), "Precondition: the host exists");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems));
        }

        [Component]
        private static VNode TypeSwapHost()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            s_setFlag = setSwapped;
            return swapped
                ? V.Div(key: "x", className: "w-[128px] h-[128px]")
                : V.Particles(s_effect, key: "x", className: "w-[128px] h-[128px]");
        }

        [Test]
        public void Given_ASameKeyTypeSwap_When_TheParticlesBecomeADiv_Then_TheHostIsDestroyed()
        {
            // Arrange
            s_effect = CreateEffectSource("fx");
            MountAndLayout(V.Component(TypeSwapHost, key: "root"));
            Assume.That(CountSystems(), Is.EqualTo(_baselineSystems + 1), "Precondition: the host exists");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems));
        }

        #endregion

        #region Editor simulation and advisories

        [Test]
        public void Given_AMountedEffectOutsidePlayMode_When_TheRepaintTickFires_Then_TheSimulationAdvances()
        {
            // Arrange — outside Play Mode the engine never steps a hidden host's clock on its own, so
            // the repaint tick must advance the simulation itself or an editor-context panel repaints
            // one frozen (typically empty) frame forever.
            var effect = CreateEffectSource("fx-edit-sim");
            var emission = effect.emission;
            emission.rateOverTime = 1000f;
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));
            var host = FindHost(effect);
            Assume.That(host, Is.Not.Null, "Precondition: the hidden host clone exists");

            // Act — pump the panel's scheduler with real time between firings: each firing advances the
            // simulation by its elapsed delta.
            for (var i = 0; i < 6; i++)
            {
                System.Threading.Thread.Sleep(5);
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }

            // Assert — emission produced live particles (~30 ms at 1000/s).
            Assert.That(host.particleCount, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AFinishedRootWithALiveChildSystem_When_TheTickObservesIt_Then_TheTickParks()
        {
            // Arrange — the draw samples only the ROOT's particles, so a longer-lived child system must
            // not keep the repaint tick dirtying the element after the drawn output is already empty.
            var effect = CreateEffectSource("fx-park-root");
            var rootMain = effect.main;
            rootMain.loop = false;
            rootMain.duration = 0.02f;
            rootMain.startLifetime = 0.01f;
            var childGo = new GameObject("fx-park-child");
            childGo.transform.SetParent(effect.transform);
            var childMain = childGo.AddComponent<ParticleSystem>().main;
            childMain.loop = true;
            MountAndLayout(V.Particles(effect, name: "px-park", className: "w-[128px] h-[128px]"));
            var element = _host.Root.Q<VisualElement>("px-park");
            Assume.That(element, Is.Not.Null, "Precondition: the particles element mounted");
            var binding = _mounted.Root.Reconciler.Context.ParticlesBindings[element];
            Assume.That(binding.Host != null && binding.Host.IsAlive(false), Is.True,
                "Precondition: the played root reads alive before any advance");

            // Act — advance well past the root's whole lifetime, then let the tick observe the corpse.
            for (var i = 0; i < 12; i++)
            {
                System.Threading.Thread.Sleep(5);
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }

            // Assert — the tick parked itself even though the looping child system is still alive.
            Assert.That(binding.RepaintTick, Is.Null);
        }

        [Test]
        public void Given_AnEffectSwapOnOneElement_When_BothSourcesAreMisconfigured_Then_TheAdvisoryFiresOnce()
        {
            // Arrange — the advisory is per mounted element: an unstable reference that rebuilds its
            // source every render (fresh instance, any name) must not repeat the advice per rebuild.
            s_effect = CreateEffectSource("fx-adv-a");
            var mainA = s_effect.main;
            mainA.simulationSpace = ParticleSystemSimulationSpace.World;
            s_effectB = CreateEffectSource("fx-adv-b");
            var mainB = s_effectB.main;
            mainB.simulationSpace = ParticleSystemSimulationSpace.World;
            LogAssert.Expect(LogType.Warning, new Regex("simulation space", RegexOptions.IgnoreCase));
            MountAndLayout(V.Component(SwappingHost, key: "root"));

            // Act — swap to a different (differently named) but equally misconfigured source.
            s_setFlag.Invoke(true);
            FlushAndLayout();
            LogAssert.NoUnexpectedReceived();

            // Assert — still exactly one live host after the swap.
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems + 1));
        }

        [Test]
        public void Given_TwoElementsWithSameNamedSources_When_BothMisconfigured_Then_BothAdvisoriesFire()
        {
            // Arrange — two DIFFERENT effects that merely share a name are independent problems;
            // each mounted element gets its own advisory.
            var first = CreateEffectSource("fx-shared-name");
            var m1 = first.main;
            m1.simulationSpace = ParticleSystemSimulationSpace.World;
            var second = CreateEffectSource("fx-shared-name");
            var m2 = second.main;
            m2.simulationSpace = ParticleSystemSimulationSpace.World;
            LogAssert.Expect(LogType.Warning, new Regex("simulation space", RegexOptions.IgnoreCase));
            LogAssert.Expect(LogType.Warning, new Regex("simulation space", RegexOptions.IgnoreCase));

            // Act
            MountAndLayout(V.Div(children: new VNode[]
            {
                V.Particles(first, className: "w-[64px] h-[64px]"),
                V.Particles(second, className: "w-[64px] h-[64px]"),
            }));

            // Assert — both hosts exist (the two expectations pin both advisories firing).
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems + 2));
        }

        [Test]
        public void Given_ADirectlyConstructedSettings_When_PixelsPerUnitIsInvalid_Then_ItThrows()
        {
            // Arrange — the factory's fail-fast guard must hold for every construction path (fixtures
            // and wrapper hosts build the settings record directly).
            // Act & Assert
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ParticlesSettings(null, PlayTrigger.Mount, float.NaN));
        }

        #endregion

        // The hidden host is the one live ParticleSystem that is neither the tracked source objects nor
        // a prefab asset — identified by exclusion so the production code needs no test-facing handle.
        private ParticleSystem FindHost(ParticleSystem source)
        {
            foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
            {
                if (ps != null && ps != source && ps.gameObject.scene.IsValid() == false)
                {
                    continue;
                }
                if (ps != source && !_spawned.Contains(ps.gameObject))
                {
                    return ps;
                }
            }
            return null;
        }
    }
}
