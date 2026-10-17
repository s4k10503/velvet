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
        public static void Add(VisualElement element, string cls, long priority)
        {
            var previousUniform = StyleArbitraryValueResolver.UniformScaleClassFallback(element);
            AddToModel(element, cls, priority);
            StyleArbitraryValueResolver.NotifyClassesChanged(element, previousUniform);
        }

        private static void AddToModel(VisualElement element, string cls, long priority)
        {
            CornerRadiusFit.TrackClass(element, cls);
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

        public static void Remove(VisualElement element, string cls, long priority)
        {
            var previousUniform = StyleArbitraryValueResolver.UniformScaleClassFallback(element);
            RemoveFromModel(element, cls, priority);
            StyleArbitraryValueResolver.NotifyClassesChanged(element, previousUniform);
        }

        private static void RemoveFromModel(VisualElement element, string cls, long priority)
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

        // A class its own writer (StyleAnimationClassUtils) puts on and takes off the live list, rather than a
        // payload. The model ranks it at priority while it is on, so a payload above still outranks it and a
        // play's class outranks the element's own, and never keeps it on: the writer's removal takes it off
        // whatever else wants it. The base priority takes the model-free path, as Add's does.
        public static void RawAdded(VisualElement element, string cls, long priority)
        {
            var model = StyleArbitraryValueResolver.TryGetProjection(element);
            if (model == null)
            {
                if (priority == StyleLayerPriority.Base)
                {
                    return;
                }
                model = StyleArbitraryValueResolver.GetOrCreateProjection(element);
                model.SeedBaseLayer(element);
            }
            model.RawAdded(element, cls, priority);
        }

        // The removal half of RawAdded. It also forgets the class at the base priority, where a seed adopted it
        // if the model was built while the class was on.
        public static void RawRemoved(VisualElement element, string cls)
            => StyleArbitraryValueResolver.TryGetProjection(element)?.RawRemoved(element, cls);

        // Ranks a class a play ranked above the base as the element's own from now on. A class the play's rank no
        // longer holds — taken off since, or never ranked — is left alone, so a rank never outlives its class.
        public static void RawRested(VisualElement element, string cls)
            => StyleArbitraryValueResolver.TryGetProjection(element)?.RawRested(element, cls);

        // Whether cls is element's own while the projection keeps it off the live class list: declared by its
        // className, or put on by a play (RawAdded).
        internal static bool SuppressesOwnClass(VisualElement element, string cls)
            => StyleArbitraryValueResolver.TryGetProjection(element)?.SuppressesOwnClass(cls) == true;

        // Whether cls sits on element's live class list only because a variant payload put it there.
        internal static bool IsHeldOnlyByPayload(VisualElement element, string cls)
            => StyleArbitraryValueResolver.TryGetProjection(element)?.HeldOnlyByPayload(cls) == true;

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

            void ApplyFloors(VisualElement element, Dictionary<ArbitraryProperty, long> floors);
        }

        private static readonly int s_gateCount = System.Enum.GetValues(typeof(StyleUtilityGate)).Length;

        // Stand-ins handed to readers when the lazily allocated field is still null. Never mutated.
        private static readonly List<InlineLayer> s_noInlineLayers = new();
        private static readonly Dictionary<ArbitraryProperty, long> s_noFloors = new();

        // One element's layers. Lives on the arbitrary-value LayerMap (see
        // StyleArbitraryValueResolver.GetOrCreateProjection) so the class half and the inline half of the
        // cascade share one lifetime and are scrubbed by one call.
        internal sealed class Model
        {
            private readonly ILayerHost _host;
            private readonly List<Entry> _entries = new();
            private readonly StyleLonghandSet[] _claimed = new StyleLonghandSet[s_gateCount];
            // Classes this model took off the element. Nothing else may be re-added: a class another
            // subsystem removed from the live list (the animation scheduler, the presence exit) must stay
            // gone even while the model still records the layer that asked for it.
            private HashSet<string>? _suppressed;
            private List<InlineLayer>? _inline;
            private Dictionary<ArbitraryProperty, long>? _floors;
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

            public void Add(VisualElement element, string cls, long priority)
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

            public void Remove(VisualElement element, string cls, long priority)
            {
                var index = IndexOf(cls, priority);
                if (index >= 0)
                {
                    _entries.RemoveAt(index);
                }
                // The className dropping a class takes it off whatever a play wants, as a class attribute the
                // render rewrites does; a payload is no class of the element's, so its going leaves a play's.
                var declared = priority == StyleLayerPriority.Base
                    || priority == StyleLayerPriority.ImportantOf(StyleLayerPriority.Base);
                var leaves = !Holds(cls) && (declared || IndexOfRaw(cls, null) < 0);
                if (leaves)
                {
                    DropRaw(cls);
                }
                Recompute(element);
                // The last layer wanting this class is gone, so it leaves whether or not it was suppressed —
                // and the suppression marker leaves with it, or a re-add would find the class already
                // "restored" and never put it back.
                if (leaves)
                {
                    element.RemoveFromClassList(cls);
                    _suppressed?.Remove(cls);
                }
            }

            // A ranking-only entry: its writer puts the class on the live list, and the entry keeps it there only
            // against a payload's removal (Holds skips it). It lives only while the class is on that list or
            // suppressed by this model: RawRemoved drops it, and so does the className's Remove once nothing else
            // holds the class.
            public void RawAdded(VisualElement element, string cls, long priority)
            {
                // A class put on at the base priority rests there, so a play that ranked it higher has ended.
                if (priority == StyleLayerPriority.Base)
                {
                    DropRaisedRaw(cls);
                }
                // MUTANT_SURVIVES(equivalent, boundary): IndexOfRaw answers -1 or an index, and at index 0 the mutant
                // adds a second ranking-only entry to the same slot, which ranks as the first does and is dropped with
                // it.
                if (IndexOfRaw(cls, priority) < 0)
                {
                    StyleUtilityProperties.TryGet(cls, out var rule);
                    _entries.Add(new Entry(cls, priority, rule.Properties, (int)rule.Gate, rankOnly: true));
                }
                Recompute(element);
            }

            // The writer's removal stands whatever else wants the class, so its suppression goes with it: a raw
            // add that finds the class still marked suppressed would leave it on the list however it ranks.
            public void RawRemoved(VisualElement element, string cls)
            {
                DropRaw(cls);
                _suppressed?.Remove(cls);
                Recompute(element);
            }

            public void RawRested(VisualElement element, string cls)
            {
                if (DropRaisedRaw(cls))
                {
                    RawAdded(element, cls, StyleLayerPriority.Base);
                }
            }

            // Drops cls's ranking-only entries above the base priority, reporting whether there were any.
            private bool DropRaisedRaw(string cls)
            {
                var dropped = false;
                for (var i = _entries.Count - 1; i >= 0; i--)
                {
                    if (_entries[i].Class == cls && _entries[i].RankOnly
                        && _entries[i].Priority != StyleLayerPriority.Base)
                    {
                        _entries.RemoveAt(i);
                        dropped = true;
                    }
                }
                return dropped;
            }

            // Drops cls's ranking-only entries and its base one: a seed adopts a raw class there if the model is
            // built while the class is on.
            private void DropRaw(string cls)
            {
                for (var i = _entries.Count - 1; i >= 0; i--)
                {
                    var entry = _entries[i];
                    if (entry.Class == cls && (entry.RankOnly || entry.Priority == StyleLayerPriority.Base))
                    {
                        _entries.RemoveAt(i);
                    }
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

                var cutoff = long.MaxValue;
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

            private bool TryNextBand(long cutoff, out long priority)
            {
                priority = long.MinValue;
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

            // Ties WITHIN the band are deliberately left alone. A band is one rule's key, so only a token written
            // twice behind one variant, payloads carrying no rule place, or two base classes reach one; the
            // stylesheet decides them.
            private void JudgeBand(long priority)
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
                    _floors ??= new Dictionary<ArbitraryProperty, long>();
                    _floors[layer.Property] = _floors.TryGetValue(layer.Property, out var floor) && floor > priority
                        ? floor
                        : priority;
                }
            }

            // A dead layer's properties are by definition already claimed, so claiming the whole band rather
            // than only its survivors gives the same set for less work.
            private void ClaimBand(long priority)
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

            // A restored class is APPENDED to the live class list, not returned to where it was. Whether
            // that can move an answer is two questions LiveClassListOrderTests holds together: which
            // readers decide an answer from the order at all, and whether any class one of those decides
            // FROM carries a rule this path could suppress.
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

            // The highest key of a class on the element that writes longhand unconditionally, or the lowest key
            // when none does: what an inline writer of it has to outrank.
            public long ClaimOf(StyleLonghand longhand)
            {
                var claim = long.MinValue;
                foreach (var entry in _entries)
                {
                    // MUTANT_SURVIVES(equivalent): accepting an equal priority assigns the claim its existing value.
                    if (!entry.Dead && entry.Gate == (int)StyleUtilityGate.None && entry.Priority > claim
                        && entry.Properties.Contains(longhand))
                    {
                        claim = entry.Priority;
                    }
                }
                return claim;
            }

            public bool HasImportantClass(string cls)
            {
                foreach (var entry in _entries)
                {
                    if (entry.Class == cls && !entry.Dead && (entry.Priority & StyleLayerPriority.Important) != 0)
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool SuppressesOwnClass(string cls) => _suppressed?.Contains(cls) == true && IsOwn(cls);

            public bool HeldOnlyByPayload(string cls) => Holds(cls) && !IsOwn(cls);

            // A payload's class is not the element's own, so only a base entry counts, important or not, and a
            // play's ranking-only one at whatever rank the play holds it: a class written onto the element is in
            // its class attribute however the cascade ranks it.
            private bool IsOwn(string cls)
            {
                foreach (var entry in _entries)
                {
                    if (entry.Class == cls && (entry.RankOnly || entry.Priority == StyleLayerPriority.Base
                            || entry.Priority == StyleLayerPriority.ImportantOf(StyleLayerPriority.Base)))
                    {
                        return true;
                    }
                }
                return false;
            }

            private int FirstIndexOf(string cls)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Class == cls && !_entries[i].RankOnly)
                    {
                        return i;
                    }
                }
                return -1;
            }

            // A payload's or the className's slot, never a play's ranking-only entry at the same key.
            private int IndexOf(string cls, long priority)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    // MUTANT_SURVIVES(equivalent): only the base key holds both kinds, and Add and Remove both read
                    // here, so the mutant lets a resting play's entry stand for the declared one at both ends: Add
                    // records none of its own, and Remove takes the play's, which leaves the class as taking the
                    // declared one would.
                    if (_entries[i].Priority == priority && _entries[i].Class == cls && !_entries[i].RankOnly)
                    {
                        return i;
                    }
                }
                return -1;
            }

            // cls's ranking-only entry at priority, or at any priority when priority is null.
            private int IndexOfRaw(string cls, long? priority)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    if (entry.RankOnly && entry.Class == cls && (priority == null || entry.Priority == priority))
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
            // MUTANT_SURVIVES(equivalent): every entry is added on a path that recomputes before anything reads
            // it, and Recompute writes each entry's Dead.
            public Entry(string cls, long priority, StyleLonghandSet properties, int gate, bool dead = false,
                bool rankOnly = false)
            {
                Class = cls;
                Priority = priority;
                Properties = properties;
                Gate = gate;
                Dead = dead;
                RankOnly = rankOnly;
            }

            public string Class { get; }

            public long Priority { get; }

            public StyleLonghandSet Properties { get; }

            // A rule gated on a pseudo-class carries one more simple selector than a bare utility, so it
            // never contends with an ungated one; the gate partitions the claims.
            public int Gate { get; }

            public bool Dead { get; }

            // Added by Model.RawAdded: see there.
            public bool RankOnly { get; }

            public Entry WithDead(bool dead) => new Entry(Class, Priority, Properties, Gate, dead, RankOnly);
        }

        // One registered arbitrary-value layer, as the projection sees it.
        internal readonly struct InlineLayer
        {
            public InlineLayer(ArbitraryProperty property, long priority)
            {
                Property = property;
                Priority = priority;
            }

            public ArbitraryProperty Property { get; }

            public long Priority { get; }
        }
    }
}
