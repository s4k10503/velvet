using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the filter bounds-spacer for V.Particles at the reconcile boundary: a filter would clip the
    /// particle quads that draw beyond the host rect, so a transparent last-child spacer widens the element's
    /// boundingBox to keep them. The spacer appears with a filter — base, variant-carried, or animate-hue —
    /// and vanishes when the filter goes, driven by the class list rather than the effect. GWT, one assert each.
    /// </summary>
    internal sealed class ParticlesFilterSpacerTests
    {
        private const string Filtered = "w-[128px] h-[128px] hue-rotate-90";
        private const string Unfiltered = "w-[128px] h-[128px]";

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<Object> _spawned = new();
        private static ParticleSystem s_effect;
        private static StateUpdater<string> s_setClass;

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
            s_setClass = default;
        }

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
    }
}
