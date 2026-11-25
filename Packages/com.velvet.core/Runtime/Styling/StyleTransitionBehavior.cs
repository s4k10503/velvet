#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// CSS's <c>transition-behavior</c>, which UI Toolkit lacks. A transition UI Toolkit starts for one of
    /// <see cref="LandedAtOnce"/> on an element under a <see cref="Hold"/> is cancelled, which lands the new value,
    /// where CSS's default <c>normal</c> changes such a property at once, unless <c>transition-discrete</c> outranks
    /// <c>transition-normal</c> on the element when it starts. TransitionDiscretePanelTests fails when a cancel stops
    /// landing the value. A transition of visibility or background-position under a hold, and the end or cancel of one,
    /// go to <see cref="StyleTransitionInterpolation"/>.
    /// </summary>
    /// <remarks>
    /// A transition a style pass starts on an element <see cref="Watch"/> marks is cancelled in that pass, so the frame
    /// it paints shows the new value and no transition event is sent for it
    /// (TransitionDiscretePanelTests' Given_AnElementTransitioningAll_When_ItsJustifyContentClassChanges cases). One an
    /// inline write starts on such an element is cancelled from its run event at the panel's next animation update, by a
    /// trickle-down handler of the element's own, which stops the run, start and cancel events ahead of the element's
    /// bubbling listeners
    /// (Given_AnElementTransitioningAll_When_ItsJustifyContentIsWrittenInline_Then_ItHearsNoTransitionEventForIt).
    /// One on an element nothing has marked yet is cancelled from its run event where that reaches the hold, which marks
    /// the element.
    /// </remarks>
    internal static class StyleTransitionBehavior
    {
        internal const string Discrete = "transition-discrete";
        internal const string Normal = "transition-normal";

        internal const string WatchClass = "velvet-transition-watch";
        private const string WatchProperty = "--velvet-transition-watch";

        /// <summary>
        /// The longhands UI Toolkit only switches partway through a transition, but for visibility and
        /// background-position, which CSS transitions under <c>normal</c>, and the font properties, <c>-unity-font</c>
        /// among them, which this leaves to switch as before: a weight comes through <c>-unity-font-definition</c> and
        /// <c>-unity-font-style</c> as a family does, and CSS interpolates a weight. TransitionLandedAtOnceSetTests
        /// derives the set from the engine and fails when this one differs.
        /// </summary>
        internal static readonly string[] LandedAtOnce =
        {
            "align-content", "align-items", "align-self", "background-image", "background-repeat", "flex-direction",
            "flex-wrap", "justify-content", "overflow", "position", "text-overflow", "-unity-overflow-clip-box",
            "-unity-slice-type", "-unity-text-align", "-unity-text-overflow-position", "white-space",
        };

        private sealed class Listener
        {
            public int Holds;
            // The root of the panel the held element is on, which holds the watch sheet while it is.
            public VisualElement? SheetHost;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Listener> s_listeners =
            new();
        private static readonly EventCallback<TransitionRunEvent> s_onRun = OnRun;
        private static readonly EventCallback<TransitionRunEvent> s_onRunHere = OnRunHere;
        private static readonly EventCallback<TransitionStartEvent> s_onStartHere = OnStartHere;
        private static readonly EventCallback<TransitionCancelEvent> s_onCancelHere = OnCancelHere;
        private static readonly EventCallback<TransitionEndEvent> s_onEnd = OnEnd;
        private static readonly EventCallback<TransitionCancelEvent> s_onCancel = OnCancel;
        private static readonly EventCallback<CustomStyleResolvedEvent> s_onRestyled = OnRestyled;
        private static readonly EventCallback<AttachToPanelEvent, Listener> s_onAttached =
            static (evt, listener) => Place(listener, evt.destinationPanel);
        private static readonly EventCallback<DetachFromPanelEvent, Listener> s_onDetached =
            static (_, listener) => Unplace(listener);
        private static readonly Action<VisualElement, StylePropertyName> s_ended = StyleTransitionInterpolation.Ended;
        private static readonly Action<VisualElement, StylePropertyName> s_cancelled = StyleTransitionInterpolation.Cancelled;
        private static readonly MethodInfo? s_cancel = EngineMember.CancelStyleAnimation.ResolveMethod();
        private static readonly MethodInfo? s_runs = EngineMember.HasRunningStyleAnimation.ResolveMethod();

        // The engine's id of each of LandedAtOnce, boxed as the reflected calls take it and as the int
        // RunningStyleAnimations.Of takes; none where an engine member does not resolve, which leaves UI Toolkit's
        // transitions as they run.
        private static readonly Dictionary<StylePropertyName, object?[]> s_cancelArguments = new();
        private static readonly List<(StylePropertyName Name, object?[] Arguments, int Id)> s_landedIds = new();

        private static readonly StyleLonghandSet s_timing =
            StyleLonghandSet.Of(StyleLonghand.TransitionDuration).Union(StyleLonghandSet.Of(StyleLonghand.TransitionDelay));

        // The transitions of LandedAtOnce left running because transition-discrete applied when they started, each by its
        // start time and end value (StyleTransitionInterpolation.Running) rather than until its end or cancel event, which
        // need not reach the hold
        // (Given_ATransitionDiscreteRunWhoseEndEventAHandlerStops_When_TheJustifyContentClassChangesUnderTransitionNormal_Then_TheStylePassThatRestylesItLandsTheNewValue,
        // Given_ATransitionDiscreteRunAMoveUnderTheHoldCancels_When_TheJustifyContentClassChangesUnderTransitionNormal_Then_TheStylePassThatRestylesItLandsTheNewValue).
        // A later change of transition-behavior leaves the one decided running, as CSS does.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement,
            List<(StylePropertyName Name, double Start, object? End)>> s_allowed = new();

        // The transitions cancelled from their run event, whose start and cancel events are stopped too, with UI Toolkit's
        // time for the animation update the cancel came in. An entry from an earlier update is dropped at a later decision
        // on its property, so that one whose cancel event never reached the element stops none of the events of the
        // transition decided there
        // (Given_ARunEventCancelWhoseCancelEventWasSentOffThePanel_When_ATransitionDiscreteRunAfterItIsRetargeted_Then_TheElementHearsItsCancel).
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement,
            List<(StylePropertyName Name, double At)>> s_stopped = new();

        // For each panel's root, how many held roots on that panel have it hold the watch sheet.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, int[]> s_sheetHolds =
            new();

        private static StyleSheet? s_watchSheet;

        static StyleTransitionBehavior()
        {
            var id = EngineMember.StylePropertyNameId.ResolveProperty();
            if (s_cancel == null || id == null)
            {
                return;
            }
            foreach (var longhand in LandedAtOnce)
            {
                var name = new StylePropertyName(longhand);
                try
                {
                    var boxed = id.GetValue(name);
                    var arguments = new[] { boxed };
                    s_cancelArguments[name] = arguments;
                    s_landedIds.Add((name, arguments, Convert.ToInt32(boxed)));
                }
                catch (Exception exception) when (exception is TargetInvocationException or TargetException
                    or ArgumentException or MemberAccessException or NotSupportedException or InvalidCastException)
                {
                }
            }
        }

        internal static bool IsBehaviourClass(string core) => core == Discrete || core == Normal;

        /// <summary>
        /// Whether transition-discrete outranks transition-normal on the element: the one held at the higher layer
        /// key, a play's included, and transition-normal where the keys tie, as Tailwind emits it after
        /// transition-discrete.
        /// </summary>
        internal static bool AllowsDiscrete(VisualElement element)
        {
            var discrete = HighestKey(element, Discrete);
            if (discrete == null)
            {
                return false;
            }
            var normal = HighestKey(element, Normal);
            return normal == null || discrete > normal;
        }

        /// <summary>
        /// Listens on <paramref name="root"/> for the transitions UI Toolkit starts in its subtree, until every hold
        /// taken on it is disposed, and has the root of each panel the root is on hold the watch sheet meanwhile.
        /// </summary>
        internal static IDisposable Hold(VisualElement root)
        {
            var listener = s_listeners.GetValue(root, static _ => new Listener());
            if (listener.Holds++ == 0)
            {
                root.RegisterCallback(s_onRun);
                root.RegisterCallback(s_onEnd);
                root.RegisterCallback(s_onCancel);
                root.RegisterCallback(s_onAttached, listener);
                root.RegisterCallback(s_onDetached, listener);
                Place(listener, root.panel);
            }
            return new Release(root, listener);
        }

        // No guard against a second Dispose: its holders are DefaultFamilyRoots' and give it back as they give back
        // theirs.
        private sealed class Release : IDisposable
        {
            private readonly VisualElement _root;
            private readonly Listener _listener;

            public Release(VisualElement root, Listener listener)
            {
                _root = root;
                _listener = listener;
            }

            public void Dispose()
            {
                if (--_listener.Holds == 0)
                {
                    _root.UnregisterCallback(s_onRun);
                    _root.UnregisterCallback(s_onEnd);
                    _root.UnregisterCallback(s_onCancel);
                    _root.UnregisterCallback(s_onAttached);
                    _root.UnregisterCallback(s_onDetached);
                    Unplace(_listener);
                }
            }
        }

        // Once on the panel's root rather than on each watched element, so that a descendant of several watched elements
        // has the sheet once among its ancestors'. Not on the held element itself: a layer's root is one, and the sheets
        // it carries are an application's.
        private static void Place(Listener listener, IPanel? panel)
        {
            if (listener.SheetHost != null)
            {
                return;
            }
            if (panel?.visualTree is not { } host || WatchSheet() is not { } sheet)
            {
                return;
            }
            var holds = s_sheetHolds.GetValue(host, static _ => new int[1]);
            if (holds[0]++ == 0)
            {
                host.styleSheets.Add(sheet);
            }
            listener.SheetHost = host;
        }

        private static void Unplace(Listener listener)
        {
            if (listener.SheetHost is not { } host)
            {
                return;
            }
            listener.SheetHost = null;
            if (s_sheetHolds.TryGetValue(host, out var holds) && --holds[0] == 0 && s_watchSheet != null)
            {
                host.styleSheets.Remove(s_watchSheet);
            }
        }

        /// <summary>
        /// Watches an element a class whose bundled rule declares a transition duration or delay is written on: by
        /// StyleClassProjection and StyleAnimationClassUtils, and by the writers that copy one element's classes onto
        /// another (ClipPathLayoutBox's wrapper, SelectionTextOverlay's drawing, VelvetStyleUtilities' host roots).
        /// </summary>
        internal static void TrackClass(VisualElement element, string cls)
        {
            if (StyleUtilityProperties.TryGet(StyleArbitraryValueResolver.StripImportant(cls, out _), out var rule)
                && rule.Properties.Overlaps(s_timing))
            {
                Watch(element);
            }
        }

        /// <summary>
        /// Marks the element so that a transition a style pass starts on it is cancelled in that pass: WatchClass, whose
        /// rule the watch sheet holds (MarkerSheets), as FiberFontScope watches an inheritor. TrackClass, a
        /// <c>duration-[…]</c> write, a Tween's timing and the clip wrapper's copy of a duration call it, and so does a
        /// run event under a hold, for an element none of them reached.
        /// </summary>
        internal static void Watch(VisualElement element)
        {
            if (WatchSheet() == null)
            {
                return;
            }
            element.RegisterCallback(s_onRestyled, TrickleDown.TrickleDown);
            element.RegisterCallback(s_onRunHere, TrickleDown.TrickleDown);
            element.RegisterCallback(s_onStartHere, TrickleDown.TrickleDown);
            element.RegisterCallback(s_onCancelHere, TrickleDown.TrickleDown);
            if (!element.ClassListContains(WatchClass))
            {
                element.AddToClassList(WatchClass);
            }
        }

        /// <summary>
        /// The element is torn down (FiberElementCleaner), its field's input box with it, so a pooled element carries
        /// no transition-discrete decision into its next use, and of its watch only what Forget keeps.
        /// </summary>
        internal static void Unwatch(VisualElement element)
        {
            Forget(element);
            if (StyleInputBoxSurface.IsControl(element) && StyleInputBoxSurface.TryGetBox(element, out var box))
            {
                Forget(box);
            }
        }

        // The stopped entries are kept, and while any is the handler that stops their cancel events, so that the next
        // consumer of a pooled element, whether or not anything watches it, hears no cancel event of a run OnRunHere
        // cancelled before the pool handed it out
        // (Given_APooledLabelTornDownBeforeTheCancelEventOfItsRunEventCancelIsSent_When_ItsNextConsumerIsRendered_Then_ThatConsumerHearsNoCancelEvent,
        // Given_APooledLabelTornDownBeforeTheCancelEventOfItsRunEventCancelIsSent_When_ANextConsumerNothingTimesIsRendered_Then_ThatConsumerHearsNoCancelEvent).
        private static void Forget(VisualElement element)
        {
            s_allowed.Remove(element);
            element.UnregisterCallback(s_onRestyled, TrickleDown.TrickleDown);
            element.UnregisterCallback(s_onRunHere, TrickleDown.TrickleDown);
            element.UnregisterCallback(s_onStartHere, TrickleDown.TrickleDown);
            if (!s_stopped.TryGetValue(element, out var stopped) || stopped.Count == 0)
            {
                element.UnregisterCallback(s_onCancelHere, TrickleDown.TrickleDown);
            }
            element.RemoveFromClassList(WatchClass);
        }

        private static StyleSheet? WatchSheet()
            => s_cancelArguments.Count == 0 ? null : s_watchSheet ??= MarkerSheets.Build(WatchClass, WatchProperty);

        // Sent once the element's transitions have started and before its children are styled, so what a cancel here
        // lands is what they inherit, once the hash they are cached under follows it
        // (Given_AContainerTransitioningAll_When_ItsWhiteSpaceClassChanges_Then_ItsChildInheritsTheNewValueInThatStylePass).
        // Sent in every style pass restyling a watched element, so the element's one running-animation flag is read
        // first, through RunningStyleAnimations.Any, which the editor's suites measure adding no allocation to such a pass
        // (Given_AWatchedElementATransitionRunsOn_When_AClassNoRuleNamesToggles_Then_ItsStylePassesAllocateAsAnUnwatchedOnesDo).
        // Where it answers null the cancel waits for the run event, rather than allocating on each pass
        // (Given_ARunningAnimationFlagThatThrows_When_AWatchedElementsJustifyContentClassChanges_Then_TheNextAnimationUpdateLandsTheNewValue).
        private static void OnRestyled(CustomStyleResolvedEvent evt)
        {
            if (evt.currentTarget is not VisualElement element || RunningStyleAnimations.Any(element) is not true || !Held(element))
            {
                return;
            }
            foreach (var (name, arguments, id) in s_landedIds)
            {
                if (Runs(element, arguments, id) && CancelUnlessAllowed(element, name))
                {
                    InheritedStyleHash.Refresh(element);
                }
            }
        }

        private static bool Held(VisualElement element)
        {
            for (var up = element; up != null; up = up.hierarchy.parent)
            {
                if (s_listeners.TryGetValue(up, out var listener) && listener.Holds > 0)
                {
                    return true;
                }
            }
            return false;
        }

        // The first of OnRestyled, OnRunHere and OnRun to reach a transition decides it: a cancel unless transition-discrete
        // applies then. Where it applies, the transition's start and end are kept, and an ask about a transition with
        // both is answered as that one was until an ask about one without them.
        private static bool CancelUnlessAllowed(VisualElement element, StylePropertyName name)
        {
            DropStale(element, name);
            var at = s_allowed.TryGetValue(element, out var allowed) ? allowed.FindIndex(entry => entry.Name == name) : -1;
            if (at >= 0)
            {
                if (StyleTransitionInterpolation.Running(element, name) is { } running && running.Start == allowed![at].Start
                    && Equals(running.End, allowed[at].End))
                {
                    return false;
                }
                allowed!.RemoveAt(at);
            }
            if (AllowsDiscrete(element))
            {
                // One whose start and end cannot be read is decided again at each ask, as it was before any was recorded.
                if (StyleTransitionInterpolation.Running(element, name) is { } running)
                {
                    s_allowed.GetValue(element, static _ => new List<(StylePropertyName, double, object?)>())
                        .Add((name, running.Start, running.End));
                }
                return false;
            }
            try
            {
                s_cancel!.Invoke(element, s_cancelArguments[name]);
            }
            catch (Exception exception) when (exception is TargetInvocationException or TargetException
                or ArgumentException or MemberAccessException or NotSupportedException)
            {
                // MUTANT_SURVIVES(unreachable, literal): reached only where the engine's cancel throws, which nothing the suites drive makes it do.
                return false;
            }
            return true;
        }

        private static void DropStale(VisualElement element, StylePropertyName name)
        {
            if (!s_stopped.TryGetValue(element, out var stopped) || stopped.Count == 0)
            {
                return;
            }
            var now = StyleTransitionInterpolation.Now(element);
            stopped.RemoveAll(entry => entry.Name == name && entry.At < now);
        }

        private static void OnRunHere(TransitionRunEvent evt)
        {
            if (evt.currentTarget is not VisualElement element || evt.target != element || !Held(element))
            {
                return;
            }
            var cancelled = false;
            foreach (var name in evt.stylePropertyNames)
            {
                if (s_cancelArguments.ContainsKey(name) && CancelUnlessAllowed(element, name))
                {
                    s_stopped.GetValue(element, static _ => new List<(StylePropertyName, double)>())
                        .Add((name, StyleTransitionInterpolation.Now(element)));
                    cancelled = true;
                }
            }
            if (cancelled)
            {
                evt.StopImmediatePropagation();
            }
        }

        private static void OnStartHere(TransitionStartEvent evt)
        {
            if (evt.currentTarget is VisualElement element && evt.target == element
                && Stopped(element, evt.stylePropertyNames, release: false))
            {
                evt.StopImmediatePropagation();
            }
        }

        private static void OnCancelHere(TransitionCancelEvent evt)
        {
            if (evt.currentTarget is VisualElement element && evt.target == element
                && Stopped(element, evt.stylePropertyNames, release: true))
            {
                evt.StopImmediatePropagation();
            }
        }

        private static bool Stopped(VisualElement element, StylePropertyNameCollection names, bool release)
        {
            if (!s_stopped.TryGetValue(element, out var stopped) || stopped.Count == 0)
            {
                return false;
            }
            var found = false;
            foreach (var name in names)
            {
                var at = stopped.FindIndex(entry => entry.Name == name);
                if (at < 0)
                {
                    continue;
                }
                found = true;
                if (release)
                {
                    stopped.RemoveAt(at);
                }
            }
            return found;
        }

        private static void OnEnd(TransitionEndEvent evt) => Forward(evt.target, evt.stylePropertyNames, s_ended);

        private static void OnCancel(TransitionCancelEvent evt) => Forward(evt.target, evt.stylePropertyNames, s_cancelled);

        private static void Forward(IEventHandler target, StylePropertyNameCollection names,
            Action<VisualElement, StylePropertyName> to)
        {
            if (target is not VisualElement element)
            {
                return;
            }
            foreach (var name in names)
            {
                to(element, name);
            }
        }

        private static void OnRun(TransitionRunEvent evt)
        {
            if (evt.target is not VisualElement element)
            {
                return;
            }
            Watch(element);
            foreach (var name in evt.stylePropertyNames)
            {
                // Whether transition-discrete applies or not, as allow-discrete changes neither in CSS.
                if (StyleTransitionInterpolation.Interpolates(name))
                {
                    StyleTransitionInterpolation.Take(element, name);
                }
                else if (s_cancelArguments.ContainsKey(name))
                {
                    CancelUnlessAllowed(element, name);
                }
            }
        }

        // Through RunningStyleAnimations.Of, and by invoking the method, which boxes what it returns, only where that
        // answers null (Given_ARunningAnimationTestThatThrows_When_AWatchedElementsJustifyContentClassChanges_Then_TheStylePassThatRestylesItLandsTheNewValue).
        private static bool Runs(VisualElement element, object?[] arguments, int id)
        {
            if (RunningStyleAnimations.Of(element, id) is { } runs)
            {
                return runs;
            }
            try
            {
                return s_runs != null && s_runs.Invoke(element, arguments) is true;
            }
            catch (Exception exception) when (exception is TargetInvocationException or TargetException
                or ArgumentException or MemberAccessException or NotSupportedException)
            {
                // MUTANT_SURVIVES(unreachable, literal): reached only where the engine's HasRunningAnimation throws, which nothing the suites drive makes it do.
                return false;
            }
        }

        // Without a model no payload above the base key was ever applied, so a class on the list is held there.
        private static long? HighestKey(VisualElement element, string cls)
        {
            if (StyleArbitraryValueResolver.TryGetProjection(element) is { } model)
            {
                return model.HighestKey(cls);
            }
            return element.ClassListContains(cls) ? StyleLayerPriority.Base : (long?)null;
        }
    }
}
