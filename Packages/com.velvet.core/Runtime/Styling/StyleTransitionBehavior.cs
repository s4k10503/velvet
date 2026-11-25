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
    /// (TransitionDiscretePanelTests' Given_AnElementTransitioningAll_When_ItsJustifyContentClassChanges cases). Any
    /// other, one an inline write starts or one on an element nothing has marked yet, is cancelled from its run event
    /// at the panel's next animation update, and its run, start and cancel events stop at the hold
    /// (Given_AnElementTransitioningAll_When_ItsJustifyContentIsWrittenInline_Then_ItHearsNoTransitionEventForIt).
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
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Listener> s_listeners =
            new();
        private static readonly EventCallback<TransitionRunEvent> s_onRun = OnRun;
        private static readonly EventCallback<TransitionRunEvent> s_onRunFirst = OnRunFirst;
        private static readonly EventCallback<TransitionStartEvent> s_onStartFirst = OnStartFirst;
        private static readonly EventCallback<TransitionCancelEvent> s_onCancelFirst = OnCancelFirst;
        private static readonly EventCallback<TransitionEndEvent> s_onEnd = OnEnd;
        private static readonly EventCallback<TransitionCancelEvent> s_onCancel = OnCancel;
        private static readonly EventCallback<CustomStyleResolvedEvent> s_onRestyled = OnRestyled;
        private static readonly Action<VisualElement, StylePropertyName> s_ended = StyleTransitionInterpolation.Ended;
        private static readonly Action<VisualElement, StylePropertyName> s_cancelled = StyleTransitionInterpolation.Cancelled;
        private static readonly MethodInfo? s_cancel = EngineMember.CancelStyleAnimation.ResolveMethod();
        private static readonly MethodInfo? s_runs = EngineMember.HasRunningStyleAnimation.ResolveMethod();
        private static readonly Func<VisualElement, bool>? s_runsAny =
            EngineMember.HasRunningStyleAnimations.ResolveGetter<Func<VisualElement, bool>>();

        // The engine's id of each of LandedAtOnce, boxed as the reflected calls take it and as the int the compiled run
        // check takes; none where an engine member does not resolve, which leaves UI Toolkit's transitions as they run.
        private static readonly Dictionary<StylePropertyName, object?[]> s_cancelArguments = new();
        private static readonly List<(StylePropertyName Name, object?[] Arguments, int Id)> s_landedIds = new();

        private static readonly StyleLonghandSet s_timing =
            StyleLonghandSet.Of(StyleLonghand.TransitionDuration).Union(StyleLonghandSet.Of(StyleLonghand.TransitionDelay));

        // The transitions of LandedAtOnce left running because transition-discrete applied when they started, so that a
        // later change of transition-behavior leaves them running, as CSS does. An entry goes when the hold hears the
        // transition end or cancel, or with the element's teardown.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, List<StylePropertyName>>
            s_allowed = new();

        // The transitions cancelled from their run event, whose start and cancel events are stopped too.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, List<StylePropertyName>>
            s_stopped = new();

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
        /// taken on it is disposed.
        /// </summary>
        internal static IDisposable Hold(VisualElement root)
        {
            var listener = s_listeners.GetValue(root, static _ => new Listener());
            if (listener.Holds++ == 0)
            {
                root.RegisterCallback(s_onRunFirst, TrickleDown.TrickleDown);
                root.RegisterCallback(s_onStartFirst, TrickleDown.TrickleDown);
                root.RegisterCallback(s_onCancelFirst, TrickleDown.TrickleDown);
                root.RegisterCallback(s_onRun);
                root.RegisterCallback(s_onEnd);
                root.RegisterCallback(s_onCancel);
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
                    _root.UnregisterCallback(s_onRunFirst, TrickleDown.TrickleDown);
                    _root.UnregisterCallback(s_onStartFirst, TrickleDown.TrickleDown);
                    _root.UnregisterCallback(s_onCancelFirst, TrickleDown.TrickleDown);
                    _root.UnregisterCallback(s_onRun);
                    _root.UnregisterCallback(s_onEnd);
                    _root.UnregisterCallback(s_onCancel);
                }
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
        /// Marks the element so that a transition a style pass starts on it is cancelled in that pass: WatchClass and
        /// the sheet holding its rule (MarkerSheets), as FiberFontScope watches an inheritor. TrackClass, a
        /// <c>duration-[…]</c> write and a Tween's timing call it, and so does a run event under a hold, for an element
        /// none of them reached.
        /// </summary>
        internal static void Watch(VisualElement element)
        {
            var sheet = WatchSheet();
            if (sheet == null)
            {
                return;
            }
            element.RegisterCallback(s_onRestyled, TrickleDown.TrickleDown);
            if (!element.ClassListContains(WatchClass))
            {
                element.AddToClassList(WatchClass);
            }
            if (!element.styleSheets.Contains(sheet))
            {
                element.styleSheets.Add(sheet);
            }
        }

        /// <summary>
        /// The element is torn down (FiberElementCleaner), its field's input box with it, so a pooled element carries
        /// no watch or decision into its next use.
        /// </summary>
        internal static void Unwatch(VisualElement element)
        {
            Forget(element);
            if (StyleInputBoxSurface.IsControl(element) && StyleInputBoxSurface.TryGetBox(element, out var box))
            {
                Forget(box);
            }
        }

        private static void Forget(VisualElement element)
        {
            s_allowed.Remove(element);
            s_stopped.Remove(element);
            element.UnregisterCallback(s_onRestyled, TrickleDown.TrickleDown);
            element.RemoveFromClassList(WatchClass);
            if (s_watchSheet != null)
            {
                element.styleSheets.Remove(s_watchSheet);
            }
        }

        private static StyleSheet? WatchSheet()
            => s_cancelArguments.Count == 0 ? null : s_watchSheet ??= MarkerSheets.Build(WatchClass, WatchProperty);

        // Sent once the element's transitions have started and before its children are styled, so what a cancel here
        // lands is what they inherit, once the hash they are cached under follows it
        // (Given_AContainerTransitioningAll_When_ItsWhiteSpaceClassChanges_Then_ItsChildInheritsTheNewValueInThatStylePass).
        private static void OnRestyled(CustomStyleResolvedEvent evt)
        {
            if (evt.currentTarget is not VisualElement element || !RunsAny(element) || !Held(element))
            {
                return;
            }
            var landed = false;
            foreach (var (name, arguments, id) in s_landedIds)
            {
                landed |= Runs(element, arguments, id) && CancelUnlessAllowed(element, name);
            }
            if (landed)
            {
                InheritedStyleHash.Refresh(element);
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

        // Decided once, where the transition starts: a cancel unless transition-discrete applies then.
        private static bool CancelUnlessAllowed(VisualElement element, StylePropertyName name)
        {
            if (s_allowed.TryGetValue(element, out var allowed) && allowed.Contains(name))
            {
                return false;
            }
            if (AllowsDiscrete(element))
            {
                s_allowed.GetValue(element, static _ => new List<StylePropertyName>()).Add(name);
                return false;
            }
            try
            {
                s_cancel!.Invoke(element, s_cancelArguments[name]);
            }
            catch (Exception exception) when (exception is TargetInvocationException or TargetException
                or ArgumentException or MemberAccessException or NotSupportedException)
            {
                return false;
            }
            return true;
        }

        private static void OnRunFirst(TransitionRunEvent evt)
        {
            if (evt.target is not VisualElement element)
            {
                return;
            }
            Watch(element);
            var cancelled = false;
            foreach (var name in evt.stylePropertyNames)
            {
                if (s_cancelArguments.ContainsKey(name) && CancelUnlessAllowed(element, name))
                {
                    s_stopped.GetValue(element, static _ => new List<StylePropertyName>()).Add(name);
                    cancelled = true;
                }
            }
            if (cancelled)
            {
                evt.StopImmediatePropagation();
            }
        }

        private static void OnStartFirst(TransitionStartEvent evt)
        {
            if (evt.target is VisualElement element && Stopped(element, evt.stylePropertyNames, release: false))
            {
                evt.StopImmediatePropagation();
            }
        }

        private static void OnCancelFirst(TransitionCancelEvent evt)
        {
            if (evt.target is VisualElement element && Stopped(element, evt.stylePropertyNames, release: true))
            {
                evt.StopImmediatePropagation();
            }
        }

        private static bool Stopped(VisualElement element, StylePropertyNameCollection names, bool release)
        {
            if (!s_stopped.TryGetValue(element, out var stopped))
            {
                return false;
            }
            var found = false;
            foreach (var name in names)
            {
                found |= release ? stopped.Remove(name) : stopped.Contains(name);
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
            s_allowed.TryGetValue(element, out var allowed);
            foreach (var name in names)
            {
                allowed?.Remove(name);
                to(element, name);
            }
        }

        private static void OnRun(TransitionRunEvent evt)
        {
            if (evt.target is not VisualElement element)
            {
                return;
            }
            foreach (var name in evt.stylePropertyNames)
            {
                // Whether transition-discrete applies or not, as allow-discrete changes neither in CSS.
                if (StyleTransitionInterpolation.Interpolates(name))
                {
                    StyleTransitionInterpolation.Take(element, name);
                }
            }
        }

        // Asked in every style pass restyling a watched element, so it reads through a delegate on either backend, which
        // allocates nothing (Given_AWatchedElement_When_WhetherAnyTransitionRunsOnItIsRead_Then_TheReadAllocatesNothing).
        // Where the runtime refuses the delegate it reads false, and the cancel waits for the run event, rather than
        // allocating on each pass.
        private static bool RunsAny(VisualElement element) => s_runsAny != null && s_runsAny(element);

        private static bool Runs(VisualElement element, object?[] arguments, int id)
        {
#if !ENABLE_IL2CPP
            if (s_compiledRuns != null) return s_compiledRuns(element, id);
#endif
            try
            {
                return s_runs != null && s_runs.Invoke(element, arguments) is true;
            }
            catch (Exception exception) when (exception is TargetInvocationException or TargetException
                or ArgumentException or MemberAccessException or NotSupportedException)
            {
                return false;
            }
        }

#if !ENABLE_IL2CPP
        // Compiled once, as StyleCascade's readers are, since a style pass asks it for each of LandedAtOnce wherever a
        // transition runs on a watched element it restyles.
        private static readonly Func<VisualElement, int, bool>? s_compiledRuns = CompileRuns();

        private static Func<VisualElement, int, bool>? CompileRuns()
        {
            if (s_runs == null) return null;
            try
            {
                var element = System.Linq.Expressions.Expression.Parameter(typeof(VisualElement), "element");
                var id = System.Linq.Expressions.Expression.Parameter(typeof(int), "id");
                var call = System.Linq.Expressions.Expression.Call(element, s_runs,
                    System.Linq.Expressions.Expression.Convert(id, s_runs.GetParameters()[0].ParameterType));
                return System.Linq.Expressions.Expression.Lambda<Func<VisualElement, int, bool>>(call, element, id).Compile();
            }
            catch (Exception)
            {
                return null;
            }
        }
#endif

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
