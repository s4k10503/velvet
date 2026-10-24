using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the clone a VisualEffect source is mounted as: hidden with the camera under it, holding every
    /// renderer, moved to the lowest layer a project leaves unnamed with one advisory when none is left, drawn by a
    /// layer with the premultiplied blend material, and how the play trigger, Stop and a teardown treat it.
    /// GWT, one assert each.
    /// </summary>
    internal sealed class ParticlesEffectViewTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<Object> _spawned = new();

        private static UnityEngine.VFX.VisualEffect s_visualEffect;
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
            s_visualEffect = null;
        }

        // A VisualEffect source with a quad under it, which gives the clone a renderer to hold.
        private UnityEngine.VFX.VisualEffect CreateVisualEffectSource(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            GameObject.CreatePrimitive(PrimitiveType.Quad).transform.SetParent(go.transform, false);
            return go.AddComponent<UnityEngine.VFX.VisualEffect>();
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

        // Names every unnamed layer from firstLayer to 31 for the duration of the case, as a project with no layer to
        // spare above it.
        private static List<int> NameEveryFreeLayer(UnityEditor.SerializedObject tagManager, int firstLayer = 8)
        {
            var layers = tagManager.FindProperty("layers");
            var named = new List<int>();
            for (var i = firstLayer; i < 32; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layer.stringValue))
                {
                    layer.stringValue = "VelvetTestLayer" + i;
                    named.Add(i);
                }
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return named;
        }

        private static void UnnameLayers(UnityEditor.SerializedObject tagManager, List<int> named)
        {
            var layers = tagManager.FindProperty("layers");
            foreach (var i in named)
            {
                layers.GetArrayElementAtIndex(i).stringValue = string.Empty;
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static UnityEditor.SerializedObject LoadTagManager() => new(
            UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

        // GREEN_ON_BASE(characterization): the clone takes the lowest layer a project leaves unnamed, as before.
        [Test]
        public void Given_AProjectLeavingOnlyLayerEightUnnamed_When_AVisualEffectMounts_Then_ItsCloneStandsOnLayerEight()
        {
            // Arrange
            var effect = CreateVisualEffectSource("fx-vfx-layer-eight");
            var tagManager = LoadTagManager();
            var named = NameEveryFreeLayer(tagManager, firstLayer: 9);
            int cloneLayer;
            try
            {
                // Act
                MountAndLayout(V.Particles(effect, name: "px-vfx-layer-eight", className: "w-[128px] h-[128px]"));
                cloneLayer = _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>("px-vfx-layer-eight")]
                    .EffectHost.gameObject.layer;
            }
            finally
            {
                UnnameLayers(tagManager, named);
            }

            // Assert
            Assert.That(cloneLayer, Is.EqualTo(8));
        }

        // GREEN_ON_BASE(characterization): the no-layer advisory is logged once however many effects mount, as before.
        [Test]
        public void Given_AProjectNamingEveryLayer_When_TwoVisualEffectsMount_Then_TheNoLayerWarningIsLoggedOnce()
        {
            // Arrange — the advisory's latch is process-wide, so the case resets it and puts it back.
            var first = CreateVisualEffectSource("fx-vfx-warn-a");
            var second = CreateVisualEffectSource("fx-vfx-warn-b");
            var latch = typeof(ParticlesEffectView).GetField(
                "s_warnedNoLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var latched = (bool)latch.GetValue(null);
            latch.SetValue(null, false);
            var warnings = 0;
            void CountWarning(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && message.Contains("Every layer from 8 to 31"))
                {
                    warnings++;
                }
            }
            var tagManager = LoadTagManager();
            var named = NameEveryFreeLayer(tagManager);
            Application.logMessageReceived += CountWarning;
            try
            {
                // Act
                MountAndLayout(V.Div(children: new VNode[]
                {
                    V.Particles(first, key: "a", name: "px-warn-a", className: "w-[64px] h-[64px]"),
                    V.Particles(second, key: "b", name: "px-warn-b", className: "w-[64px] h-[64px]"),
                }));
            }
            finally
            {
                Application.logMessageReceived -= CountWarning;
                UnnameLayers(tagManager, named);
                latch.SetValue(null, latched);
            }

            // Assert
            Assert.That(warnings, Is.EqualTo(1));
        }

        private ParticlesBinding MountVisualEffect(UnityEngine.VFX.VisualEffect effect, string name, PlayTrigger playOn = PlayTrigger.Mount)
        {
            MountAndLayout(V.Particles(effect, name: name, className: "w-[128px] h-[128px]", playOn: playOn));
            return _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>(name)];
        }

        // GREEN_ON_BASE(characterization): the clone is hidden from the hierarchy and from scene saves, as the ParticleSystem host is.
        [Test]
        public void Given_AMountedVisualEffect_When_Attached_Then_TheCloneIsHidden()
        {
            // Arrange
            var effect = CreateVisualEffectSource("fx-vfx-hidden");

            // Act
            var binding = MountVisualEffect(effect, "px-vfx-hidden");

            // Assert
            Assert.That(binding.EffectHost.gameObject.hideFlags, Is.EqualTo(HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor));
        }

        // GREEN_ON_BASE(characterization): the camera under the clone is hidden the way the clone is.
        [Test]
        public void Given_AMountedVisualEffect_When_Attached_Then_ItsCameraIsHidden()
        {
            // Arrange
            var effect = CreateVisualEffectSource("fx-vfx-camera-hidden");

            // Act
            var binding = MountVisualEffect(effect, "px-vfx-camera-hidden");

            // Assert
            Assert.That(binding.EffectCamera.gameObject.hideFlags, Is.EqualTo(HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor));
        }

        // GREEN_ON_BASE(characterization): every renderer under the clone is held, an inactive child's included, as before.
        [Test]
        public void Given_AVisualEffectWithAnInactiveChildRenderer_When_Attached_Then_ThatRendererIsAmongTheHeldOnes()
        {
            // Arrange — the source has its active quad and this inactive one.
            var effect = CreateVisualEffectSource("fx-vfx-inactive-child");
            var inactive = GameObject.CreatePrimitive(PrimitiveType.Quad);
            inactive.transform.SetParent(effect.transform, false);
            inactive.SetActive(false);

            // Act
            var binding = MountVisualEffect(effect, "px-vfx-inactive-child");

            // Assert
            Assert.That(binding.EffectRenderers.Length, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the layer drawing the texture takes the premultiplied blend material, as before.
        [Test]
        public void Given_AMountedVisualEffect_When_Attached_Then_ItsLayerDrawsWithThePremultipliedBlendMaterial()
        {
            // Arrange
            var effect = CreateVisualEffectSource("fx-vfx-blend");
            var premultiplied = ParticleBlendMaterials.For(ParticleBlend.Premultiplied);

            // Act
            var binding = MountVisualEffect(effect, "px-vfx-blend");

            // Assert — the material is read too, since a shader that cannot be found leaves the layer without one on either side.
            Assert.That(premultiplied != null
                && binding.EffectLayer.style.unityMaterial.Equals(new StyleMaterialDefinition(premultiplied)), Is.True);
        }

        // GREEN_ON_BASE(characterization): a Manual trigger is recorded as applied when the clone is made, as before.
        [Test]
        public void Given_AManualVisualEffect_When_Mounted_Then_TheManualTriggerIsRecordedAsApplied()
        {
            // Arrange
            var effect = CreateVisualEffectSource("fx-vfx-manual");

            // Act
            var binding = MountVisualEffect(effect, "px-vfx-manual", PlayTrigger.Manual);

            // Assert
            Assert.That(binding.AppliedPlayOn, Is.EqualTo(PlayTrigger.Manual));
        }

        // GREEN_ON_BASE(characterization): Stop on the element clears the clone's initial event, as before.
        [Test]
        public void Given_AMountedVisualEffect_When_StopIsCalledOnTheElement_Then_TheCloneNamesNoInitialEvent()
        {
            // Arrange — the source names an event, which a Mount clone keeps until it is stopped.
            var effect = CreateVisualEffectSource("fx-vfx-stop");
            effect.initialEventName = "StartBurst";
            var binding = MountVisualEffect(effect, "px-vfx-stop");
            var named = binding.EffectHost.initialEventName;

            // Act
            _host.Root.Q<ParticlesElement>("px-vfx-stop").Stop();

            // Assert
            Assert.That((named, binding.EffectHost.initialEventName), Is.EqualTo(("StartBurst", string.Empty)));
        }

        [Component]
        private static VNode PlayOnFlipHost()
        {
            var (mount, setMount) = Hooks.UseState(false);
            s_setFlag = setMount;
            return V.Particles(s_visualEffect, name: "px-vfx-flip", className: "w-[128px] h-[128px]",
                playOn: mount ? PlayTrigger.Mount : PlayTrigger.Manual);
        }

        // GREEN_ON_BASE(characterization): flipping Manual to Mount hands the clone the source's initial event back, as before.
        [Test]
        public void Given_AManualVisualEffect_When_ThePlayTriggerFlipsToMount_Then_TheCloneNamesTheSourcesInitialEvent()
        {
            // Arrange — the Manual mount cleared the event, so the flip has one to restore.
            s_visualEffect = CreateVisualEffectSource("fx-vfx-flip");
            s_visualEffect.initialEventName = "StartBurst";
            MountAndLayout(V.Component(PlayOnFlipHost, key: "root"));
            var binding = _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>("px-vfx-flip")];
            var cleared = binding.EffectHost.initialEventName;

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That((cleared, binding.EffectHost.initialEventName), Is.EqualTo((string.Empty, "StartBurst")));
        }

        [Component]
        private static VNode VisualEffectToNullHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.Particles(removed ? null : s_visualEffect, name: "px-vfx-removed", className: "w-[128px] h-[128px]");
        }

        private ParticlesBinding MountThenRemoveVisualEffect()
        {
            s_visualEffect = CreateVisualEffectSource("fx-vfx-removed");
            MountAndLayout(V.Component(VisualEffectToNullHost, key: "root"));
            var binding = _mounted.Root.Reconciler.Context.ParticlesBindings[_host.Root.Q<VisualElement>("px-vfx-removed")];
            s_setFlag.Invoke(true);
            FlushAndLayout();
            return binding;
        }

        // GREEN_ON_BASE(characterization): a binding whose clone is gone holds no reference to its camera, as before.
        [Test]
        public void Given_AMountedVisualEffect_When_RepatchedToNull_Then_TheBindingHoldsNoCamera()
        {
            // Arrange & Act — the reference is read as an object, since a destroyed camera compares equal to null as a Unity object.
            var binding = MountThenRemoveVisualEffect();

            // Assert
            Assert.That((object)binding.EffectCamera, Is.Null);
        }

        // GREEN_ON_BASE(characterization): a binding whose clone is gone holds no reference to its renderers, as before.
        [Test]
        public void Given_AMountedVisualEffect_When_RepatchedToNull_Then_TheBindingHoldsNoRenderers()
        {
            // Arrange & Act
            var binding = MountThenRemoveVisualEffect();

            // Assert
            Assert.That(binding.EffectRenderers, Is.Null);
        }
    }
}
