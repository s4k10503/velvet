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
    /// it can only switch, lands the new value, in the style pass that starts it where Velvet gave the element its
    /// transition or the element has run one and by the panel's next animation update otherwise, and sends no
    /// transition event to the element, unless transition-discrete outranks transition-normal on the element.
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

        private MountedTree Mount(VisualElement root, VNode tree)
        {
            var mounted = V.Mount(root, tree);
            _mounts.Add(mounted);
            return mounted;
        }

        // An element a className gives classNames, in a tree mounted on an element of its own.
        private VisualElement Mounted(string classNames)
        {
            var root = NewRoot();
            Mount(root, V.Div(name: "leaf", className: classNames));
            return Attached(root.Q<VisualElement>("leaf"));
        }

        // An element carrying classNames on its class list, under an element no tree is mounted on.
        private VisualElement Unmounted(string classNames, VisualElement root) => Unmounted(new VisualElement(), classNames, root);

        private VisualElement Unmounted(VisualElement element, string classNames, VisualElement root)
        {
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

        private static bool Runs(VisualElement element, string property = "justify-content")
        {
            var id = EngineMember.StylePropertyNameId.ResolveProperty()!.GetValue(new StylePropertyName(property));
            return (bool)EngineMember.HasRunningStyleAnimation.ResolveMethod()!.Invoke(element, new[] { id });
        }

        // The resolved justify-content after the style pass that starts the transitions a change asks for and the
        // panel's animation update after it, with the clock where it was; NoneRan where no transition of it started.
        // A start is read from the run event the root of the element's panel hears on its way down in either update,
        // where no hold stops one. Reading a transition still running after the style pass was rejected: in
        // Given_TransitionDiscreteAndADarkTransitionNormal_When_TheThemeIsDark_Then_TheNextAnimationUpdateLandsAWrite
        // none was.
        private static Justify AfterTheNextAnimationUpdate(VisualElement element)
        {
            var above = Above(element);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            above.Stop();
            return above.Justify > 0 ? element.resolvedStyle.justifyContent : NoneRan;
        }

        // The resolved justify-content after the style pass a change asks for, before any animation update; NoneRan
        // where the transition of control that change also asks for is not running, so that a change nothing
        // transitioned cannot pass for one landed in the pass.
        private static Justify AfterTheStylePass(VisualElement element, string control = "opacity")
        {
            ForcePanelUpdate(element.panel);
            return Runs(element, control) ? element.resolvedStyle.justifyContent : NoneRan;
        }

        // The transition events of justify-content and of opacity targeting an element that reach a listener: the
        // element itself, or the root of its panel on their way down.
        private sealed class Heard
        {
            private static readonly StylePropertyName s_justify = new("justify-content");
            private static readonly StylePropertyName s_opacity = new("opacity");

            private readonly VisualElement _listener;
            private readonly TrickleDown _phase;
            private readonly EventCallback<TransitionRunEvent> _run;
            private readonly EventCallback<TransitionStartEvent> _start;
            private readonly EventCallback<TransitionEndEvent> _end;
            private readonly EventCallback<TransitionCancelEvent> _cancel;

            public int Justify;
            public int Opacity;

            public Heard(VisualElement target, VisualElement listener, TrickleDown phase)
            {
                (_listener, _phase) = (listener, phase);
                _run = evt => Count(target, evt.target, evt.stylePropertyNames);
                _start = evt => Count(target, evt.target, evt.stylePropertyNames);
                _end = evt => Count(target, evt.target, evt.stylePropertyNames);
                _cancel = evt => Count(target, evt.target, evt.stylePropertyNames);
                listener.RegisterCallback(_run, phase);
                listener.RegisterCallback(_start, phase);
                listener.RegisterCallback(_end, phase);
                listener.RegisterCallback(_cancel, phase);
            }

            private void Count(VisualElement target, IEventHandler reached, StylePropertyNameCollection names)
            {
                if (!ReferenceEquals(reached, target))
                {
                    return;
                }
                Justify += names.Contains(s_justify) ? 1 : 0;
                Opacity += names.Contains(s_opacity) ? 1 : 0;
            }

            public void Stop()
            {
                _listener.UnregisterCallback(_run, _phase);
                _listener.UnregisterCallback(_start, _phase);
                _listener.UnregisterCallback(_end, _phase);
                _listener.UnregisterCallback(_cancel, _phase);
            }
        }

        private static Heard On(VisualElement element) => new(element, element, TrickleDown.NoTrickleDown);

        private static Heard Above(VisualElement element) => new(element, element.panel.visualTree, TrickleDown.TrickleDown);

        private static void Swap(VisualElement element, params (string From, string To)[] swaps)
        {
            foreach (var (from, to) in swaps)
            {
                Swap(element, from, to);
            }
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
        public void Given_AnElementTransitioningAll_When_ItsJustifyContentClassChanges_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange
            var element = Mounted("transition-all justify-start opacity-100 " + OneSecond);
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_ItsJustifyContentClassChanges_Then_ItHearsNoTransitionEventForIt()
        {
            // Arrange — two updates, so that a cancel event a first one would queue is sent as well.
            var element = Mounted("transition-all justify-start opacity-100 " + OneSecond);
            var heard = On(element);
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));

            // Act
            RunAQuarterSecond(element);
            RunAQuarterSecond(element);

            // Assert — the opacity the same change transitions shows the element hears its transitions' events.
            Assert.That(heard.Opacity > 0 ? heard.Justify : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_AContainerTransitioningAll_When_ItsWhiteSpaceClassChanges_Then_ItsChildInheritsTheNewValueInThatStylePass()
        {
            // Arrange — white-space is inherited, and the child has no transition of its own.
            var root = NewRoot();
            Mount(root, V.Div(name: "container", className: "transition-all whitespace-normal opacity-100 " + OneSecond,
                children: new VNode[] { V.Div(name: "child") }));
            var container = Attached(root.Q<VisualElement>("container"));
            var child = root.Q<VisualElement>("child");
            Swap(container, ("whitespace-normal", "whitespace-nowrap"), ("opacity-100", "opacity-0"));

            // Act
            ForcePanelUpdate(container.panel);

            // Assert
            Assert.That(Runs(container, "opacity") ? child.resolvedStyle.whiteSpace : (WhiteSpace)(-1),
                Is.EqualTo(WhiteSpace.NoWrap));
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
        public void Given_ATweenSwappingJustifyContent_When_ItsSwapHasStartedTheTransition_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange — the frame the tween swaps its classes in, up to the scheduler tick that swaps them.
            var element = Mounted("justify-start bg-red-500");
            new StyleAnimationScheduler().PlayVariantEnter(element, new[] { "justify-start", "bg-red-500" },
                new[] { "justify-end", "bg-blue-500" }, durationSec: 1f, easing: EasingMode.Linear, delaySec: 0f);
            ForcePanelUpdate(element.panel);
            _now += 0.016;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);

            // Act
            var landed = AfterTheStylePass(element, "background-color");

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
        // Dropping the `AllowsDiscrete(element)` branch from StyleTransitionBehavior.CancelUnlessAllowed reddens this case.
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

        // GREEN_ON_BASE(characterization): the base cancels no transition in the style pass that starts it.
        // Calling the cancel in StyleTransitionBehavior.OnRestyled without CancelUnlessAllowed's checks reddens this case.
        [Test]
        public void Given_TransitionAllWithTransitionDiscrete_When_TheJustifyContentClassChanges_Then_AQuarterSecondInItKeepsTheOldValue()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete justify-start " + OneSecond);
            Swap(element, "justify-start", "justify-end");

            // Act
            RunAQuarterSecond(element);

            // Assert
            Assert.That(WhileItsTransitionRuns(element), Is.EqualTo(Justify.FlexStart));
        }

        // GREEN_ON_BASE(characterization): the base decides a transition only at its run event, so a style pass leaves it.
        // Reading no recorded decision in StyleTransitionBehavior.CancelUnlessAllowed reddens this case.
        [Test]
        public void Given_AJustifyContentTransitionTransitionDiscreteLetRun_When_TransitionNormalIsAddedAQuarterSecondIn_Then_ItKeepsRunning()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete justify-start " + OneSecond);
            Swap(element, "justify-start", "justify-end");
            RunAQuarterSecond(element);

            // Act
            element.AddToClassList("transition-normal");
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(WhileItsTransitionRuns(element), Is.EqualTo(Justify.FlexStart));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_ItsJustifyContentIsWrittenInline_Then_ItHearsNoTransitionEventForIt()
        {
            // Arrange — two updates, so that a cancel event a first one would queue is sent as well.
            var element = Mounted("transition-all " + OneSecond);
            var heard = On(element);
            var above = Above(element);
            element.style.justifyContent = Justify.FlexEnd;

            // Act
            RunAQuarterSecond(element);
            RunAQuarterSecond(element);

            // Assert — the panel root hearing the run on its way down shows the write started a transition.
            Assert.That(above.Justify > 0 ? heard.Justify : -1, Is.EqualTo(0));
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
        public void Given_ADurationWithNoTransitionUtility_When_TheJustifyContentClassChanges_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange — no rule names a transition-property, so UI Toolkit's initial `all` stands.
            var element = Mounted("justify-start opacity-100 " + OneSecond);
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_AnArbitraryDurationAlone_When_TheJustifyContentClassChanges_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange — the duration is written inline, and no utility of the element declares one.
            var element = Mounted("justify-start opacity-100 ease-linear duration-[1000ms]");
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

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
        public void Given_AnApplicationRuleThatRanATransition_When_TheJustifyContentClassChangesAgain_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange — the first change is cancelled at the animation update a quarter second in.
            AttachSheet(".app-justify { transition-property: justify-content, opacity; transition-duration: 1s; }");
            var element = Mounted("app-justify justify-start opacity-100");
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));
            RunAQuarterSecond(element);
            Swap(element, ("justify-end", "justify-start"), ("opacity-0", "opacity-100"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexStart));
        }

        [Test]
        public void Given_ARowAPortalRendersOutsideTheMount_When_ItsJustifyContentClassChanges_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange
            var container = NewRoot();
            Mount(NewRoot(), V.Div(children: new VNode[]
            {
                V.Portal(container, new VNode[]
                {
                    V.Div(name: "ported", className: "transition-all justify-start opacity-100 " + OneSecond),
                }),
            }));
            var element = Attached(container.Q<VisualElement>("ported"));
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

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

        private sealed class Shown : Store<bool>
        {
            public Shown() : base(true) { }

            public void Hide() => SetState(_ => false);

            protected override void ResetCore() => SetState(_ => true);
        }

        private static Shown s_shown;
        private static VisualElement s_portalTarget;

        [Component]
        private static VNode PortalHost() => V.Div(children: new VNode[]
        {
            V.Portal(s_portalTarget,
                Hooks.UseStore(s_shown, shown => shown) ? new VNode[] { V.Div(name: "ported") } : Array.Empty<VNode>()),
        });

        // GREEN_ON_BASE(characterization): the base cancels no transition UI Toolkit runs of justify-content.
        // Leaving out RowHolds' release of its StyleTransitionBehavior hold reddens this case.
        [Test]
        public void Given_ARowAPortalStoppedRendering_When_ItChangesJustifyContentUnderNoMount_Then_UIToolkitRunsItsTransition()
        {
            // Arrange
            s_shown = new Shown();
            s_portalTarget = NewRoot();
            var mounted = Mount(NewRoot(), V.Component(PortalHost, key: "host"));
            var row = s_portalTarget.Q<VisualElement>("ported");
            s_shown.Hide();
            mounted.FlushStateForTest();
            var released = s_portalTarget.Q<VisualElement>("ported") == null;
            var element = Unmounted(row, "transition-all justify-start " + OneSecond, NewRoot());
            Swap(element, "justify-start", "justify-end");

            // Act
            var after = AfterTheNextAnimationUpdate(element);

            // Assert — the portal gave the row back only where it left the target.
            Assert.That(released ? after : NoneRan, Is.EqualTo(Justify.FlexStart));
        }

        [Component]
        private static VNode LeafHost() => V.Div(children: new VNode[]
        {
            Hooks.UseStore(s_shown, shown => shown) ? V.Div(name: "leaf", className: "transition-all") : null,
        });

        [Test]
        public void Given_AnElementATransitionUtilityGaveATransition_When_ItIsRemoved_Then_ItKeepsNoStyleCallbackOnItsWayDown()
        {
            // Arrange
            s_shown = new Shown();
            var root = NewRoot();
            var mounted = Mount(root, V.Component(LeafHost, key: "host"));
            var leaf = root.Q<VisualElement>("leaf");
            var watched = CallbackRegistryProbe.TrickleDownCallbackCount(leaf) > 0;

            // Act
            s_shown.Hide();
            mounted.FlushStateForTest();

            // Assert — -1 where nothing registered one while the element was rendered.
            Assert.That(watched ? CallbackRegistryProbe.TrickleDownCallbackCount(leaf) : -1, Is.EqualTo(0));
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
