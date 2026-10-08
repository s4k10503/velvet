using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <c>V.Particles</c> — a hidden, framework-owned simulation host:
    /// <list type="bullet">
    /// <item>Mounting with an effect clones it into a hidden host (renderer disabled — only the
    /// simulation is consumed) and never mutates the SOURCE system; the host is destroyed on unmount,
    /// conditional removal, same-key type swaps and tree disposal, recreated on an effect swap, and kept
    /// through a pixelsPerUnit change on the same effect.</item>
    /// <item>A null effect mounts an inert element with no host. Every system under the effect is
    /// hosted: each child's renderer is disabled and each always simulates.</item>
    /// <item>An invalid pixelsPerUnit fails fast at the factory.</item>
    /// <item>The filter bounds-spacer (<see cref="SilhouetteBoundsSpacer"/>) also applies at the
    /// Particles reconcile boundary: a filter would clip the particle quads that draw beyond the host
    /// rect, so a transparent last-child spacer widens the element's boundingBox to keep them. The
    /// spacer appears with a filter — base, variant-carried, or animate-hue — and vanishes when the
    /// filter goes, driven by the class list rather than the effect.</item>
    /// </list>
    /// Host accounting reads through Resources.FindObjectsOfTypeAll, which sees hidden objects. GWT,
    /// one assert each.
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
        private static StateUpdater<string> s_setClass;

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
            s_effect = null;
            s_setClass = default;
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
        private static VNode RescalingHost()
        {
            var (rescaled, setRescaled) = Hooks.UseState(false);
            s_setFlag = setRescaled;
            return V.Particles(s_effect, className: "w-[128px] h-[128px]", pixelsPerUnit: rescaled ? 50f : 100f);
        }

        // GREEN_ON_BASE(characterization): the base keeps the host for an unchanged source, and the
        // source id changing type must not change that. Deleting `binding.SourceId = source.GetEntityId();`
        // makes every settings change rebuild the host; measured over both Particles fixtures, this case
        // alone then fails.
        [Test]
        public void Given_TheSameEffect_When_ASettingsChangeRepatches_Then_TheHostIsKept()
        {
            // Arrange — a pixelsPerUnit change reaches the driver with the source unchanged, so whether the
            // host is rebuilt rests on the id recorded for that source alone.
            s_effect = CreateEffectSource("fx");
            MountAndLayout(V.Component(RescalingHost, key: "root"));
            var host = FindHost(s_effect);

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — a rebuild destroys the first clone, and a mount that made none finds none.
            Assert.That((host != null, FindHost(s_effect) == host), Is.EqualTo((true, true)));
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

        // Fake panel clock for the editor-simulation specs: the repaint tick fires on a 16 ms
        // interval read exclusively through the panel's time function, so fixed fake steps make the
        // firing count and every simulated delta deterministic under any machine load.
        private long _fakeMs;

        [Test]
        public void Given_AMountedEffectOutsidePlayMode_When_TheRepaintTickFires_Then_TheSimulationAdvances()
        {
            // Arrange — outside Play Mode the engine never steps a hidden host's clock on its own, so
            // the repaint tick must advance the simulation itself or an editor-context panel repaints
            // one frozen (typically empty) frame forever.
            _fakeMs = 1000;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _fakeMs / 1000.0);
            var effect = CreateEffectSource("fx-edit-sim");
            var emission = effect.emission;
            emission.rateOverTime = 1000f;
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));
            var host = FindHost(effect);
            Assume.That(host, Is.Not.Null, "Precondition: the hidden host clone exists");

            // Act — each 20 fake-ms step crosses the tick interval, so every drive fires once and
            // advances the simulation by exactly that delta.
            for (var i = 0; i < 6; i++)
            {
                _fakeMs += 20;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }

            // Assert — emission produced live particles (~100 fake-ms at 1000/s).
            Assert.That(host.particleCount, Is.GreaterThan(0));
        }

        // A non-looping root whose emission is enabled at rate zero and whose 125ms timeline the first
        // firing below completes, over one child system.
        private ParticlesBinding MountFinishingRootOver(ParticleSystem child, string name, System.Action<ParticleSystem> configureRoot = null)
        {
            _fakeMs = 1000;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _fakeMs / 1000.0);
            var effect = CreateEffectSource(name);
            var rootMain = effect.main;
            rootMain.loop = false;
            rootMain.duration = 0.125f;
            var rootEmission = effect.emission;
            rootEmission.rateOverTime = 0f;
            child.transform.SetParent(effect.transform);
            configureRoot?.Invoke(effect);
            MountAndLayout(V.Particles(effect, name: "px-park", className: "w-[128px] h-[128px]"));
            return _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>("px-park")];
        }

        // Each 125 fake-ms step fires the tick once and advances the simulation by exactly that step,
        // a value binary floating point holds exactly, so the clock the tick keeps lands on 0.125
        // after the first firing rather than beside it.
        private void AdvanceTicks(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                _fakeMs += 125;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
        }

        [Test]
        public void Given_AFinishedRootWithALiveChildSystem_When_TheTickObservesIt_Then_TheTickKeepsRunning()
        {
            // Arrange — the draw samples the child systems too, so a looping child still emitting after
            // the root finished is output the tick has to keep repainting.
            var child = new GameObject("fx-live-child").AddComponent<ParticleSystem>();
            var childMain = child.main;
            childMain.loop = true;
            var childEmission = child.emission;
            childEmission.rateOverTime = 1000f;
            var binding = MountFinishingRootOver(child, "fx-park-root");

            // Act
            AdvanceTicks(6);

            // Assert
            Assert.That(binding.RepaintTick, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): a finished root over a child that never emits parks the tick, as it did.
        [Test]
        public void Given_AFinishedRootWithAnEmptyChildSystem_When_TheTickObservesIt_Then_TheTickParks()
        {
            // Arrange — the child loops but emits nothing, so once the root finishes no system holds a
            // particle and nothing is left to draw.
            var child = new GameObject("fx-empty-child").AddComponent<ParticleSystem>();
            var emission = child.emission;
            emission.enabled = false;
            var binding = MountFinishingRootOver(child, "fx-park-empty");

            // Act — two firings: the second is the first to see the root's 125ms timeline over.
            AdvanceTicks(2);

            // Assert
            Assert.That(binding.RepaintTick, Is.Null);
        }

        [Test]
        public void Given_AFinishedRootWithADelayedChild_When_TheTickObservesIt_Then_TheTickKeepsRunning()
        {
            // Arrange — the child starts emitting 200ms after play, past both firings below; until
            // then it holds no particle, yet it has a burst still due.
            var child = new GameObject("fx-delayed-child").AddComponent<ParticleSystem>();
            var childMain = child.main;
            childMain.loop = false;
            childMain.startDelay = 0.2f;
            childMain.duration = 0.1f;
            var childEmission = child.emission;
            childEmission.rateOverTime = 1000f;
            var binding = MountFinishingRootOver(child, "fx-park-delayed");

            // Act — the second firing sees 125ms: the root's timeline over, the child's delay not.
            AdvanceTicks(2);

            // Assert
            Assert.That(binding.RepaintTick, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): a sub-emitter nothing triggers keeps no tick running once the root finishes.
        [Test]
        public void Given_AFinishedRootWithAnUntriggeredSubEmitter_When_TheTickObservesIt_Then_TheTickParks()
        {
            // Arrange — the child loops with its emission enabled, but as the root's birth sub-emitter it
            // emits only per root particle, and the root emits none.
            var child = new GameObject("fx-sub-emitter").AddComponent<ParticleSystem>();
            var binding = MountFinishingRootOver(child, "fx-park-sub", root =>
            {
                var rootEmission = root.emission;
                rootEmission.enabled = false;
                var subEmitters = root.subEmitters;
                subEmitters.enabled = true;
                subEmitters.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
            });

            // Act
            AdvanceTicks(6);

            // Assert
            Assert.That(binding.RepaintTick, Is.Null);
        }

        [Test]
        public void Given_ARootWhoseSubEmitterIsNotItsChild_When_Mounted_Then_TheHostRootTriggersACloneUnderTheHost()
        {
            // Arrange — the sub-emitter is a separate object, so Instantiate leaves the host's
            // reference pointing at it.
            var external = new GameObject("fx-external-sub").AddComponent<ParticleSystem>();
            _spawned.Add(external.gameObject);
            var effect = CreateEffectSource("fx-external-root");
            var subEmitters = effect.subEmitters;
            subEmitters.enabled = true;
            subEmitters.AddSubEmitter(external, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);

            // Act
            MountAndLayout(V.Particles(effect, name: "px-external-sub", className: "w-[128px] h-[128px]"));
            var host = _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>("px-external-sub")].Host;

            // Assert
            Assert.That(host.subEmitters.GetSubEmitterSystem(0).transform.IsChildOf(host.transform), Is.True);
        }

        private ParticleSystem CreateExternalSystem(string name)
        {
            var system = new GameObject(name).AddComponent<ParticleSystem>();
            _spawned.Add(system.gameObject);
            return system;
        }

        private static void AddBirthSubEmitter(ParticleSystem parent, ParticleSystem subEmitter)
        {
            var module = parent.subEmitters;
            module.enabled = true;
            module.AddSubEmitter(subEmitter, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
        }

        private ParticlesBinding MountEffect(ParticleSystem effect, string name)
        {
            MountAndLayout(V.Particles(effect, name: name, className: "w-[128px] h-[128px]"));
            return _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>(name)];
        }

        [Test]
        public void Given_TwoSystemsNamingOneExternalSubEmitter_When_Mounted_Then_TheHostHoldsOneCloneOfIt()
        {
            // Arrange
            var external = CreateExternalSystem("fx-dedupe-sub");
            var effect = CreateEffectSource("fx-dedupe-root");
            var child = CreateExternalSystem("fx-dedupe-child");
            child.transform.SetParent(effect.transform);
            AddBirthSubEmitter(effect, external);
            AddBirthSubEmitter(child, external);

            // Act
            var host = MountEffect(effect, "px-dedupe").Host;

            // Assert — the root, its child and the single clone.
            Assert.That(host.GetComponentsInChildren<ParticleSystem>().Length, Is.EqualTo(3));
        }

        [Test]
        public void Given_AnExternalSubEmitterWithItsOwnExternalSubEmitter_When_Mounted_Then_TheSecondIsClonedUnderTheHost()
        {
            // Arrange
            var first = CreateExternalSystem("fx-chain-first");
            var second = CreateExternalSystem("fx-chain-second");
            AddBirthSubEmitter(first, second);
            var effect = CreateEffectSource("fx-chain-root");
            AddBirthSubEmitter(effect, first);

            // Act
            var host = MountEffect(effect, "px-chain").Host;
            var firstClone = host.subEmitters.GetSubEmitterSystem(0);

            // Assert
            Assert.That(firstClone.subEmitters.GetSubEmitterSystem(0).transform.IsChildOf(host.transform), Is.True);
        }

        [Test]
        public void Given_AnInactiveExternalSubEmitter_When_Mounted_Then_TheCloneIsActive()
        {
            // Arrange
            var external = CreateExternalSystem("fx-inactive-sub");
            external.gameObject.SetActive(false);
            var effect = CreateEffectSource("fx-inactive-root");
            AddBirthSubEmitter(effect, external);

            // Act
            var host = MountEffect(effect, "px-inactive").Host;

            // Assert
            Assert.That(host.subEmitters.GetSubEmitterSystem(0).gameObject.activeSelf, Is.True);
        }

        [Test]
        public void Given_AnExternalSubEmitter_When_Mounted_Then_TheCloneIsHiddenLikeTheHost()
        {
            // Arrange
            var external = CreateExternalSystem("fx-hidden-sub");
            var effect = CreateEffectSource("fx-hidden-root");
            AddBirthSubEmitter(effect, external);

            // Act
            var host = MountEffect(effect, "px-hidden").Host;

            // Assert
            Assert.That(
                host.subEmitters.GetSubEmitterSystem(0).gameObject.hideFlags,
                Is.EqualTo(HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor));
        }

        // GREEN_ON_BASE(characterization): a sub-emitter inside the effect needs no clone, as before.
        [Test]
        public void Given_ASubEmitterThatIsTheEffectsChild_When_Mounted_Then_NoExtraCloneIsMade()
        {
            // Arrange
            var effect = CreateEffectSource("fx-inside-root");
            var child = CreateExternalSystem("fx-inside-child");
            child.transform.SetParent(effect.transform);
            AddBirthSubEmitter(effect, child);

            // Act
            var host = MountEffect(effect, "px-inside").Host;

            // Assert — the root and its one child.
            Assert.That(host.GetComponentsInChildren<ParticleSystem>().Length, Is.EqualTo(2));
        }

        [Test]
        public void Given_AnExternalSubEmitter_When_Mounted_Then_TheCloneIsAmongTheDrawnSystems()
        {
            // Arrange
            var external = CreateExternalSystem("fx-drawn-sub");
            var effect = CreateEffectSource("fx-drawn-root");
            AddBirthSubEmitter(effect, external);

            // Act
            var binding = MountEffect(effect, "px-drawn");

            // Assert
            Assert.That(
                binding.Systems.Any(hosted => hosted.System == binding.Host.subEmitters.GetSubEmitterSystem(0)),
                Is.True);
        }

        // GREEN_ON_BASE(characterization): a finished burst whose particles still live keeps the tick running, as before.
        [Test]
        public void Given_AFinishedBurstWithLiveParticles_When_TheTickObservesIt_Then_TheTickKeepsRunning()
        {
            // Arrange — a 50ms burst of one-second particles: its emission is over after the first
            // firing, and every particle it made is still alive for the rest.
            _fakeMs = 1000;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _fakeMs / 1000.0);
            var effect = CreateEffectSource("fx-burst-live");
            var main = effect.main;
            main.loop = false;
            main.duration = 0.05f;
            main.startLifetime = 1f;
            var emission = effect.emission;
            emission.rateOverTime = 1000f;
            MountAndLayout(V.Particles(effect, name: "px-burst-live", className: "w-[128px] h-[128px]"));
            var binding = _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>("px-burst-live")];

            // Act
            AdvanceTicks(3);

            // Assert
            Assert.That(binding.RepaintTick, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): Play on a host still emitting outside Play Mode keeps its particles, as before.
        [Test]
        public void Given_ALiveHostOutsidePlayMode_When_PlayIsCalledOnTheElement_Then_ItsParticlesAreKept()
        {
            // Arrange
            _fakeMs = 1000;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _fakeMs / 1000.0);
            var effect = CreateEffectSource("fx-live-play");
            var emission = effect.emission;
            emission.rateOverTime = 1000f;
            MountAndLayout(V.Particles(effect, name: "px-live-play", className: "w-[128px] h-[128px]"));
            var element = _host.Root.Q<ParticlesElement>("px-live-play");
            var host = _mounted.Root.Reconciler.Context.ParticlesBindings[element].Host;
            AdvanceTicks(3);
            var before = host.particleCount;

            // Act — a restart would clear the particles the three firings emitted.
            element.Play();

            // Assert
            Assert.That((before > 0, host.particleCount), Is.EqualTo((true, before)));
        }

        // GREEN_ON_BASE(characterization): replaying a finished burst outside Play Mode starts it over, as before.
        [Test]
        public void Given_AFinishedBurstOutsidePlayMode_When_PlayIsCalledOnTheElement_Then_ItEmitsAgain()
        {
            // Arrange — a 50ms burst of 200ms particles: finished and emptied well inside 750ms, and
            // replayed particles still alive 125ms after the replay.
            _fakeMs = 1000;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _fakeMs / 1000.0);
            var effect = CreateEffectSource("fx-replay");
            var main = effect.main;
            main.loop = false;
            main.duration = 0.05f;
            main.startLifetime = 0.2f;
            var emission = effect.emission;
            emission.rateOverTime = 1000f;
            MountAndLayout(V.Particles(effect, name: "px-replay", className: "w-[128px] h-[128px]"));
            var element = _host.Root.Q<ParticlesElement>("px-replay");
            var binding = _mounted.Root.Reconciler.Context.ParticlesBindings[element];
            AdvanceTicks(6);
            Assume.That(binding.RepaintTick, Is.Null, "Precondition: the finished burst parked the tick");

            // Act
            element.Play();
            AdvanceTicks(1);

            // Assert
            Assert.That(binding.Host.particleCount, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): a Manual host holds no particle and plays nothing, so the tick parks.
        [Test]
        public void Given_AManualHostOutsidePlayMode_When_TheTickFires_Then_TheTickParks()
        {
            // Arrange
            _fakeMs = 1000;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _fakeMs / 1000.0);
            var effect = CreateEffectSource("fx-manual-park");
            MountAndLayout(V.Particles(effect, name: "px-manual", className: "w-[128px] h-[128px]", playOn: PlayTrigger.Manual));
            var binding = _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>("px-manual")];

            // Act
            _fakeMs += 20;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(binding.RepaintTick, Is.Null);
        }

        // The hosted clone of the source's single child system, reached through the element's binding.
        private ParticleSystem MountAndFindHostedChild(ParticleSystem child)
        {
            var effect = CreateEffectSource("fx-parent");
            child.transform.SetParent(effect.transform);
            MountAndLayout(V.Particles(effect, name: "px-child", className: "w-[128px] h-[128px]"));
            var host = _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>("px-child")].Host;
            return System.Array.Find(host.GetComponentsInChildren<ParticleSystem>(), ps => ps != host);
        }

        [Test]
        public void Given_AChildSystem_When_Mounted_Then_ItsHostedRendererIsDisabled()
        {
            // Arrange — the element draws the child, so no camera may draw the hidden clone as well.
            var child = new GameObject("fx-child").AddComponent<ParticleSystem>();

            // Act
            var hostedChild = MountAndFindHostedChild(child);

            // Assert — the source child's renderer keeps its own state.
            Assert.That((hostedChild.GetComponent<ParticleSystemRenderer>().enabled, child.GetComponent<ParticleSystemRenderer>().enabled),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AChildSystem_When_Mounted_Then_ItAlwaysSimulates()
        {
            // Arrange — the same automatic culling that would pause the root would pause a child.
            var child = new GameObject("fx-child").AddComponent<ParticleSystem>();

            // Act
            var hostedChild = MountAndFindHostedChild(child);

            // Assert
            Assert.That(hostedChild.main.cullingMode, Is.EqualTo(ParticleSystemCullingMode.AlwaysSimulate));
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

        #region Filter bounds-spacer

        private const string Filtered = "w-[128px] h-[128px] hue-rotate-90";
        private const string Unfiltered = "w-[128px] h-[128px]";

        private ParticleSystem CreateEffect()
        {
            var go = new GameObject("fx-source");
            _spawned.Add(go);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        // Manual trigger keeps the host stopped: the spacer is gated on the filter, not on any live particles.
        [Component]
        private static VNode Host()
        {
            var (cls, setClass) = Hooks.UseState(Filtered);
            s_setClass = setClass;
            return V.Particles(s_effect, className: cls, name: "fx", playOn: PlayTrigger.Manual);
        }

        private void MountAndLayout()
        {
            _mounted = V.Mount(_host.Root, V.Component(Host, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void MountUnfilteredAndLayout()
        {
            _mounted = V.Mount(_host.Root,
                V.Particles(s_effect, className: Unfiltered, name: "fx", playOn: PlayTrigger.Manual));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void MountWithClassAndLayout(string className)
        {
            _mounted = V.Mount(_host.Root,
                V.Particles(s_effect, className: className, name: "fx", playOn: PlayTrigger.Manual));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void SetClass(string cls)
        {
            s_setClass.Invoke(cls);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private VisualElement Particle => _host.Root.Q<VisualElement>("fx");

        private int SpacerCount() => Enumerable.Range(0, Particle.childCount)
            .Count(i => SilhouetteBoundsSpacer.IsSpacer(Particle[i]));

        [Test]
        public void Given_AFilteredParticles_When_Mounted_Then_OneSpacerIsAdded()
        {
            s_effect = CreateEffect();
            MountAndLayout();

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_AnUnfilteredParticles_When_Mounted_Then_NoSpacerIsAdded()
        {
            s_effect = CreateEffect();
            MountUnfilteredAndLayout();

            Assert.That(SpacerCount(), Is.EqualTo(0));
        }

        [Test]
        public void Given_AFilteredParticles_When_Mounted_Then_TheSpacerIsTheHostsOnlyChild()
        {
            // Particles carry no rendered children, so the spacer must be the single child — and recognized
            // as a spacer, not a rendered one.
            s_effect = CreateEffect();
            MountAndLayout();

            Assert.That(Particle.childCount == 1 && SilhouetteBoundsSpacer.IsSpacer(Particle[0]), Is.True);
        }

        [Test]
        public void Given_TheFilterRemoved_When_Repatched_Then_TheSpacerIsGone()
        {
            s_effect = CreateEffect();
            MountAndLayout();
            Assume.That(SpacerCount(), Is.EqualTo(1), "Precondition: the spacer was present under the filter");
            SetClass(Unfiltered);

            Assert.That(SpacerCount(), Is.EqualTo(0));
        }

        [Test]
        public void Given_TheFilterAdded_When_Repatched_Then_TheSpacerAppears()
        {
            s_effect = CreateEffect();
            _mounted = V.Mount(_host.Root, V.Component(HostStartingUnfiltered, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            Assume.That(SpacerCount(), Is.EqualTo(0), "Precondition: no spacer without a filter");
            SetClass(Filtered);

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Component]
        private static VNode HostStartingUnfiltered()
        {
            var (cls, setClass) = Hooks.UseState(Unfiltered);
            s_setClass = setClass;
            return V.Particles(s_effect, className: cls, name: "fx", playOn: PlayTrigger.Manual);
        }

        [Test]
        public void Given_AReRenderThatKeepsTheFilter_When_Repatched_Then_TheSpacerSurvivesAsTheOnlyChild()
        {
            // Particles reconcile against an empty child list every patch; that pass must not drop, duplicate,
            // or orphan the lone spacer while the filter holds (a raw-childCount site would index into it).
            s_effect = CreateEffect();
            MountAndLayout();
            Assume.That(SpacerCount(), Is.EqualTo(1), "Precondition: one spacer under the initial filter");
            SetClass("w-[128px] h-[128px] blur-sm");

            Assert.That(Particle.childCount == 1 && SilhouetteBoundsSpacer.IsSpacer(Particle[0]), Is.True);
        }

        [Test]
        public void Given_AVariantOnlyFilter_When_Mounted_Then_ASpacerIsAdded()
        {
            // A filter carried only by a state variant applies at state time, outside the reconcile — so the
            // spacer must exist whenever a filter COULD apply, not only while the state is active.
            s_effect = CreateEffect();
            MountWithClassAndLayout("w-[128px] h-[128px] hover:blur-sm");

            Assert.That(SpacerCount(), Is.EqualTo(1));
        }

        [Test]
        public void Given_AnAnimateHueFilter_When_Mounted_Then_ASpacerIsAdded()
        {
            // animate-hue writes style.filter every frame, so it promotes the element the same way a static
            // filter utility does.
            s_effect = CreateEffect();
            MountWithClassAndLayout("w-[128px] h-[128px] animate-hue");

            Assert.That(SpacerCount(), Is.EqualTo(1));
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
