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
    /// <c>transition-normal</c> on the element. TransitionDiscretePanelTests fails when a cancel stops landing the value.
    /// A transition of visibility or background-position under a hold goes to <see cref="StyleTransitionInterpolation"/>.
    /// </summary>
    internal static class StyleTransitionBehavior
    {
        internal const string Discrete = "transition-discrete";
        internal const string Normal = "transition-normal";

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
        private static readonly MethodInfo? s_cancel = EngineMember.CancelStyleAnimation.ResolveMethod();
        // The cancel's argument for each of LandedAtOnce; none where an engine member does not resolve, which leaves
        // UI Toolkit's transitions as they run.
        private static readonly Dictionary<StylePropertyName, object?[]> s_cancelArguments = CancelArguments();

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
                root.RegisterCallback(s_onRun);
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
                }
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
                if (s_cancelArguments.TryGetValue(name, out var arguments) && !AllowsDiscrete(element))
                {
                    try
                    {
                        s_cancel!.Invoke(element, arguments);
                    }
                    catch (Exception exception) when (exception is TargetInvocationException or TargetException
                        or ArgumentException or MemberAccessException or NotSupportedException)
                    {
                    }
                }
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

        private static Dictionary<StylePropertyName, object?[]> CancelArguments()
        {
            var arguments = new Dictionary<StylePropertyName, object?[]>();
            var id = EngineMember.StylePropertyNameId.ResolveProperty();
            if (s_cancel == null || id == null)
            {
                return arguments;
            }
            foreach (var longhand in LandedAtOnce)
            {
                var name = new StylePropertyName(longhand);
                try
                {
                    arguments[name] = new[] { id.GetValue(name) };
                }
                catch (Exception exception) when (exception is TargetInvocationException or TargetException
                    or ArgumentException or MemberAccessException or NotSupportedException)
                {
                }
            }
            return arguments;
        }
    }
}
