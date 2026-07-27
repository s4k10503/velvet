using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The class-list half of an element's cascade model: which utility class every priority layer wants, and
    // which of those may sit on the live class list at once.
    //
    // Bundled utilities are single-class selectors, so two of them tie on specificity and USS breaks the tie
    // by declaration order — which has nothing to do with which one the author meant to win. Applying a
    // variant payload later cannot change that. So the winner is decided here instead, per USS longhand,
    // from the priority the payload's manipulator already computes, and the losers are taken off the element.
    //
    // The verdict is RECOMPUTED from the whole model on every change rather than bookkept as a stack of
    // removals to undo. Restoring a payload that turns off is then structural: it falls out of the model no
    // longer holding that layer, whatever else is active at the time.
    //
    // Only a class whose property set is wholly CONTAINED in what higher priorities claim is suppressed,
    // which leaves two shapes to declaration order. One is safe: a strict superset survives because it still
    // owns properties nothing above it claims, and the bundled stylesheets declare every utility before the
    // narrower ones its set contains, so `size-8 md:w-4` resolves the width correctly on source order alone.
    // The other is not: two sets that merely OVERLAP (`rounded-l` and `rounded-t` share one corner and
    // neither contains the other) have no invariant behind them, so `rounded-l md:rounded-t` can be a silent
    // no-op — and the important modifier does not rescue it, since raising the payload's band still leaves
    // the base uncontained and therefore alive.
    //
    // A class with no entry in StyleUtilityProperties — a user's own, or one of the families Velvet realises
    // in C# rather than in USS — has an empty property set. It neither claims a longhand nor loses one, so
    // two of a user's own classes tie exactly as they did before, with no way to rank them.
    internal static class StyleClassProjection
    {
        // The base priority takes the model-free path until some payload above it exists: two base classes
        // tie by declaration order with or without a model, so building one for the overwhelming majority of
        // elements (a class list with no variant, no important modifier and no bracket value) would buy
        // nothing.
        public static void Add(VisualElement element, string cls, int priority)
        {
            var model = StyleArbitraryValueResolver.TryGetProjection(element);
            if (model == null)
            {
                if (priority == StyleLayerPriority.Base)
                {
                    element.AddToClassList(cls);
                    return;
                }
                model = StyleArbitraryValueResolver.GetOrCreateProjection(element);
                model.SeedBaseLayer(element);
            }
            model.Add(element, cls, priority);
        }

        public static void Remove(VisualElement element, string cls, int priority)
        {
            var model = StyleArbitraryValueResolver.TryGetProjection(element);
            if (model != null)
            {
                model.Remove(element, cls, priority);
                return;
            }
            // No model means no payload above the base priority was ever applied, so an off-toggle from one
            // is spurious and must not touch the class list. Several families (structural, has-[.class]:,
            // data-/aria-, supports-) issue an unconditional off for a rule that never matched, and the class
            // it names may be the author's own literal token.
            if (priority == StyleLayerPriority.Base)
            {
                element.RemoveFromClassList(cls);
            }
        }

        // Called by StyleArbitraryValueResolver, because an inline layer both outranks the classes below it
        // and can itself be outranked by a class above it.
        internal static void OnInlineLayersChanged(VisualElement element, Model model) => model.Recompute(element);

        // The inline half of the cascade, seen from the class half. Implemented by the layer map the model is
        // stored on, so a recompute reaches it directly rather than resolving the element through the weak
        // table once per question — three lookups per toggle on a list that toggles every row.
        internal interface ILayerHost
        {
            bool HasLayers { get; }

            void CollectLayers(List<InlineLayer> into);

            void ApplyFloors(VisualElement element, Dictionary<ArbitraryProperty, int> floors);
        }

        private static readonly int s_gateCount = System.Enum.GetValues(typeof(StyleUtilityGate)).Length;

        // Stand-ins handed to readers when the lazily allocated field is still null. Never mutated.
        private static readonly List<InlineLayer> s_noInlineLayers = new();
        private static readonly Dictionary<ArbitraryProperty, int> s_noFloors = new();

        // One element's layers. Lives on the arbitrary-value LayerMap (see
        // StyleArbitraryValueResolver.GetOrCreateProjection) so the class half and the inline half of the
        // cascade share one lifetime and are scrubbed by one call.
        internal sealed class Model
        {
            private readonly ILayerHost _host;
            private readonly List<Entry> _entries = new();
            private readonly StyleLonghandSet[] _claimed = new StyleLonghandSet[s_gateCount];
            // Classes this model took off the element. Nothing else may be re-added: a class another
            // subsystem removed from the live list (the animation scheduler, a gesture manipulator) must stay
            // gone even while the model still records the layer that asked for it.
            private HashSet<string>? _suppressed;
            private List<InlineLayer>? _inline;
            private Dictionary<ArbitraryProperty, int>? _floors;
            private Dictionary<string, bool>? _verdict;
            private int _deadCount;

            public Model(ILayerHost host) => _host = host;

            private List<InlineLayer> Inline => _inline ?? s_noInlineLayers;

            // Adopts what is already on the element as the base layer. The model is not built until a payload
            // above the base priority arrives, which is long after the reconciler wrote the base classes
            // straight onto the element — without this the arriving payload would find nothing to outrank.
            //
            // Classes carrying no USS rule are seeded too, though they can neither claim a property nor lose
            // one: recording them is what lets a payload naming a class the element ALREADY carries
            // (`gap-4` beside `md:gap-4`) leave that class behind when the payload turns off.
            public void SeedBaseLayer(VisualElement element)
            {
                foreach (var cls in element.GetClasses())
                {
                    StyleUtilityProperties.TryGet(cls, out var rule);
                    _entries.Add(new Entry(cls, StyleLayerPriority.Base, rule.Properties, (int)rule.Gate));
                }
            }

            public void Add(VisualElement element, string cls, int priority)
            {
                if (IndexOf(cls, priority) < 0)
                {
                    StyleUtilityProperties.TryGet(cls, out var rule);
                    _entries.Add(new Entry(cls, priority, rule.Properties, (int)rule.Gate));
                }
                Recompute(element);
                if (IsAlive(cls))
                {
                    element.AddToClassList(cls);
                }
            }

            public void Remove(VisualElement element, string cls, int priority)
            {
                var index = IndexOf(cls, priority);
                if (index >= 0)
                {
                    _entries.RemoveAt(index);
                }
                Recompute(element);
                // The last layer wanting this class is gone, so it leaves whether or not it was suppressed —
                // and the suppression marker leaves with it, or a re-add would find the class already
                // "restored" and never put it back.
                if (!Holds(cls))
                {
                    element.RemoveFromClassList(cls);
                    _suppressed?.Remove(cls);
                }
            }

            public void Recompute(VisualElement element)
            {
                CollectInlineLayers(element);
                for (var i = 0; i < _entries.Count; i++)
                {
                    _entries[i] = _entries[i].WithDead(false);
                }
                for (var i = 0; i < _claimed.Length; i++)
                {
                    _claimed[i] = StyleLonghandSet.Empty;
                }
                _floors?.Clear();
                _deadCount = 0;

                var cutoff = int.MaxValue;
                while (TryNextBand(cutoff, out var priority))
                {
                    JudgeBand(priority);
                    ClaimBand(priority);
                    cutoff = priority;
                }

                ApplyClassVerdict(element);
                _host.ApplyFloors(element, _floors ?? s_noFloors);
            }

            private void CollectInlineLayers(VisualElement element)
            {
                _inline?.Clear();
                if (_host.HasLayers)
                {
                    _host.CollectLayers(_inline ??= new List<InlineLayer>());
                }
            }

            private bool TryNextBand(int cutoff, out int priority)
            {
                priority = int.MinValue;
                var found = false;
                foreach (var entry in _entries)
                {
                    if (entry.Priority < cutoff && (!found || entry.Priority > priority))
                    {
                        priority = entry.Priority;
                        found = true;
                    }
                }
                foreach (var layer in Inline)
                {
                    if (layer.Priority < cutoff && (!found || layer.Priority > priority))
                    {
                        priority = layer.Priority;
                        found = true;
                    }
                }
                return found;
            }

            // Ties WITHIN the band are deliberately left alone: source order decides them, as it always has.
            private void JudgeBand(int priority)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    if (entry.Priority == priority && !entry.Properties.IsEmpty
                        && IsSubset(entry.Properties, _claimed[entry.Gate]))
                    {
                        _entries[i] = entry.WithDead(true);
                        _deadCount++;
                    }
                }
                var inline = Inline;
                for (var i = 0; i < inline.Count; i++)
                {
                    var layer = inline[i];
                    var properties = StyleArbitraryLonghands.Of(layer.Property);
                    if (layer.Priority != priority || properties.IsEmpty
                        || !IsSubset(properties, _claimed[(int)StyleUtilityGate.None]))
                    {
                        continue;
                    }
                    // A property's layers die from the bottom up (the claims a lower layer faces are a
                    // superset of the ones above it), so the highest dead priority is the whole floor.
                    _floors ??= new Dictionary<ArbitraryProperty, int>();
                    _floors[layer.Property] = _floors.TryGetValue(layer.Property, out var floor) && floor > priority
                        ? floor
                        : priority;
                }
            }

            // A dead layer's properties are by definition already claimed, so claiming the whole band rather
            // than only its survivors gives the same set for less work.
            private void ClaimBand(int priority)
            {
                foreach (var entry in _entries)
                {
                    if (entry.Priority == priority)
                    {
                        _claimed[entry.Gate] = _claimed[entry.Gate].Union(entry.Properties);
                    }
                }
                var none = (int)StyleUtilityGate.None;
                foreach (var layer in Inline)
                {
                    if (layer.Priority == priority)
                    {
                        _claimed[none] = _claimed[none].Union(StyleArbitraryLonghands.Of(layer.Property));
                    }
                }
            }

            // A restored class is APPENDED to the live class list, not returned to where it was. No reader
            // depends on live-list order today except the clip-path scan, whose tokens carry no USS rule and
            // so are never suppressed; a new order-sensitive reader has to hold that in mind.
            //
            // Skipped outright unless something is suppressed or has just become so — the state most
            // elements carrying a model are never in, and the one that turns a class-list toggle into work
            // proportional to the whole class list.
            private void ApplyClassVerdict(VisualElement element)
            {
                if (_deadCount == 0 && (_suppressed == null || _suppressed.Count == 0))
                {
                    return;
                }
                // One aliveness per distinct class, folded in a single pass. A class the model holds at two
                // priorities is alive if EITHER layer survived, so the two entries cannot be judged apart.
                var verdict = _verdict ??= new Dictionary<string, bool>();
                verdict.Clear();
                foreach (var entry in _entries)
                {
                    verdict[entry.Class] = !entry.Dead
                        || (verdict.TryGetValue(entry.Class, out var alive) && alive);
                }
                foreach (var pair in verdict)
                {
                    if (pair.Value)
                    {
                        if (_suppressed != null && _suppressed.Remove(pair.Key))
                        {
                            element.AddToClassList(pair.Key);
                        }
                    }
                    else if ((_suppressed ??= new HashSet<string>()).Add(pair.Key))
                    {
                        element.RemoveFromClassList(pair.Key);
                    }
                }
            }

            private static bool IsSubset(StyleLonghandSet a, StyleLonghandSet b) => b.Union(a) == b;

            private bool IsAlive(string cls)
            {
                foreach (var entry in _entries)
                {
                    if (entry.Class == cls && !entry.Dead)
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool Holds(string cls) => FirstIndexOf(cls) >= 0;

            private int FirstIndexOf(string cls)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Class == cls)
                    {
                        return i;
                    }
                }
                return -1;
            }

            private int IndexOf(string cls, int priority)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Priority == priority && _entries[i].Class == cls)
                    {
                        return i;
                    }
                }
                return -1;
            }
        }

        // One class at one priority. A (priority, class) pair is a single slot, matching the inline layer
        // map: two payloads of the same priority naming the same class share it.
        private readonly struct Entry
        {
            public Entry(string cls, int priority, StyleLonghandSet properties, int gate, bool dead = false)
            {
                Class = cls;
                Priority = priority;
                Properties = properties;
                Gate = gate;
                Dead = dead;
            }

            public string Class { get; }

            public int Priority { get; }

            public StyleLonghandSet Properties { get; }

            // A rule gated on a pseudo-class carries one more simple selector than a bare utility, so it
            // never contends with an ungated one; the gate partitions the claims.
            public int Gate { get; }

            public bool Dead { get; }

            public Entry WithDead(bool dead) => new Entry(Class, Priority, Properties, Gate, dead);
        }

        // One registered arbitrary-value layer, as the projection sees it.
        internal readonly struct InlineLayer
        {
            public InlineLayer(ArbitraryProperty property, int priority)
            {
                Property = property;
                Priority = priority;
            }

            public ArbitraryProperty Property { get; }

            public int Priority { get; }
        }
    }
}
