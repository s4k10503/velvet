using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // CSS custom properties for the class tokens StyleCustomPropertyClass reads. A declaration is held per element
    // and per layer, as a variant payload's value is; a stylesheet's declarations are read from the engine. A
    // consumer is resolved on its element into the utility it would be with each var() replaced, split into its
    // sides where it is a shorthand holding several values, and applied as payloads at the consumer's own layer.
    // A var() that cannot be resolved, or whose result the utility's property rejects, is invalid at
    // computed-value time: a consumer writing a style property writes that property's unset value, and one a
    // class-driven pass paints writes the utility standing for what the browser draws (PaintedUnset).
    internal static partial class StyleCustomProperties
    {
        // Nesting of var() through fallbacks and declarations past this depth is declined rather than followed.
        private const int MaxDepth = 32;

        // The tick RelativeLengthPoll polls at.
        private const long TickMs = 16;

        private static readonly ConditionalWeakTable<VisualElement, Dictionary<string, SortedList<long, string>>> s_declared = new();
        private static readonly ConditionalWeakTable<VisualElement, Consumers> s_consumers = new();
        private static readonly EventCallback<AttachToPanelEvent> s_onAttach = OnAttach;

        // The class list a reconcile last applied to the element, which ranks its base-layer consumers against its
        // literal base utilities (Settle).
        private static readonly ConditionalWeakTable<VisualElement, string[]> s_classes = new();

        // Reused by every resolution: one runs alone and nothing it calls resolves re-entrantly.
        private static readonly StringBuilder s_built = new();
        private static readonly List<(VisualElement Element, string Name)> s_resolving = new();
        private static int s_depth;
        private static int s_held;
        private static readonly List<VisualElement> s_heldRoots = new();

        // The number of elements holding a consumer, so a declaration change on a tree with none walks nothing.
        private static int s_consumerElements;

        private static readonly Dictionary<string, string?> s_probes = new(StringComparer.Ordinal);

        private sealed class Consumers
        {
            public readonly List<Consumer> Entries = new();
            public IVisualElementScheduledItem? Poll;
            // The variable context the poll last resolved against. The engine hands an element a new context
            // whenever a variable in scope of it changes, so an unchanged one leaves nothing for the poll to do.
            public object? Context;
        }

        private sealed class Consumer
        {
            public readonly string Utility;
            public readonly long Priority;
            public readonly int Declaration;
            // The utility's place among the element's rules (StyleRuleOrder), for a base-layer consumer, which
            // ranks it against the element's other base utilities writing the same property.
            public readonly int Order;
            public readonly ReconcilerContext? Context;
            public readonly UtilityShape Shape;
            // The hint written into the resolved bracket where Tailwind types the value without testing it
            // (DefaultHint), and the hint the value is read with, written or so added.
            public readonly string? AddedHint;
            public readonly string? EffectiveHint;
            // The property the utility writes as an inline layer (WrittenProperty), which an unresolvable var()
            // writes the unset value of. Null for a utility that reads no var(), whose value is as written.
            public readonly ArbitraryProperty? Property;
            // For a utility a class-driven pass paints, the one standing for what an unresolvable var() computes
            // to (PaintedUnset).
            public readonly string? UnsetUtility;
            // The side utilities a shorthand splits into (StyleCustomPropertyClass.SidesOf).
            public readonly string[]? Sides;
            // Whether a modifier bracket reads a var(), whose value is a color-mix() percentage.
            public readonly bool ModifierReadsVariable;
            // The resolution the applied state came from, null when the last one resolved nothing.
            public string? Source;
            public bool Resolved;
            public string[]? Applied;
            public bool Unset;
            // The parent's value an inherited unset writes, once the element's own stylesheets are seen to give it
            // another (TrackInherited); null while no inline value stands for it.
            public ArbitraryStyle? Inherited;

            public Consumer(string utility, long priority, int declaration, int order, ReconcilerContext? context)
            {
                Utility = utility;
                Priority = priority;
                Declaration = declaration;
                Order = order;
                Context = context;
                Shape = UtilityShape.Of(utility);
                AddedHint = DefaultHint(utility, Shape);
                EffectiveHint = Shape.Hint ?? AddedHint;
                Sides = StyleCustomPropertyClass.SidesOf(utility, Shape);
                var readsVariable = utility.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0;
                ModifierReadsVariable = Shape.HasModifier
                    && utility.IndexOf("var(", Math.Max(Shape.MainClose, 0), StringComparison.OrdinalIgnoreCase) >= 0;
                if (readsVariable)
                {
                    Property = WrittenProperty(utility, Shape, AddedHint, out _);
                    UnsetUtility = Property == null && Shape.MainOpen > 0
                        ? PaintedUnset(utility.Substring(utility[0] == '-' ? 1 : 0, Shape.MainOpen - (utility[0] == '-' ? 1 : 0)),
                            EffectiveHint)
                        : null;
                }
            }

            public long Key => StyleLayerPriority.WithRule(Priority, Declaration);

            // The layer the element's literal base utilities are applied at (ApplyClassToken), which this shares.
            public bool OnBaseLayer => (Key & ~StyleLayerPriority.Important) == StyleLayerPriority.Base;
        }

        // Applies (on) or withdraws one token StyleCustomPropertyClass.IsCustomPropertyToken accepts, at the
        // priority and rule place a payload carries (StyleVariantPayload.Apply). order is the token's place among
        // the element's rules, for a token on the base layer.
        public static void Toggle(VisualElement target, string core, bool on, long priority, int declaration,
            ReconcilerContext? context, int order = StyleVariantPayload.NoDeclaration)
        {
            if (StyleCustomPropertyClass.TryDeclaration(core, out var name, out var value))
            {
                if (Declare(target, name, value, StyleLayerPriority.WithRule(priority, declaration), on))
                {
                    if (s_held > 0)
                    {
                        if (!s_heldRoots.Contains(target))
                        {
                            s_heldRoots.Add(target);
                        }
                    }
                    else
                    {
                        ReresolveSubtree(target);
                    }
                }
                return;
            }
            var utility = StyleCustomPropertyClass.Consumer(core);
            if (utility == null)
            {
                return;
            }
            if (on)
            {
                Bind(target, new Consumer(utility, priority, declaration, order, context));
            }
            else
            {
                Unbind(target, utility, priority, declaration);
            }
        }

        // Holds the consumers a declaration change reaches until ReleaseHeld, so a set of toggles that replaces one
        // declaration with another resolves them against the result rather than against the step between.
        public static void Hold() => s_held++;

        public static void ReleaseHeld()
        {
            if (--s_held > 0)
            {
                return;
            }
            var roots = s_heldRoots.ToArray();
            s_heldRoots.Clear();
            foreach (var root in roots)
            {
                ReresolveSubtree(root);
            }
        }

        // Drops everything held for element without writing to it, for an element whose styles are being cleared.
        public static void Release(VisualElement element)
        {
            s_declared.Remove(element);
            s_classes.Remove(element);
            if (s_consumers.TryGetValue(element, out var consumers))
            {
                Unwatch(element, consumers);
                s_consumers.Remove(element);
            }
        }

        // Records the class list a reconcile applied to element, for Settle.
        public static void RecordClasses(VisualElement element, string[] classNames)
        {
            s_classes.Remove(element);
            s_classes.Add(element, classNames);
        }

        // A parseable stand-in for a consumer token, read in place of it where the rule order sorts the element's
        // tokens by the property they write: the utility with a value of the type Tailwind gives it. Null for any
        // other token, and for a consumer no such value parses for.
        public static string? SortableOf(string core)
        {
            if (StyleCustomPropertyClass.Consumer(core) is not { } utility)
            {
                return null;
            }
            if (!s_probes.TryGetValue(utility, out var probe))
            {
                var shape = UtilityShape.Of(utility);
                WrittenProperty(utility, shape, DefaultHint(utility, shape), out probe, keywordOnly: false);
                s_probes[utility] = probe;
            }
            return probe;
        }

        private static bool Declare(VisualElement target, string name, string value, long key, bool on)
        {
            var declared = s_declared.GetValue(target, static _ => new Dictionary<string, SortedList<long, string>>());
            if (!declared.TryGetValue(name, out var layers))
            {
                if (!on)
                {
                    return false;
                }
                layers = new SortedList<long, string>();
                declared[name] = layers;
            }
            var before = layers.Count == 0 ? null : layers.Values[layers.Count - 1];
            if (on)
            {
                layers[key] = value;
            }
            else
            {
                layers.Remove(key);
            }
            var after = layers.Count == 0 ? null : layers.Values[layers.Count - 1];
            if (layers.Count == 0)
            {
                declared.Remove(name);
            }
            return !string.Equals(before, after, StringComparison.Ordinal);
        }

        private static void Bind(VisualElement target, Consumer consumer)
        {
            var consumers = s_consumers.GetValue(target, static _ => new Consumers());
            consumers.Entries.Add(consumer);
            if (consumers.Entries.Count == 1)
            {
                Watch(target, consumers);
            }
            Reresolve(target, consumer);
        }

        private static void Unbind(VisualElement target, string utility, long priority, int declaration)
        {
            if (!s_consumers.TryGetValue(target, out var consumers))
            {
                return;
            }
            var entries = consumers.Entries;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var consumer = entries[i];
                if (consumer.Utility == utility && consumer.Priority == priority && consumer.Declaration == declaration)
                {
                    entries.RemoveAt(i);
                    Withdraw(target, consumer);
                    break;
                }
            }
            if (entries.Count == 0)
            {
                Unwatch(target, consumers);
                s_consumers.Remove(target);
            }
        }

        // Attaching to a panel is when the element first has the ancestors its declarations come from. A
        // stylesheet's declarations move at a style pass, where OnCustomStyle hears the element declaring them;
        // the scheduler's tick asks again for a context that changed on an element no walk has read yet, which
        // nothing listens to.
        private static void Watch(VisualElement target, Consumers consumers)
        {
            s_consumerElements++;
            target.RegisterCallback(s_onAttach);
            consumers.Poll = target.schedule.Execute(() => Poll(target)).Every(TickMs);
        }

        private static void Unwatch(VisualElement target, Consumers consumers)
        {
            s_consumerElements--;
            target.UnregisterCallback(s_onAttach);
            consumers.Poll?.Pause();
            consumers.Poll = null;
        }

        // An inline layer is written here and now. A payload a class-driven pass reads waits for the element's next
        // scheduler tick: that pass may add or remove a sibling (a ring's overlay), and this runs inside the walk
        // the engine attaches the tree with.
        private static void OnAttach(AttachToPanelEvent evt)
        {
            var target = (VisualElement)evt.currentTarget;
            if (!s_consumers.TryGetValue(target, out var consumers))
            {
                return;
            }
            var deferred = false;
            foreach (var consumer in consumers.Entries.ToArray())
            {
                if (consumer.Property != null)
                {
                    Reresolve(target, consumer);
                }
                else
                {
                    deferred = true;
                }
            }
            if (deferred)
            {
                target.schedule.Execute(() => ReresolveAll(target));
            }
        }

        // The engine sends this from inside its style traversal, after the declaring element's variable context is
        // rebuilt and before its descendants' are, and a descendant's inline value is written here, ahead of the
        // traversal reaching it, rather than at the next scheduler tick. Each descendant still holds its previous
        // context then, so the one compared with the declaring element's (TryStylesheet) is that element's
        // previous one. A payload a class-driven pass reads waits for the scheduler, since that pass may add or
        // remove a sibling mid-traversal.
        private static void OnCustomStyle(CustomStyleResolvedEvent evt)
        {
            if (s_consumerElements <= 0 || evt.currentTarget is not VisualElement declaring)
            {
                return;
            }
            s_seen.TryGetValue(declaring, out var previous);
            (s_traversed, s_traversedPrevious) = (declaring, previous);
            try
            {
                ReresolveInlineSubtree(declaring);
            }
            finally
            {
                (s_traversed, s_traversedPrevious) = (null, null);
            }
        }

        private static void ReresolveInlineSubtree(VisualElement root)
        {
            if (s_consumers.TryGetValue(root, out var consumers))
            {
                var deferred = false;
                foreach (var consumer in consumers.Entries.ToArray())
                {
                    if (consumer.Property != null)
                    {
                        Reresolve(root, consumer);
                    }
                    else
                    {
                        deferred = true;
                    }
                }
                if (deferred)
                {
                    root.schedule.Execute(() => ReresolveAll(root));
                }
            }
            var children = root.hierarchy;
            for (var i = 0; i < children.childCount; i++)
            {
                ReresolveInlineSubtree(children[i]);
            }
        }

        private static void Poll(VisualElement target)
        {
            if (!s_consumers.TryGetValue(target, out var consumers))
            {
                return;
            }
            var entries = consumers.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].Unset && Inherits(entries[i].Property!.Value))
                {
                    TrackInherited(target, entries[i]);
                }
            }
            var context = s_variableContext?.GetValue(target);
            if (ReferenceEquals(context, consumers.Context))
            {
                return;
            }
            consumers.Context = context;
            ReresolveAll(target);
        }

        private static void ReresolveAll(VisualElement target)
        {
            if (!s_consumers.TryGetValue(target, out var consumers))
            {
                return;
            }
            // Indexed: this runs on every poll tick, and resolving a consumer binds or unbinds none.
            var entries = consumers.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                Reresolve(target, entries[i]);
            }
        }

        private static void ReresolveSubtree(VisualElement root)
        {
            if (s_consumerElements <= 0)
            {
                return;
            }
            ReresolveAll(root);
            var children = root.hierarchy;
            for (var i = 0; i < children.childCount; i++)
            {
                ReresolveSubtree(children[i]);
            }
        }

        private static void Reresolve(VisualElement target, Consumer consumer)
        {
            s_built.Clear();
            var resolved = TryBuild(target, consumer, s_built);
            if (consumer.Resolved && (resolved ? consumer.Source != null && Matches(s_built, consumer.Source) : consumer.Source == null))
            {
                return;
            }
            var source = resolved ? s_built.ToString() : null;
            var payloads = source == null ? null : Outranked(target, consumer, Split(consumer, source));
            var unset = false;
            if (consumer.Property is { } property)
            {
                unset = payloads == null || !AllWrite(payloads, consumer.Sides == null || payloads.Length == 1 ? property : null);
                payloads = unset ? null : payloads;
            }
            else if (payloads == null && consumer.UnsetUtility != null)
            {
                payloads = new[] { consumer.UnsetUtility };
            }
            Withdraw(target, consumer);
            consumer.Source = source;
            consumer.Resolved = true;
            consumer.Applied = payloads;
            consumer.Unset = unset;
            if (unset)
            {
                StyleArbitraryValueResolver.Apply(target, UnsetOf(consumer), consumer.Key);
            }
            else if (payloads != null)
            {
                StyleVariantPayload.Apply(target, payloads, true, consumer.Priority, consumer.Context,
                    declarations: Repeat(consumer.Declaration, payloads.Length));
            }
            SettleWritten(target, consumer, WrittenBy(consumer.Applied, unset ? consumer.Property : null));
        }

        // A consumer on the base layer shares it with the literal base utilities writing the same property, so
        // whichever of them ranks last is put back once this one is gone.
        private static void Withdraw(VisualElement target, Consumer consumer)
        {
            var (applied, unset) = (consumer.Applied, consumer.Unset);
            consumer.Applied = null;
            consumer.Unset = false;
            consumer.Inherited = null;
            if (unset)
            {
                StyleArbitraryValueResolver.Clear(target, consumer.Property!.Value, consumer.Key);
            }
            else if (applied != null)
            {
                StyleVariantPayload.Apply(target, applied, false, consumer.Priority, consumer.Context,
                    declarations: Repeat(consumer.Declaration, applied.Length));
            }
            SettleWritten(target, consumer, WrittenBy(applied, unset ? consumer.Property : null));
        }

        private static List<ArbitraryProperty>? WrittenBy(string[]? payloads, ArbitraryProperty? unset)
        {
            List<ArbitraryProperty>? written = null;
            if (unset is { } property)
            {
                (written = new List<ArbitraryProperty>()).Add(property);
            }
            foreach (var payload in payloads ?? Array.Empty<string>())
            {
                if (StyleArbitraryValueResolver.TryParseLayer(payload, out var parsed))
                {
                    (written ??= new List<ArbitraryProperty>()).Add(parsed.Property);
                }
            }
            return written;
        }

        private static void SettleWritten(VisualElement target, Consumer consumer, List<ArbitraryProperty>? written)
        {
            if (written == null || !consumer.OnBaseLayer)
            {
                return;
            }
            foreach (var property in written)
            {
                Settle(target, property, consumer.Key);
            }
        }

        // The base layer holds one value per property, which every base utility writing it shares: re-applies the
        // one Tailwind emits last among the element's literal utilities and its consumers, so the order they were
        // applied in does not decide.
        private static void Settle(VisualElement target, ArbitraryProperty property, long key)
        {
            var best = -1;
            ArbitraryStyle? winner = null;
            if (s_classes.TryGetValue(target, out var classes))
            {
                for (var i = 0; i < classes.Length; i++)
                {
                    var raw = classes[i];
                    if (!FiberNodePatcher.IsInlineResolved(raw))
                    {
                        continue;
                    }
                    var core = StyleArbitraryValueResolver.StripImportant(raw, out var important);
                    if ((important ? StyleLayerPriority.ImportantOf(StyleLayerPriority.Base) : StyleLayerPriority.Base) != key
                        || !StyleArbitraryValueResolver.TryParseLayer(core, out var style) || style.Property != property)
                    {
                        continue;
                    }
                    var order = StyleRuleOrder.OrdinalOf(classes, i);
                    if (order >= best)
                    {
                        (best, winner) = (order, style);
                    }
                }
            }
            if (s_consumers.TryGetValue(target, out var consumers))
            {
                foreach (var consumer in consumers.Entries)
                {
                    if (consumer.Key != key || consumer.Order < best)
                    {
                        continue;
                    }
                    if (consumer.Unset && consumer.Property == property)
                    {
                        (best, winner) = (consumer.Order, UnsetOf(consumer));
                        continue;
                    }
                    foreach (var payload in consumer.Applied ?? Array.Empty<string>())
                    {
                        if (StyleArbitraryValueResolver.TryParseLayer(payload, out var style) && style.Property == property)
                        {
                            (best, winner) = (consumer.Order, style);
                        }
                    }
                }
            }
            if (winner is { } settled)
            {
                StyleArbitraryValueResolver.Apply(target, settled, key);
            }
        }

        // A split shorthand's sides each write a longhand, which the cascade at the shared base layer ranks above a
        // shorthand or axis utility of the same family whatever their rule order: a side another base utility
        // Tailwind emits later also writes is left to it.
        private static string[]? Outranked(VisualElement target, Consumer consumer, string[]? payloads)
        {
            if (payloads == null || payloads.Length < 2 || !consumer.OnBaseLayer)
            {
                return payloads;
            }
            var later = LaterLonghands(target, consumer);
            if (later.IsEmpty)
            {
                return payloads;
            }
            var kept = new List<string>(payloads.Length);
            foreach (var payload in payloads)
            {
                if (!StyleArbitraryValueResolver.TryParseLayer(payload, out var side)
                    || later.Union(StyleArbitraryLonghands.Of(side.Property)) != later)
                {
                    kept.Add(payload);
                }
            }
            return kept.ToArray();
        }

        private static StyleLonghandSet LaterLonghands(VisualElement target, Consumer consumer)
        {
            var later = StyleLonghandSet.Empty;
            if (s_classes.TryGetValue(target, out var classes))
            {
                for (var i = 0; i < classes.Length; i++)
                {
                    var core = StyleArbitraryValueResolver.StripImportant(classes[i], out var important);
                    if (FiberNodePatcher.IsInlineResolved(classes[i])
                        && (important ? StyleLayerPriority.ImportantOf(StyleLayerPriority.Base) : StyleLayerPriority.Base) == consumer.Key
                        && StyleRuleOrder.OrdinalOf(classes, i) > consumer.Order
                        && StyleArbitraryValueResolver.TryParseLayer(core, out var style))
                    {
                        later = later.Union(StyleArbitraryLonghands.Of(style.Property));
                    }
                }
            }
            if (s_consumers.TryGetValue(target, out var consumers))
            {
                foreach (var other in consumers.Entries)
                {
                    if (other != consumer && other.Key == consumer.Key && other.Order > consumer.Order)
                    {
                        foreach (var property in WrittenBy(other.Applied, other.Unset ? other.Property : null)
                            ?? new List<ArbitraryProperty>())
                        {
                            later = later.Union(StyleArbitraryLonghands.Of(property));
                        }
                    }
                }
            }
            return later;
        }

        // Resolves element's split shorthands again once a reconcile has applied its class list, which may have
        // added a utility Tailwind emits after one of them (Outranked).
        public static void RerankSplit(VisualElement element)
        {
            if (!s_consumers.TryGetValue(element, out var consumers))
            {
                return;
            }
            foreach (var consumer in consumers.Entries.ToArray())
            {
                if (consumer.Sides != null && consumer.OnBaseLayer)
                {
                    consumer.Resolved = false;
                    Reresolve(element, consumer);
                }
            }
        }

        // Re-ranks element's base-layer consumers against the literal base utilities the reconciler has just
        // re-applied, for the properties writing one of longhands, or every one when longhands is null.
        public static void ReassertBase(VisualElement element, StyleLonghandSet? longhands)
        {
            if (!s_consumers.TryGetValue(element, out var consumers))
            {
                return;
            }
            foreach (var consumer in consumers.Entries.ToArray())
            {
                if (!consumer.OnBaseLayer)
                {
                    continue;
                }
                foreach (var property in WrittenBy(consumer.Applied, consumer.Unset ? consumer.Property : null)
                    ?? new List<ArbitraryProperty>())
                {
                    if (longhands is not { } affected || StyleArbitraryLonghands.Of(property).Overlaps(affected))
                    {
                        Settle(element, property, consumer.Key);
                    }
                }
            }
        }
    }
}
