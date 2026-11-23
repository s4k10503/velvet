#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// CSS's <c>transition-behavior</c>, which UI Toolkit lacks: <c>transition-discrete</c> and
    /// <c>transition-normal</c>, resolved from the element's classes as the projection holds them, a play's among them.
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
        private static readonly List<long> s_wanted = new();
        private static readonly List<long> s_held = new();
        private static readonly List<StylePropertyName> s_all = new() { new StylePropertyName("all") };
        private static readonly List<StylePropertyName> s_transitionAllList = TransitionAllNames();
        private static readonly Dictionary<StylePropertyName, int> s_transitionAllIndex = IndexOf(s_transitionAllList);
        private static readonly bool[] s_seen = new bool[s_transitionAllList.Count];

        internal static bool Owns(string cls) => cls == TransitionAll || cls == Discrete || cls == Normal;

        internal static bool IsBehaviourClass(string core) => core == Discrete || core == Normal;

        internal static void Add(VisualElement element, string cls, long priority)
        {
            StyleClassProjection.AddToModel(element, cls, priority, Projection(element));
            Resync(element);
        }

        internal static void Remove(VisualElement element, string cls, long priority)
        {
            StyleClassProjection.RemoveFromModel(element, cls, priority, Projection(element));
            Resync(element);
        }

        // A class a play's writer put on, took off or ranked again (StyleClassProjection.RawAdded and its siblings).
        internal static void PlayChanged(VisualElement element, string cls)
        {
            if (Owns(cls))
            {
                Resync(element);
            }
        }

        internal static bool AllowsDiscrete(VisualElement element) => AllowsDiscrete(element, null, StyleLayerPriority.Base);

        /// <summary>
        /// Whether transition-discrete outranks transition-normal on the element once <paramref name="pose"/>, a play's
        /// classes, shows at <paramref name="poseKey"/> as well.
        /// </summary>
        internal static bool AllowsDiscrete(VisualElement element, string[]? pose, long poseKey)
        {
            var discrete = Higher(HighestKey(element, Discrete), PoseKey(pose, Discrete, poseKey));
            if (discrete == null)
            {
                return false;
            }
            var normal = Higher(HighestKey(element, Normal), PoseKey(pose, Normal, poseKey));
            return normal == null || discrete > normal;
        }

        /// <summary>
        /// The transition-property list transition-all gives an element: its own, or <c>all</c> under
        /// transition-discrete. Shared, so a caller writes it to a slot and never changes it.
        /// </summary>
        internal static List<StylePropertyName> TransitionAllList(bool allowsDiscrete)
            => allowsDiscrete ? s_all : s_transitionAllList;

        /// <summary>
        /// Whether <paramref name="names"/> holds transition-all's list, in any order, in the entries that end at
        /// <paramref name="end"/>.
        /// </summary>
        internal static bool EndsTransitionAllList(IList<StylePropertyName> names, int end)
        {
            var count = s_transitionAllList.Count;
            if (end + 1 < count)
            {
                return false;
            }
            System.Array.Clear(s_seen, 0, count);
            for (var i = end - count + 1; i <= end; i++)
            {
                if (!s_transitionAllIndex.TryGetValue(names[i], out var at) || s_seen[at])
                {
                    return false;
                }
                s_seen[at] = true;
            }
            return true;
        }

        // Puts transition-all-discrete on beside transition-all at every key that holds transition-all, a play's
        // included, while transition-discrete outranks transition-normal, and takes it off every other key.
        private static void Resync(VisualElement element)
        {
            s_wanted.Clear();
            if (AllowsDiscrete(element))
            {
                Keys(element, TransitionAll, s_wanted, withPlays: true);
            }
            s_held.Clear();
            Keys(element, TransitionAllDiscrete, s_held, withPlays: false);
            foreach (var key in s_held)
            {
                if (!s_wanted.Contains(key))
                {
                    StyleClassProjection.RemoveFromModel(element, TransitionAllDiscrete, key, Projection(element));
                }
            }
            foreach (var key in s_wanted)
            {
                if (!s_held.Contains(key))
                {
                    StyleClassProjection.AddToModel(element, TransitionAllDiscrete, key, Projection(element));
                    s_held.Add(key);
                }
            }
        }

        private static long? HighestKey(VisualElement element, string cls)
        {
            s_keys.Clear();
            Keys(element, cls, s_keys, withPlays: true);
            long? highest = null;
            foreach (var key in s_keys)
            {
                // MUTANT_SURVIVES(equivalent, boundary): at an equal key the reassignment leaves the same value.
                if (highest == null || key > highest)
                {
                    highest = key;
                }
            }
            return highest;
        }

        private static long? PoseKey(string[]? pose, string cls, long poseKey)
        {
            if (pose == null)
            {
                return null;
            }
            long? found = null;
            foreach (var token in pose)
            {
                var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
                if (core == cls)
                {
                    found = Higher(found, important ? StyleLayerPriority.ImportantOf(poseKey) : poseKey);
                }
            }
            return found;
        }

        private static long? Higher(long? a, long? b) => a == null ? b : b == null ? a : System.Math.Max(a.Value, b.Value);

        // Without a model no payload above the base key was ever applied, so a class on the list is read as held there.
        private static void Keys(VisualElement element, string cls, List<long> into, bool withPlays)
        {
            var model = Projection(element);
            if (model != null)
            {
                model.Keys(cls, into, withPlays);
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
            // MUTANT_SURVIVES(equivalent, boundary): the longhand one past the last is a bit transition-all's set never
            // holds, so reading it adds nothing.
            for (var longhand = 0; longhand < StyleUtilityProperties.LonghandCount; longhand++)
            {
                if (listed.Contains((StyleLonghand)longhand))
                {
                    names.Add(new StylePropertyName(StyleUtilityProperties.UssName((StyleLonghand)longhand)));
                }
            }
            return names;
        }

        private static Dictionary<StylePropertyName, int> IndexOf(List<StylePropertyName> names)
        {
            var index = new Dictionary<StylePropertyName, int>(names.Count);
            for (var i = 0; i < names.Count; i++)
            {
                index[names[i]] = i;
            }
            return index;
        }
    }
}
