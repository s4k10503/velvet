using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins CSS's transition-behavior on what Velvet renders: a transition UI Toolkit starts for justify-content, which
    /// it can only switch, lands the new value by the panel's next animation update, whatever gave the element its
    /// transition, unless transition-discrete outranks transition-normal on the element.
    /// </summary>
    internal sealed class TransitionDiscretePanelTests : PanelTestBase
    {
        private const BindingFlags Internal = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance;

        private const string OneSecond = "duration-1000 ease-linear";

        // Where no transition of justify-content ran, so that a change nothing transitioned cannot pass for one
        // landed or held back.
        private const Justify NoneRan = (Justify)(-1);

        private readonly List<MountedTree> _mounts = new();
        private StyleSheet _sheet;
        private double _now;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void TearDown()
        {
            VelvetTheme.IsDark = false;
            foreach (var mount in _mounts)
            {
                mount.Dispose();
            }
            _mounts.Clear();
            if (_sheet != null)
            {
                UnityEngine.Object.DestroyImmediate(_sheet);
                _sheet = null;
            }
            base.TearDown();
        }

        // An element of its own under the window's root, for a tree to be mounted on or a portal to render into.
        private VisualElement NewRoot()
        {
            var root = new VisualElement();
            _window.rootVisualElement.Add(root);
            return root;
        }

        private void Mount(VisualElement root, VNode tree) => _mounts.Add(V.Mount(root, tree));

        // An element a className gives classNames, in a tree mounted on an element of its own.
        private VisualElement Mounted(string classNames)
        {
            var root = NewRoot();
            Mount(root, V.Div(name: "leaf", className: classNames));
            return Attached(root.Q<VisualElement>("leaf"));
        }

        // An element carrying classNames on its class list, under an element no tree is mounted on.
        private VisualElement Unmounted(string classNames, VisualElement root)
        {
            var element = new VisualElement();
            foreach (var className in classNames.Split(' '))
            {
                element.AddToClassList(className);
            }
            root.Add(element);
            return Attached(element);
        }

        private VisualElement Attached(VisualElement element)
        {
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => _now);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            return element;
        }

        private void RunFor(VisualElement element, double seconds)
        {
            ForcePanelUpdate(element.panel);
            _now += seconds;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
        }

        // A quarter of the way into the one-second transitions the cases declare.
        private void RunAQuarterSecond(VisualElement element) => RunFor(element, 0.25);

        private static void Swap(VisualElement element, string from, string to)
        {
            element.RemoveFromClassList(from);
            element.AddToClassList(to);
        }

        private static bool Runs(VisualElement element)
        {
            var id = EngineMember.StylePropertyNameId.ResolveProperty()!.GetValue(new StylePropertyName("justify-content"));
            return (bool)EngineMember.HasRunningStyleAnimation.ResolveMethod()!.Invoke(element, new[] { id });
        }

        // The resolved justify-content after the style pass that starts the transitions a change asks for and the
        // panel's animation update after it, with the clock where it was; NoneRan where that pass started none.
        private static Justify AfterTheNextAnimationUpdate(VisualElement element)
        {
            ForcePanelUpdate(element.panel);
            var started = Runs(element);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            return started ? element.resolvedStyle.justifyContent : NoneRan;
        }

        private static Justify WhileItsTransitionRuns(VisualElement element)
            => Runs(element) ? element.resolvedStyle.justifyContent : NoneRan;

        // A stylesheet of the application's own, attached after the bundled one.
        private void AttachSheet(string uss)
        {
            _sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
            try
            {
                StockStyleSheetImport.Import!.Invoke(impl, new object[] { _sheet, uss });
            }
            catch (TargetInvocationException)
            {
                // The importer reports through its context, which this instance has none of; what it stored is used.
            }
            _window.rootVisualElement.styleSheets.Add(_sheet);
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_ItsJustifyContentClassChanges_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange
            var element = Mounted("transition-all justify-start " + OneSecond);
            Swap(element, "justify-start", "justify-end");
            ForcePanelUpdate(element.panel);
            var started = Runs(element);
            TestContext.WriteLine($"After the style pass, before the animation update: {element.resolvedStyle.justifyContent}");

            // Act
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);

            // Assert
            Assert.That(started ? element.resolvedStyle.justifyContent : NoneRan, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_ItsJustifyContentIsWrittenInline_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange
            var element = Mounted("transition-all " + OneSecond);
            element.style.justifyContent = Justify.FlexEnd;

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_ATweenSwappingJustifyContent_When_ItsSwapHasStartedTheTransition_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange — the frame the tween swaps its classes in, up to the scheduler tick that swaps them.
            var element = Mounted("justify-start");
            new StyleAnimationScheduler().PlayVariantEnter(element, new[] { "justify-start" }, new[] { "justify-end" },
                durationSec: 1f, easing: EasingMode.Linear, delaySec: 0f);
            ForcePanelUpdate(element.panel);
            _now += 0.016;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        // GREEN_ON_BASE(characterization): the base transitions opacity under transition-all as this change does.
        // Adding `opacity` to StyleTransitionBehavior.LandedAtOnce reddens this case.
        [Test]
        public void Given_AnElementTransitioningAllOnItsOwnDuration_When_ItsOpacityClassChanges_Then_AQuarterOfTheWayInItIsAQuarterOfTheWay()
        {
            // Arrange — transition-all's own duration, the sheet's 0.15s, so that nothing else gives the element one.
            var element = Mounted("transition-all ease-linear opacity-100");
            Swap(element, "opacity-100", "opacity-0");

            // Act
            RunFor(element, 0.15 / 4);

            // Assert
            Assert.That(element.resolvedStyle.opacity, Is.EqualTo(0.75f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base cancels no transition UI Toolkit runs of justify-content.
        // Dropping `!AllowsDiscrete(element)` from StyleTransitionBehavior.OnRun reddens this case.
        [Test]
        public void Given_TransitionAllWithTransitionDiscrete_When_JustifyContentIsWrittenInline_Then_AQuarterSecondInItKeepsTheOldValue()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete " + OneSecond);

            // Act
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);

            // Assert
            Assert.That(WhileItsTransitionRuns(element), Is.EqualTo(Justify.FlexStart));
        }

        // GREEN_ON_BASE(characterization): the engine switches a property it cannot interpolate before its end, on the base too.
        // Nothing in Velvet reddens this case: it fails when the engine stops switching by three quarters of the way.
        [Test]
        public void Given_TransitionAllWithTransitionDiscrete_When_JustifyContentIsWrittenInline_Then_ThreeQuartersInItHoldsTheNewValue()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete " + OneSecond);
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);

            // Act
            RunFor(element, 0.5);

            // Assert
            Assert.That(element.resolvedStyle.justifyContent, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_TransitionDiscreteAndTransitionNormalOnOneRank_When_JustifyContentIsWrittenInline_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete transition-normal " + OneSecond);
            element.style.justifyContent = Justify.FlexEnd;

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert — the tie is only one where transition-discrete is on the element.
            Assert.That(element.ClassListContains("transition-discrete") ? landed : NoneRan, Is.EqualTo(Justify.FlexEnd));
        }

        // GREEN_ON_BASE(characterization): the base cancels no transition UI Toolkit runs of justify-content.
        // Ranking transition-normal over transition-discrete whatever their keys reddens this case.
        [Test]
        public void Given_TransitionNormalAndADarkTransitionDiscrete_When_TheThemeIsDark_Then_AQuarterSecondIntoAWriteKeepsTheOldValue()
        {
            // Arrange
            var element = Mounted("transition-all transition-normal dark:transition-discrete " + OneSecond);
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(element.panel);

            // Act
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);

            // Assert
            Assert.That(WhileItsTransitionRuns(element), Is.EqualTo(Justify.FlexStart));
        }

        [Test]
        public void Given_TransitionDiscreteAndADarkTransitionNormal_When_TheThemeIsDark_Then_TheNextAnimationUpdateLandsAWrite()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete dark:transition-normal " + OneSecond);
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(element.panel);
            element.style.justifyContent = Justify.FlexEnd;

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        // GREEN_ON_BASE(characterization): the base cancels no transition UI Toolkit runs of justify-content.
        // Leaving a play's ranking-only entries out of StyleClassProjection.Model.HighestKey reddens this case.
        [Test]
        public void Given_TransitionAllUnderADarkTransitionNormal_When_APlayShowsTransitionDiscrete_Then_AQuarterSecondIntoAWriteKeepsTheOldValue()
        {
            // Arrange — the dark payload builds the element's projection model before the play shows anything.
            var element = Mounted("transition-all dark:transition-normal " + OneSecond);
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(element.panel);
            StyleAnimationClassUtils.AddClasses(element, new[] { "transition-discrete" }, StyleLayerPriority.Animation);
            ForcePanelUpdate(element.panel);

            // Act
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);

            // Assert — the play outranks a transition-normal only where the dark payload put one on.
            Assert.That(element.ClassListContains("transition-normal") ? WhileItsTransitionRuns(element) : NoneRan,
                Is.EqualTo(Justify.FlexStart));
        }

        [Test]
        public void Given_TransitionAllUnderADarkTransitionDiscrete_When_APlayShowsTransitionNormal_Then_TheNextAnimationUpdateLandsAWrite()
        {
            // Arrange
            var element = Mounted("transition-all dark:transition-discrete " + OneSecond);
            VelvetTheme.IsDark = true;
            ForcePanelUpdate(element.panel);
            StyleAnimationClassUtils.AddClasses(element, new[] { "transition-normal" }, StyleLayerPriority.Animation);
            ForcePanelUpdate(element.panel);
            element.style.justifyContent = Justify.FlexEnd;

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert — the play outranks a transition-discrete only where the dark payload put one on.
            Assert.That(element.ClassListContains("transition-discrete") ? landed : NoneRan, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_ADurationWithNoTransitionUtility_When_TheJustifyContentClassChanges_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange — no rule names a transition-property, so UI Toolkit's initial `all` stands.
            var element = Mounted("justify-start " + OneSecond);
            Swap(element, "justify-start", "justify-end");

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_AnApplicationRuleListingJustifyContent_When_TheJustifyContentClassChanges_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange
            AttachSheet(".app-justify { transition-property: justify-content; transition-duration: 1s; }");
            var element = Mounted("app-justify justify-start");
            Swap(element, "justify-start", "justify-end");

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_ARowAPortalRendersOutsideTheMount_When_ItsJustifyContentClassChanges_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange
            var container = NewRoot();
            Mount(NewRoot(), V.Div(children: new VNode[]
            {
                V.Portal(container, new VNode[] { V.Div(name: "ported", className: "transition-all justify-start " + OneSecond) }),
            }));
            var element = Attached(container.Q<VisualElement>("ported"));
            Swap(element, "justify-start", "justify-end");

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_AnElementALayerPortalRenders_When_ItsJustifyContentIsWrittenInline_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange — a transition given inline, which needs no stylesheet on the layer's panel.
            Mount(NewRoot(), V.Portal(UILayer.Overlay, new VNode[] { V.Div(name: "overlaid") }));
            var element = Resources.FindObjectsOfTypeAll<UIDocument>()
                .Select(document => document.rootVisualElement?.Q<VisualElement>("overlaid"))
                .First(found => found != null);
            element.style.transitionDuration = new List<TimeValue> { new(1f, TimeUnit.Second) };
            Attached(element);
            element.style.justifyContent = Justify.FlexEnd;

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        // GREEN_ON_BASE(characterization): the base cancels no transition UI Toolkit runs of justify-content.
        // Holding StyleTransitionBehavior on the panel's root rather than on what Velvet renders into reddens this case.
        [Test]
        public void Given_AnElementUnderNoMount_When_ItsJustifyContentClassChanges_Then_UIToolkitRunsItsTransition()
        {
            // Arrange
            var element = Unmounted("transition-all justify-start " + OneSecond, NewRoot());
            Swap(element, "justify-start", "justify-end");

            // Act
            var after = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(after, Is.EqualTo(Justify.FlexStart));
        }

        // GREEN_ON_BASE(characterization): the base cancels no transition UI Toolkit runs of justify-content.
        // Leaving out MountedTree.Dispose's release of its StyleTransitionBehavior hold reddens this case.
        [Test]
        public void Given_ATreeUnmountedFromAnElement_When_AnElementAddedUnderItChangesJustifyContent_Then_UIToolkitRunsItsTransition()
        {
            // Arrange
            var root = NewRoot();
            V.Mount(root, V.Div()).Dispose();
            var element = Unmounted("transition-all justify-start " + OneSecond, root);
            Swap(element, "justify-start", "justify-end");

            // Act
            var after = AfterTheNextAnimationUpdate(element);

            // Assert
            Assert.That(after, Is.EqualTo(Justify.FlexStart));
        }

        private static object s_sink;

        // A class no rule names, on and off, each change matched again by a style pass; nothing it changes transitions.
        private static Action Toggled(VisualElement element)
        {
            var applyStyles = (Action)Delegate.CreateDelegate(typeof(Action), element.panel,
                element.panel.GetType().GetMethod("ApplyStyles", Internal, null, Type.EmptyTypes, null)!);
            return () =>
            {
                element.AddToClassList("app-marker");
                applyStyles();
                element.RemoveFromClassList("app-marker");
                applyStyles();
            };
        }

        // GREEN_ON_BASE(characterization): the base's transition-all adds no allocation to a style pass changing nothing.
        // Listing transition-all's longhands in _effects.uss in place of `all` reddens this case.
        [Test]
        public void Given_ATransitionAllElementAndAPlainOneUnderAMount_When_AClassNoRuleNamesToggles_Then_TheirStylePassesAllocateAlike()
        {
            // Arrange
            var transitioning = Toggled(Mounted("transition-all"));
            var plain = Toggled(Mounted("app-plain"));
            var canary = GCAllocationProbe.MedianBlocksDuring(() => s_sink = new object());

            // Act
            var extra = GCAllocationProbe.MedianBlocksDuring(transitioning) - GCAllocationProbe.MedianBlocksDuring(plain);

            // Assert
            Assert.That(canary > 0 ? extra : -1, Is.EqualTo(0));
        }
    }
}
