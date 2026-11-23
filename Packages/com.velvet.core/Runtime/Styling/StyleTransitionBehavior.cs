#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// CSS's <c>transition-behavior</c>, which UI Toolkit lacks: <c>transition-discrete</c> and
    /// <c>transition-normal</c>, resolved from the element's classes as the projection holds them.
    /// </summary>
    /// <remarks>
    /// Neither class declares a transition-property, so every rule keeps its place in the cascade beside them. What
    /// they decide is whether transition-all-discrete, which names <c>all</c> from the place in the sheet right after
    /// transition-all, joins transition-all on the live list at each key transition-all is held at. transition-all
    /// stays on the list either way, so a rule selecting it still applies. The one of the two held at the higher layer
    /// key decides, and transition-normal where the keys tie, as Tailwind emits it after transition-discrete.
    /// </remarks>
    internal static class StyleTransitionBehavior
    {
        internal const string TransitionAll = "transition-all";
        internal const string TransitionAllDiscrete = "transition-all-discrete";
        internal const string Discrete = "transition-discrete";
        internal const string Normal = "transition-normal";

        private static readonly List<long> s_keys = new();
        private static readonly List<StylePropertyName> s_all = new() { new StylePropertyName("all") };
        private static readonly List<StylePropertyName> s_transitionAllList = TransitionAllNames();

        internal static bool Owns(string cls) => cls == TransitionAll || cls == Discrete || cls == Normal;

        internal static void Add(VisualElement element, string cls, long priority)
        {
            if (cls == TransitionAll)
            {
                StyleClassProjection.AddToModel(element, TransitionAll, priority, Projection(element));
                if (AllowsDiscrete(element))
                {
                    StyleClassProjection.AddToModel(element, TransitionAllDiscrete, priority, Projection(element));
                }
                return;
            }
            var before = AllowsDiscrete(element);
            StyleClassProjection.AddToModel(element, cls, priority, Projection(element));
            Sync(element, before);
        }

        internal static void Remove(VisualElement element, string cls, long priority)
        {
            if (cls == TransitionAll)
            {
                if (AllowsDiscrete(element))
                {
                    StyleClassProjection.RemoveFromModel(element, TransitionAllDiscrete, priority, Projection(element));
                }
                StyleClassProjection.RemoveFromModel(element, TransitionAll, priority, Projection(element));
                return;
            }
            var before = AllowsDiscrete(element);
            StyleClassProjection.RemoveFromModel(element, cls, priority, Projection(element));
            Sync(element, before);
        }

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
        /// The transition-property list transition-all gives an element: its own, or <c>all</c> under
        /// transition-discrete. Shared, so a caller writes it to a slot and never changes it.
        /// </summary>
        internal static List<StylePropertyName> TransitionAllList(bool allowsDiscrete)
            => allowsDiscrete ? s_all : s_transitionAllList;

        // Puts transition-all-discrete on beside transition-all at every key that holds it, or takes it off, once the
        // behaviour has changed.
        private static void Sync(VisualElement element, bool before)
        {
            var after = AllowsDiscrete(element);
            if (after == before)
            {
                return;
            }
            var keys = new List<long>();
            DeclaredKeys(element, TransitionAll, keys);
            foreach (var key in keys)
            {
                if (after)
                {
                    StyleClassProjection.AddToModel(element, TransitionAllDiscrete, key, Projection(element));
                }
                else
                {
                    StyleClassProjection.RemoveFromModel(element, TransitionAllDiscrete, key, Projection(element));
                }
            }
        }

        private static long? HighestKey(VisualElement element, string cls)
        {
            s_keys.Clear();
            DeclaredKeys(element, cls, s_keys);
            long? highest = null;
            foreach (var key in s_keys)
            {
                if (highest == null || key > highest)
                {
                    highest = key;
                }
            }
            return highest;
        }

        // Without a model no payload above the base key was ever applied, so a class on the list is read as held there.
        private static void DeclaredKeys(VisualElement element, string cls, List<long> into)
        {
            var model = Projection(element);
            if (model != null)
            {
                model.DeclaredKeys(cls, into);
            }
            else if (element.ClassListContains(cls))
            {
                into.Add(StyleLayerPriority.Base);
            }
        }

        private static StyleClassProjection.Model? Projection(VisualElement element)
            => StyleArbitraryValueResolver.TryGetProjection(element);

        private static List<StylePropertyName> TransitionAllNames()
        {
            StyleTransitionUtilities.TryGet(TransitionAll, out _, out var listed);
            var names = new List<StylePropertyName>();
            for (var longhand = 0; longhand < StyleUtilityProperties.LonghandCount; longhand++)
            {
                if (listed.Contains((StyleLonghand)longhand))
                {
                    names.Add(new StylePropertyName(StyleUtilityProperties.UssName((StyleLonghand)longhand)));
                }
            }
            return names;
        }
    }
}
