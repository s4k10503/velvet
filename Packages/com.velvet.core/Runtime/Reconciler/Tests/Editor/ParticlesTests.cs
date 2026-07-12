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
            // so mounting one must say so instead of drawing garbage silently.
            var effect = CreateEffectSource("fx");
            var main = effect.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            LogAssert.Expect(LogType.Warning, new Regex("world", RegexOptions.IgnoreCase));

            // Act
            MountAndLayout(V.Particles(effect, className: "w-[128px] h-[128px]"));

            // Assert — the host still exists (the warning is advisory, not a rejection).
            Assert.That(CountSystems(), Is.EqualTo(_baselineSystems + 1));
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
