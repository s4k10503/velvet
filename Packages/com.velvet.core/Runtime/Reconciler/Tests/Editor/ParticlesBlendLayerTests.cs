using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <c>V.Particles</c> arranges what a hosted effect's systems draw: the order systems tie in, the runs
    /// that need no layer, the blend layers over the element and the extents they report, and how the tick and a
    /// teardown treat them. GWT, one assert each.
    /// </summary>
    internal sealed class ParticlesBlendLayerTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<Object> _spawned = new();
        private long _fakeMs;

        private static ParticleSystem s_effect;
        private static StateUpdater<bool> s_setFlag;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
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
        }

        private ParticleSystem CreateEffectSource(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<ParticleSystem>();
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

        private ParticlesBinding MountEffect(ParticleSystem effect, string name)
        {
            MountAndLayout(V.Particles(effect, name: name, className: "w-[128px] h-[128px]"));
            return _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>(name)];
        }

        // Each 125 fake-ms step fires the tick once and advances the simulation by exactly that step.
        private void AdvanceTicks(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                _fakeMs += 125;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
        }


        // The material a legacy particle shader draws with, in the transparent queue whichever shader it is.
        private Material NewParticleMaterial(string shaderName)
        {
            var material = new Material(Shader.Find(shaderName)) { renderQueue = 3000 };
            _spawned.Add(material);
            return material;
        }

        private ParticleSystem CreateChildSystem(ParticleSystem parent, string name, Material material)
        {
            var child = new GameObject(name).AddComponent<ParticleSystem>();
            child.transform.SetParent(parent.transform, false);
            child.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return child;
        }

        // An alpha-blended root over an additive child at the same place and in the same queue, which hierarchy order
        // puts as two runs: the element draws the first, and a layer draws the second.
        private ParticleSystem CreateAlphaRootOverAdditiveChild(string name)
        {
            var effect = CreateEffectSource(name);
            effect.GetComponent<ParticleSystemRenderer>().sharedMaterial = NewParticleMaterial("Legacy Shaders/Particles/Alpha Blended");
            CreateChildSystem(effect, name + "-additive", NewParticleMaterial("Legacy Shaders/Particles/Additive"));
            return effect;
        }

        private ParticlesBinding MountAlphaRootOverAdditiveChild(string name)
        {
            _fakeMs = 1000;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _fakeMs / 1000.0);
            return MountEffect(CreateAlphaRootOverAdditiveChild(name), name);
        }

        // GREEN_ON_BASE(characterization): systems that tie on every sort key keep hierarchy order, as before.
        [Test]
        public void Given_TwoSystemsAtTheSameDepth_When_Mounted_Then_TheyAreOrderedAsTheHierarchyHoldsThem()
        {
            // Arrange — both draw with one material from one place, so nothing but the hierarchy orders them.
            var effect = CreateEffectSource("fx-tied");
            var material = NewParticleMaterial("Legacy Shaders/Particles/Alpha Blended");
            effect.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            CreateChildSystem(effect, "fx-tied-child", material);

            // Act
            var binding = MountEffect(effect, "px-tied");

            // Assert
            Assert.That(string.Join(",", binding.Order.Select(placed => placed.Batch.System)), Is.EqualTo("0,1"));
        }

        // GREEN_ON_BASE(characterization): a system whose renderer is off adds no run, so a mount with nothing to draw arranges nothing.
        [Test]
        public void Given_ASystemWhoseRendererIsOff_When_Mounted_Then_NoRunIsArranged()
        {
            // Arrange
            var effect = CreateEffectSource("fx-renderer-off");
            effect.GetComponent<ParticleSystemRenderer>().enabled = false;

            // Act
            var binding = MountEffect(effect, "px-renderer-off");

            // Assert
            Assert.That(binding.Runs, Is.Empty);
        }

        private static int LayerChildren(VisualElement element)
            => element.Children().Count(child => child.ClassListContains(ParticlesDriver.LayerMarkerClass));

        [Component]
        private static VNode LayeredEffectToNullHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.Particles(removed ? null : s_effect, name: "px-layered", className: "w-[128px] h-[128px]");
        }

        // GREEN_ON_BASE(characterization): the layers an effect drew over the element leave with it, as before.
        [Test]
        public void Given_AnEffectWithABlendLayer_When_RepatchedToNull_Then_TheLayerLeavesTheElement()
        {
            // Arrange
            s_effect = CreateAlphaRootOverAdditiveChild("fx-layered");
            MountAndLayout(V.Component(LayeredEffectToNullHost, key: "root"));
            var element = _host.Root.Q<VisualElement>("px-layered");
            var before = LayerChildren(element);

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That((before, LayerChildren(element)), Is.EqualTo((1, 0)));
        }

        // GREEN_ON_BASE(characterization): a layer whose run is gone takes its recorded blend with it, as before.
        [Test]
        public void Given_ABlendLayerWhoseSystemWasDestroyed_When_TheTickArranges_Then_TheLayersBlendIsDropped()
        {
            // Arrange
            var binding = MountAlphaRootOverAdditiveChild("fx-shrink");
            var layers = binding.Layers.Count;
            Object.DestroyImmediate(binding.Systems[1].System.gameObject);

            // Act
            AdvanceTicks(1);

            // Assert
            Assert.That((layers, binding.LayerBlends.Count), Is.EqualTo((1, 0)));
        }

        // The extents of two paint targets, the element's and its layer's, set by hand so the tick's own arrangement is
        // what reads them.
        private ParticlesBinding MountAndPresetTwoExtents(string name)
        {
            var binding = MountAlphaRootOverAdditiveChild(name);
            binding.TargetExtents.Clear();
            binding.TargetExtents.Add(new Rect(0f, 0f, 10f, 10f));
            binding.TargetExtents.Add(new Rect(20f, 30f, 10f, 10f));
            return binding;
        }

        // GREEN_ON_BASE(characterization): the arrangement keeps the extent of every target it has, as before.
        [Test]
        public void Given_AnExtentForTheElementAndForItsLayer_When_TheTickArranges_Then_BothAreKept()
        {
            // Arrange
            var binding = MountAndPresetTwoExtents("fx-extents-kept");

            // Act
            AdvanceTicks(1);

            // Assert
            Assert.That(binding.TargetExtents.Count, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the live extent is the union of the targets' extents after the arrangement, as before.
        [Test]
        public void Given_AnExtentForTheElementAndForItsLayer_When_TheTickArranges_Then_TheLiveExtentIsTheirUnion()
        {
            // Arrange
            var binding = MountAndPresetTwoExtents("fx-extents-union");

            // Act
            AdvanceTicks(1);

            // Assert
            Assert.That(binding.LiveExtentLocal, Is.EqualTo(new Rect(0f, 0f, 30f, 40f)));
        }

        // GREEN_ON_BASE(characterization): every tick marks the blend layers for repaint along with the element, as before.
        [Test]
        public void Given_ALiveEffectWithABlendLayer_When_TheTickFires_Then_TheLayerRepaints()
        {
            // Arrange — two passes take up whatever the mount left dirty, as SceneViewTests' CountRepaints does.
            var binding = MountAlphaRootOverAdditiveChild("fx-layer-repaint");
            var repaints = 0;
            binding.Layers[0].generateVisualContent += _ => repaints++;
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            repaints = 0;

            // Act
            AdvanceTicks(1);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(repaints, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): what the element drew is reported as an extent that spans it, as before.
        [Test]
        public void Given_ALiveEffect_When_TheElementIsPainted_Then_ItsExtentHasAWidth()
        {
            // Arrange — the emission fills the host over the first ticks, and the pass after them paints it.
            _fakeMs = 1000;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _fakeMs / 1000.0);
            var effect = CreateEffectSource("fx-extent-width");
            effect.GetComponent<ParticleSystemRenderer>().sharedMaterial = NewParticleMaterial("Legacy Shaders/Particles/Alpha Blended");
            var emission = effect.emission;
            emission.rateOverTime = 1000f;
            var binding = MountEffect(effect, "px-extent-width");
            AdvanceTicks(3);

            // Act
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(binding.TargetExtents[0].width, Is.GreaterThan(0f));
        }

        // GREEN_ON_BASE(characterization): a live element's Stop ends the simulation's logical play, as before.
        [Test]
        public void Given_ALiveHostOutsidePlayMode_When_StopIsCalledOnTheElement_Then_ItIsNoLongerLogicallyPlaying()
        {
            // Arrange
            var effect = CreateEffectSource("fx-stop-element");
            var binding = MountEffect(effect, "px-stop-element");
            var playing = binding.LogicallyPlaying;

            // Act
            _host.Root.Q<ParticlesElement>("px-stop-element").Stop();

            // Assert
            Assert.That((playing, binding.LogicallyPlaying), Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): a blend layer is positioned absolutely, as before.
        [Test]
        public void Given_ABlendLayer_When_ItIsCreated_Then_ItIsPositionedAbsolutely()
        {
            // Arrange & Act
            var layer = ParticlesDriver.CreateLayer();

            // Assert
            Assert.That(layer.style.position, Is.EqualTo(new StyleEnum<Position>(Position.Absolute)));
        }

        // GREEN_ON_BASE(characterization): a blend layer is pinned to the element's left edge, as before.
        [Test]
        public void Given_ABlendLayer_When_ItIsCreated_Then_ItIsPinnedToTheLeftEdge()
        {
            // Arrange & Act
            var layer = ParticlesDriver.CreateLayer();

            // Assert
            Assert.That(layer.style.left, Is.EqualTo(new StyleLength(0f)));
        }
    }
}
