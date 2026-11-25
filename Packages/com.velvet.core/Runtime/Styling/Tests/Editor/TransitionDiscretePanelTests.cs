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
    /// transition or the element has run one and by the panel's next animation update otherwise, unless
    /// transition-discrete outranks transition-normal on the element.
    /// </summary>
    internal sealed class TransitionDiscretePanelTests : PanelTestBase
    {
        private const BindingFlags Internal = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance;

        private const string OneSecond = "duration-1000 ease-linear";

        // StyleTransitionBehavior.WatchClass, spelled out so that the fixture builds on a tree without it.
        private const string Watch = "velvet-transition-watch";

        // Where no transition of justify-content ran, so that a change nothing transitioned cannot pass for one
        // landed or held back.
        private const Justify NoneRan = (Justify)(-1);

        private readonly List<MountedTree> _mounts = new();
        private readonly List<(FieldInfo Field, object Bound)> _reads = new();
        private StyleSheet _sheet;
        private double _now;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        public override void TearDown()
        {
            foreach (var (field, bound) in _reads)
            {
                field.SetValue(null, bound);
            }
            _reads.Clear();
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
        // A start is read off the engine: before the style pass for one an inline write started, since an update of the
        // panel the pass is part of can already have cancelled it, and after the pass for one the pass started. A run
        // event was rejected as the reading, since a watched element's own handler stops it.
        private static Justify AfterTheNextAnimationUpdate(VisualElement element)
        {
            var started = Runs(element);
            ForcePanelUpdate(element.panel);
            started |= Runs(element);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            return started ? element.resolvedStyle.justifyContent : NoneRan;
        }

        // The resolved justify-content after the style pass a change asks for, before any animation update; NoneRan
        // where the transition of control that change also asks for is not running, so that a change nothing
        // transitioned cannot pass for one landed in the pass.
        private static Justify AfterTheStylePass(VisualElement element, string control = "opacity")
        {
            ForcePanelUpdate(element.panel);
            return Runs(element, control) ? element.resolvedStyle.justifyContent : NoneRan;
        }

        // The transition events of justify-content and of opacity targeting an element that reach a listener on it.
        private sealed class Heard
        {
            private static readonly StylePropertyName s_justify = new("justify-content");
            private static readonly StylePropertyName s_opacity = new("opacity");

            private readonly VisualElement _element;

            public int Justify;
            public int Opacity;
            public int JustifyCancels;

            public Heard(VisualElement element)
            {
                _element = element;
                element.RegisterCallback<TransitionRunEvent>(evt => Count(evt.target, evt.stylePropertyNames));
                element.RegisterCallback<TransitionStartEvent>(evt => Count(evt.target, evt.stylePropertyNames));
                element.RegisterCallback<TransitionEndEvent>(evt => Count(evt.target, evt.stylePropertyNames));
                element.RegisterCallback<TransitionCancelEvent>(evt =>
                {
                    Count(evt.target, evt.stylePropertyNames);
                    JustifyCancels += ReferenceEquals(evt.target, element) && evt.stylePropertyNames.Contains(s_justify) ? 1 : 0;
                });
            }

            private void Count(IEventHandler reached, StylePropertyNameCollection names)
            {
                if (!ReferenceEquals(reached, _element))
                {
                    return;
                }
                Justify += names.Contains(s_justify) ? 1 : 0;
                Opacity += names.Contains(s_opacity) ? 1 : 0;
            }
        }

        private static Heard On(VisualElement element) => new(element);

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

        // GREEN_ON_BASE(characterization): the base stops no transition event.
        // Returning true where StyleTransitionBehavior.CancelUnlessAllowed finds the transition it let run reddens this case.
        [Test]
        public void Given_TransitionAllWithTransitionDiscrete_When_TheJustifyContentClassChanges_Then_TheElementHearsItsRunAndStart()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete justify-start " + OneSecond);
            var heard = On(element);
            Swap(element, "justify-start", "justify-end");

            // Act
            RunAQuarterSecond(element);

            // Assert
            Assert.That(heard.Justify, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base stops no transition event.
        // Returning true where StyleTransitionBehavior.CancelUnlessAllowed finds transition-discrete applies reddens this case.
        [Test]
        public void Given_TransitionAllWithTransitionDiscrete_When_JustifyContentIsWrittenInline_Then_TheElementHearsItsRunAndStart()
        {
            // Arrange
            var element = Mounted("transition-all transition-discrete " + OneSecond);
            var heard = On(element);
            element.style.justifyContent = Justify.FlexEnd;

            // Act
            RunAQuarterSecond(element);

            // Assert
            Assert.That(heard.Justify, Is.EqualTo(2));
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

            // Assert — NoneRan where transition-normal is not on the element, which would leave the decision nothing to
            // outlast.
            Assert.That(element.ClassListContains("transition-normal") ? WhileItsTransitionRuns(element) : NoneRan,
                Is.EqualTo(Justify.FlexStart));
        }

        [Test]
        public void Given_AJustifyContentTransitionTransitionDiscreteLetRun_When_ItsEndChangesOnceTransitionNormalOutranksIt_Then_TheStylePassThatRestylesItLandsTheNewEnd()
        {
            // Arrange — the change retargets the running transition, which UI Toolkit replaces by one to the new end.
            var element = Mounted("transition-all transition-discrete justify-start opacity-100 " + OneSecond);
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));
            RunAQuarterSecond(element);
            element.AddToClassList("transition-normal");
            ForcePanelUpdate(element.panel);
            Swap(element, ("justify-end", "justify-center"), ("opacity-0", "opacity-100"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.Center));
        }

        [Test]
        public void Given_AJustifyContentTransitionTransitionDiscreteLetRun_When_ItsEndChangesUnderTransitionNormalBeforeAnAnimationUpdate_Then_TheStylePassThatRestylesItLandsTheNewEnd()
        {
            // Arrange — no animation update between the two style passes, so UI Toolkit starts both transitions at one
            // time; the opacity the first starts is still running in the second.
            var element = Mounted("transition-all transition-discrete justify-start opacity-100 " + OneSecond);
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));
            ForcePanelUpdate(element.panel);
            Swap(element, ("transition-discrete", "transition-normal"), ("justify-end", "justify-center"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.Center));
        }

        [Test]
        public void Given_AnEndedTransitionDiscreteRunToFlexEnd_When_AnotherToFlexEndStartsUnderTransitionNormal_Then_TheStylePassThatRestylesItLandsFlexEnd()
        {
            // Arrange — the run ends a second and a quarter in, and justify-content leaves flex-end while no transition
            // of it is listed, so nothing decides on it between the two runs.
            var element = Mounted("transition-all transition-discrete justify-start opacity-100 " + OneSecond);
            Swap(element, "justify-start", "justify-end");
            RunAQuarterSecond(element);
            RunFor(element, 1.0);
            Swap(element, "transition-all", "transition-opacity");
            ForcePanelUpdate(element.panel);
            Swap(element, "justify-end", "justify-start");
            ForcePanelUpdate(element.panel);
            Swap(element, ("transition-opacity", "transition-all"), ("transition-discrete", "transition-normal"),
                ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_ATransitionDiscreteRunWhoseEndEventAHandlerStops_When_TheJustifyContentClassChangesUnderTransitionNormal_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange — the run ends a second and a quarter in, its end event stopped on the element, below the hold.
            var element = Mounted("transition-all transition-discrete justify-start opacity-100 " + OneSecond);
            element.RegisterCallback<TransitionEndEvent>(evt => evt.StopPropagation());
            Swap(element, "justify-start", "justify-end");
            RunAQuarterSecond(element);
            RunFor(element, 1.0);
            Swap(element, ("transition-discrete", "transition-normal"), ("justify-end", "justify-start"),
                ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexStart));
        }

        [Test]
        public void Given_ATransitionDiscreteRunAMoveUnderTheHoldCancels_When_TheJustifyContentClassChangesUnderTransitionNormal_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange — the move takes the element off its parent before off its panel, so the cancel UI Toolkit sends it
            // reaches no element above it.
            var element = Mounted("transition-all transition-discrete justify-start opacity-100 " + OneSecond);
            Swap(element, "justify-start", "justify-end");
            RunAQuarterSecond(element);
            var parent = element.hierarchy.parent;
            element.RemoveFromHierarchy();
            parent.Add(element);
            ForcePanelUpdate(element.panel);
            Swap(element, ("transition-discrete", "transition-normal"), ("justify-end", "justify-start"),
                ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexStart));
        }

        // GREEN_ON_BASE(characterization): the base stops no transition event, so the retarget's cancel reaches the element.
        // Removing the DropStale call from StyleTransitionBehavior.CancelUnlessAllowed reddens this case.
        [Test]
        public void Given_ARunEventCancelWhoseCancelEventWasSentOffThePanel_When_ATransitionDiscreteRunAfterItIsRetargeted_Then_TheElementHearsItsCancel()
        {
            // Arrange — the first write is cancelled at its run event a quarter second in, and the cancel event for it is
            // sent at the next update, while the element is off its panel; the second runs under transition-discrete.
            var element = Mounted("transition-all " + OneSecond);
            var parent = element.hierarchy.parent;
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);
            element.RemoveFromHierarchy();
            _now += 0.25;
            EditorPanelTestHelpers.DriveAnimationsOnce(parent.panel);
            parent.Add(element);
            ForcePanelUpdate(element.panel);
            element.AddToClassList("transition-discrete");
            ForcePanelUpdate(element.panel);
            var heard = On(element);
            element.style.justifyContent = Justify.Center;
            RunAQuarterSecond(element);
            element.style.justifyContent = Justify.FlexStart;

            // Act
            RunAQuarterSecond(element);

            // Assert
            Assert.That(heard.JustifyCancels, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base stops no transition event, so the element hears the later run's.
        // Removing the `stopped.RemoveAt(at)` from StyleTransitionBehavior.Stopped reddens this case.
        [Test]
        public void Given_AWatchedElementWhoseRunEventCancelWasHeardToItsCancelEvent_When_ItRunsATransitionUnderNoMount_Then_ItHearsItsRunAndStart()
        {
            // Arrange — the write's run is cancelled at the update a quarter second in, and its cancel event is sent at
            // the next; the element then moves to an element no tree is mounted on.
            var element = Mounted("transition-all " + OneSecond);
            element.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(element);
            RunAQuarterSecond(element);
            NewRoot().Add(element);
            ForcePanelUpdate(element.panel);
            var heard = On(element);
            element.style.justifyContent = Justify.Center;

            // Act
            RunAQuarterSecond(element);

            // Assert
            Assert.That(heard.Justify, Is.EqualTo(2));
        }

        [Test]
        public void Given_AnInlineWriteCancelledAtItsRunEvent_When_TheWriteIsClearedBeforeItsCancelEventIsSent_Then_TheElementHearsNoCancelEvent()
        {
            // Arrange — the write's run is cancelled at the update a quarter second in, and the next sends its cancel event.
            var element = Mounted("transition-all " + OneSecond);
            element.style.justifyContent = Justify.FlexEnd;
            var started = Runs(element);
            RunAQuarterSecond(element);
            var heard = On(element);
            element.style.justifyContent = StyleKeyword.Null;

            // Act — two updates, so that a cancel event a first one would queue is sent as well.
            RunAQuarterSecond(element);
            RunAQuarterSecond(element);

            // Assert — -1 where the write started no transition.
            Assert.That(started ? heard.JustifyCancels : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_AnElementTransitioningAll_When_ItsJustifyContentIsWrittenInline_Then_ItHearsNoTransitionEventForIt()
        {
            // Arrange — two updates, so that a cancel event a first one would queue is sent as well.
            var element = Mounted("transition-all " + OneSecond);
            var heard = On(element);
            element.style.justifyContent = Justify.FlexEnd;
            var started = Runs(element);

            // Act
            RunAQuarterSecond(element);
            RunAQuarterSecond(element);

            // Assert — -1 where the write started no transition.
            Assert.That(started ? heard.Justify : -1, Is.EqualTo(0));
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
            Swap(element, ("justify-end", "justify-center"), ("opacity-0", "opacity-100"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert — a third value, which the transition the change starts shows only once it has landed.
            Assert.That(landed, Is.EqualTo(Justify.Center));
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

        private int WatchSheetsOnThePanelRoot()
        {
            var sheets = _window.rootVisualElement.panel.visualTree.styleSheets;
            var count = 0;
            for (var i = 0; i < sheets.count; i++)
            {
                count += sheets[i] != null && sheets[i].name == Watch ? 1 : 0;
            }
            return count;
        }

        [Test]
        public void Given_ATreeMountedOnThePanel_When_ItIsDisposed_Then_ThePanelsRootCarriesNoWatchSheet()
        {
            // Arrange
            var mounted = V.Mount(NewRoot(), V.Div());
            var placed = WatchSheetsOnThePanelRoot() > 0;

            // Act
            mounted.Dispose();

            // Assert — -1 where the mount put no watch sheet there to take off.
            Assert.That(placed ? WatchSheetsOnThePanelRoot() : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_AMountRootOnThePanel_When_ItLeavesThePanel_Then_ThePanelsRootCarriesNoWatchSheet()
        {
            // Arrange
            var root = NewRoot();
            Mount(root, V.Div());
            var placed = WatchSheetsOnThePanelRoot() > 0;

            // Act
            root.RemoveFromHierarchy();

            // Assert — -1 where the mount put no watch sheet there to take off.
            Assert.That(placed ? WatchSheetsOnThePanelRoot() : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_AMountRootTakenOffThePanelAndPutBack_When_ItsElementsJustifyContentClassChanges_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange
            var root = NewRoot();
            Mount(root, V.Div(name: "leaf", className: "transition-all justify-start opacity-100 " + OneSecond));
            root.RemoveFromHierarchy();
            _window.rootVisualElement.Add(root);
            var element = Attached(root.Q<VisualElement>("leaf"));
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert
            Assert.That(landed, Is.EqualTo(Justify.FlexEnd));
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

        private const string Triangle = "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";

        private static string Clipped(string classes) => Triangle + " transition-all " + OneSecond + " " + classes;

        [Test]
        public void Given_AClippedElementTransitioningAll_When_ItsAlignSelfClassChanges_Then_TheStylePassThatRestylesItsWrapperLandsTheNewValue()
        {
            // Arrange — the clip wrapper takes the element's place in its parent's layout, align-self and width with
            // it, from the classes it mirrors.
            var root = NewRoot();
            var mounted = Mount(root, V.Div(name: "clipped", className: Clipped("self-start w-4")));
            var wrapper = Attached(root.Q<VisualElement>("clipped")).parent;
            mounted.Render(V.Div(name: "clipped", className: Clipped("self-end w-8")));

            // Act
            ForcePanelUpdate(wrapper.panel);

            // Assert — (Align)(-1) where no wrapper holds the element or the width it mirrors does not transition.
            Assert.That(wrapper.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass) && Runs(wrapper, "width")
                ? wrapper.resolvedStyle.alignSelf : (Align)(-1), Is.EqualTo(Align.FlexEnd));
        }

        [Test]
        public void Given_AnElementWithAnArbitraryDurationThatGainsAClipPath_When_ItsAlignSelfClassChanges_Then_TheStylePassThatRestylesItsWrapperLandsTheNewValue()
        {
            // Arrange — the duration is written inline on the element, and the wrapper takes a copy of it as it wraps the
            // element; no class gives either a transition.
            const string Timed = " ease-linear duration-[1000ms] ";
            var root = NewRoot();
            var mounted = Mount(root, V.Div(name: "clipped", className: Timed + "self-start w-4"));
            var element = Attached(root.Q<VisualElement>("clipped"));
            mounted.Render(V.Div(name: "clipped", className: Triangle + Timed + "self-start w-4"));
            var wrapper = element.parent;
            ForcePanelUpdate(wrapper.panel);
            mounted.Render(V.Div(name: "clipped", className: Triangle + Timed + "self-end w-8"));

            // Act
            ForcePanelUpdate(wrapper.panel);

            // Assert — (Align)(-1) where no wrapper holds the element or the width it mirrors does not transition.
            Assert.That(wrapper.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass) && Runs(wrapper, "width")
                ? wrapper.resolvedStyle.alignSelf : (Align)(-1), Is.EqualTo(Align.FlexEnd));
        }

        private static VNode LabelIn(string name, string classes)
            => V.Div(children: new VNode[] { V.Label(name: name, text: name, className: classes + " " + OneSecond) });

        [Test]
        public void Given_APooledLabelTornDownAsItsTransitionDiscreteRunStarts_When_ItsNextConsumersTextAlignClassChangesWithNoTimePassing_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange — no time passes between the two runs, so the second starts when the first did, to the same end.
            VNodePoolTestAccess.ClearLabelPoolForTest();
            var root = NewRoot();
            var mounted = Mount(root, LabelIn("first", "transition-all transition-discrete text-left"));
            var first = Attached(root.Q<Label>("first"));
            mounted.Render(LabelIn("first", "transition-all transition-discrete text-right"));
            ForcePanelUpdate(first.panel);
            mounted.Render(V.Div());
            mounted.Render(LabelIn("second", "transition-all text-left opacity-100"));
            var second = root.Q<Label>("second");
            ForcePanelUpdate(second.panel);
            mounted.Render(LabelIn("second", "transition-all text-right opacity-0"));

            // Act
            ForcePanelUpdate(second.panel);

            // Assert — (TextAnchor)(-1) where the pool did not hand the label back or its opacity does not transition.
            Assert.That(ReferenceEquals(first, second) && Runs(second, "opacity")
                ? second.resolvedStyle.unityTextAlign : (TextAnchor)(-1), Is.EqualTo(TextAnchor.MiddleRight));
        }

        [Test]
        public void Given_APooledLabelTornDownBeforeTheCancelEventOfItsRunEventCancelIsSent_When_ItsNextConsumerIsRendered_Then_ThatConsumerHearsNoCancelEvent()
        {
            // Arrange — the write's run is cancelled at the update a quarter second in, and the label goes back to the pool
            // and out again before the next update sends the cancel event.
            VNodePoolTestAccess.ClearLabelPoolForTest();
            var root = NewRoot();
            var mounted = Mount(root, LabelIn("first", "transition-all"));
            var first = Attached(root.Q<Label>("first"));
            first.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(first);
            mounted.Render(V.Div());
            mounted.Render(LabelIn("second", "transition-all"));
            var second = root.Q<Label>("second");
            var heard = On(second);

            // Act
            RunAQuarterSecond(second);

            // Assert — -1 where the pool did not hand the label back.
            Assert.That(ReferenceEquals(first, second) ? heard.JustifyCancels : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_APooledLabelTornDownBeforeTheCancelEventOfItsRunEventCancelIsSent_When_ANextConsumerNothingTimesIsRendered_Then_ThatConsumerHearsNoCancelEvent()
        {
            // Arrange — as in the case before, but no class or value of the next consumer's gives it a transition, so
            // nothing watches it.
            VNodePoolTestAccess.ClearLabelPoolForTest();
            var root = NewRoot();
            var mounted = Mount(root, LabelIn("first", "transition-all"));
            var first = Attached(root.Q<Label>("first"));
            first.style.justifyContent = Justify.FlexEnd;
            RunAQuarterSecond(first);
            mounted.Render(V.Div());
            mounted.Render(V.Div(children: new VNode[] { V.Label(name: "second", text: "second") }));
            var second = root.Q<Label>("second");
            var heard = On(second);

            // Act
            RunAQuarterSecond(second);

            // Assert — -1 where the pool did not hand the label back.
            Assert.That(ReferenceEquals(first, second) ? heard.JustifyCancels : -1, Is.EqualTo(0));
        }

        // Binds a delegate that throws in place of one of RunningStyleAnimations' reads, until TearDown puts the bound
        // one back; false where there is no such read, which RunningStyleAnimations is reached by name to allow.
        private bool Throwing(string field, Delegate throwing)
        {
            var read = typeof(V).Assembly.GetType("Velvet.RunningStyleAnimations")
                ?.GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
            if (read == null)
            {
                return false;
            }
            _reads.Add((read, read.GetValue(null)));
            read.SetValue(null, throwing);
            return true;
        }

        [Test]
        public void Given_ARunningAnimationFlagThatThrows_When_AWatchedElementsJustifyContentClassChanges_Then_TheNextAnimationUpdateLandsTheNewValue()
        {
            // Arrange
            var element = Mounted("transition-all justify-start " + OneSecond);
            var replaced = Throwing("s_any", (Func<VisualElement, bool>)(_ => throw new InvalidOperationException()));
            Swap(element, "justify-start", "justify-end");

            // Act
            var landed = AfterTheNextAnimationUpdate(element);

            // Assert — NoneRan where there was no read to replace.
            Assert.That(replaced ? landed : NoneRan, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_ARunningAnimationTestThatThrows_When_AWatchedElementsJustifyContentClassChanges_Then_TheStylePassThatRestylesItLandsTheNewValue()
        {
            // Arrange
            var element = Mounted("transition-all justify-start opacity-100 " + OneSecond);
            var replaced = Throwing("s_of", (Func<VisualElement, int, bool>)((_, _) => throw new InvalidOperationException()));
            Swap(element, ("justify-start", "justify-end"), ("opacity-100", "opacity-0"));

            // Act
            var landed = AfterTheStylePass(element);

            // Assert — NoneRan where there was no read to replace.
            Assert.That(replaced ? landed : NoneRan, Is.EqualTo(Justify.FlexEnd));
        }

        [Test]
        public void Given_AWatchedElementATransitionRunsOn_When_AClassNoRuleNamesToggles_Then_ItsStylePassesAllocateAsAnUnwatchedOnesDo()
        {
            // Arrange — both run an opacity transition under transition-property `all`, the watched one's given by
            // utilities and the other's by an application rule, which watches nothing before the element's first run
            // event. `all` on both, since a restyle under it allocates where one under `opacity` alone does not (the
            // case after this one).
            AttachSheet(".app-all { transition-property: all; transition-duration: 1s; transition-timing-function: linear; }");
            var watchedElement = Mounted("transition-all opacity-100 " + OneSecond);
            var unwatchedElement = Mounted("app-all opacity-100");
            Swap(watchedElement, "opacity-100", "opacity-0");
            Swap(unwatchedElement, "opacity-100", "opacity-0");
            ForcePanelUpdate(watchedElement.panel);
            var watched = Toggled(watchedElement);
            var unwatched = Toggled(unwatchedElement);
            var apart = Runs(watchedElement, "opacity") && Runs(unwatchedElement, "opacity")
                && CallbackRegistryProbe.TrickleDownCallbackCount(watchedElement)
                > CallbackRegistryProbe.TrickleDownCallbackCount(unwatchedElement);
            var canary = GCAllocationProbe.MedianBlocksDuring(() => s_sink = new object());

            // Act
            var extra = GCAllocationProbe.MedianBlocksDuring(watched) - GCAllocationProbe.MedianBlocksDuring(unwatched);

            // Assert — -1 where the watch is not what sets the two apart, or where the probe sees no allocation at all.
            Assert.That(canary > 0 && apart ? extra : -1, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the engine's restyle under `all` is the base's as well.
        // Nothing in Velvet reddens this case: it fails when a restyle UI Toolkit makes under `all` of an element running
        // a transition stops allocating more than one under `opacity` alone.
        [Test]
        public void Given_TwoUnwatchedElementsRunningAnOpacityTransition_When_AClassNoRuleNamesToggles_Then_TheOneUnderTransitionPropertyAllAllocatesMore()
        {
            // Arrange — application rules, which watch nothing before an element's first run event.
            AttachSheet(".app-all { transition-property: all; transition-duration: 1s; transition-timing-function: linear; }"
                + " .app-fade { transition-property: opacity; transition-duration: 1s; transition-timing-function: linear; }");
            var underAll = Mounted("app-all opacity-100");
            var underOpacity = Mounted("app-fade opacity-100");
            Swap(underAll, "opacity-100", "opacity-0");
            Swap(underOpacity, "opacity-100", "opacity-0");
            ForcePanelUpdate(underAll.panel);
            var all = Toggled(underAll);
            var opacity = Toggled(underOpacity);
            var running = Runs(underAll, "opacity") && Runs(underOpacity, "opacity");

            // Act
            var extra = GCAllocationProbe.MedianBlocksDuring(all) - GCAllocationProbe.MedianBlocksDuring(opacity);

            // Assert — -1 where either transition is not running.
            Assert.That(running ? extra : -1, Is.GreaterThan(0));
        }

        [Component]
        private static VNode LeafHost() => V.Div(children: new VNode[]
        {
            Hooks.UseStore(s_shown, shown => shown) ? V.Div(name: "leaf", className: "transition-all " + OneSecond) : null,
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

        [Test]
        public void Given_AnElementWhoseRunEventCancelHadItsCancelEventStopped_When_ItIsRemoved_Then_ItKeepsNoStyleCallbackOnItsWayDown()
        {
            // Arrange — the write's run is cancelled at the update a quarter second in, and its cancel event, sent at the
            // next, stopped.
            s_shown = new Shown();
            var root = NewRoot();
            var mounted = Mount(root, V.Component(LeafHost, key: "host"));
            var leaf = Attached(root.Q<VisualElement>("leaf"));
            leaf.style.justifyContent = Justify.FlexEnd;
            var started = Runs(leaf);
            RunAQuarterSecond(leaf);
            RunAQuarterSecond(leaf);
            var watched = CallbackRegistryProbe.TrickleDownCallbackCount(leaf) > 0;

            // Act
            s_shown.Hide();
            mounted.FlushStateForTest();

            // Assert — -1 where the write started no transition or nothing registered one while the element was rendered.
            Assert.That(started && watched ? CallbackRegistryProbe.TrickleDownCallbackCount(leaf) : -1, Is.EqualTo(0));
        }

        [Component]
        private static VNode FieldHost() => V.Div(children: new VNode[]
        {
            Hooks.UseStore(s_shown, shown => shown) ? V.TextField(name: "field", className: "transition-all") : null,
        });

        [Test]
        public void Given_ATextFieldWhoseInputATransitionUtilityGaveATransition_When_TheFieldIsRemoved_Then_ItsInputIsNoLongerMarked()
        {
            // Arrange — the input keeps its own classes through the pool (FiberTextFieldPoolHelper).
            s_shown = new Shown();
            var root = NewRoot();
            var mounted = Mount(root, V.Component(FieldHost, key: "host"));
            var input = root.Q<TextField>("field").Q<VisualElement>(className: StyleChildVariantClass.InputBoxClass);
            var marked = input.ClassListContains(Watch);

            // Act
            s_shown.Hide();
            mounted.FlushStateForTest();

            // Assert — null where the input was not marked while the field was rendered.
            Assert.That(marked ? input.ClassListContains(Watch) : (bool?)null, Is.False);
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
