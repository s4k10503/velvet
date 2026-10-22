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
    internal static class StyleCustomProperties
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

        private static int[] Repeat(int declaration, int count)
        {
            var declarations = new int[count];
            for (var i = 0; i < count; i++)
            {
                declarations[i] = declaration;
            }
            return declarations;
        }

        // The payloads a resolution applies: the resolved utility, or for a shorthand holding several values its
        // sides, in CSS's order of one to four values (two for gap). Null where the value count is one the
        // shorthand does not take, or where a modifier read from a var() is not a percentage, which is what
        // color-mix() takes it as.
        private static string[]? Split(Consumer consumer, string source)
        {
            if (consumer.ModifierReadsVariable)
            {
                var modifier = source.LastIndexOf("/[", StringComparison.Ordinal);
                if (modifier < 0 || source.Length < modifier + 4 || source[source.Length - 2] != '%')
                {
                    return null;
                }
            }
            if (consumer.Sides is not { } sides)
            {
                return new[] { source };
            }
            var shape = UtilityShape.Of(source);
            var values = SplitValues(source, shape.BodyStart, shape.MainClose);
            if (values.Count <= 1)
            {
                return new[] { source };
            }
            if (values.Count > sides.Length)
            {
                return null;
            }
            var hint = shape.Hint == null ? string.Empty : shape.Hint + ":";
            var payloads = new string[sides.Length];
            for (var side = 0; side < sides.Length; side++)
            {
                // Top, right, bottom, left: a missing right is the top, a missing bottom the top and a missing
                // left the right.
                var index = side < values.Count ? side : side == 3 && values.Count > 1 ? 1 : 0;
                payloads[side] = sides[side] + "[" + hint + values[index] + "]";
            }
            return payloads;
        }

        private static List<string> SplitValues(string text, int start, int end)
        {
            var values = new List<string>();
            var depth = 0;
            var from = start;
            for (var i = start; i <= end; i++)
            {
                var c = i < end ? text[i] : '_';
                depth += c == '(' ? 1 : c == ')' ? -1 : 0;
                if (depth != 0 || c != '_' || (i > start && text[i - 1] == '\\'))
                {
                    continue;
                }
                if (i > from)
                {
                    values.Add(text.Substring(from, i - from));
                }
                from = i + 1;
            }
            return values;
        }

        // Whether every payload parses to an inline layer, of property where one is given.
        private static bool AllWrite(string[] payloads, ArbitraryProperty? property)
            => Array.TrueForAll(payloads, payload => StyleArbitraryValueResolver.TryParseLayer(payload, out var parsed)
                && (property == null || parsed.Property == property));

        // What an unresolvable var() computes to, as a utility, for the painted utilities where that is not
        // nothing. A gradient stop's colour and position, a box shadow, a ring's shadow and its offset are custom
        // properties Tailwind registers with an initial value: transparent; 0%, 50% and 100%; one that draws none;
        // the same; 0px. A ring's colour is one it registers without one, which its shadow reads with a
        // currentcolor fallback; an outline colour's initial value is currentcolor and its width's is medium (3px).
        // Gap's initial value is no gap. A font's weight and family and a line height inherit.
        private static string? PaintedUnset(string prefix, string? hint)
        {
            if (!s_paintedUnset.TryGetValue(prefix, out var unset))
            {
                return null;
            }
            return hint == null ? unset.Untyped
                : hint == "color" ? unset.Colour
                : hint is "family-name" or "generic-name" ? unset.Family
                : unset.Typed;
        }

        // Per painted utility: what an unresolvable var() stands for when the value is untyped, a colour, a family
        // name, and any other type. An untyped shadow is a box shadow, as Tailwind writes one it does not test.
        private static readonly Dictionary<string, (string? Untyped, string? Colour, string? Family, string? Typed)> s_paintedUnset =
            new(StringComparer.Ordinal)
            {
                ["from-"] = ("from-transparent", "from-transparent", "from-transparent", "from-0%"),
                ["via-"] = ("via-transparent", "via-transparent", "via-transparent", "via-50%"),
                ["to-"] = ("to-transparent", "to-transparent", "to-transparent", "to-100%"),
                ["shadow-"] = ("shadow-none", null, "shadow-none", "shadow-none"),
                ["ring-"] = ("ring-current", "ring-current", "ring-current", "ring-0"),
                ["ring-offset-"] = (null, null, null, "ring-offset-0"),
                ["outline-"] = ("outline-current", "outline-current", "outline-current", "outline-[length:3px]"),
                ["gap-"] = ("gap-0", "gap-0", "gap-0", "gap-0"),
                ["gap-x-"] = ("gap-x-0", "gap-x-0", "gap-x-0", "gap-x-0"),
                ["gap-y-"] = ("gap-y-0", "gap-y-0", "gap-y-0", "gap-y-0"),
                ["font-"] = ("font-[weight:inherit]", "font-[weight:inherit]", "font-[family-name:inherit]", "font-[weight:inherit]"),
                ["leading-"] = ("leading-[inherit]", "leading-[inherit]", "leading-[inherit]", "leading-[inherit]"),
            };

        private static bool Inherits(ArbitraryProperty property)
            => property is ArbitraryProperty.TextColor or ArbitraryProperty.FontSize or ArbitraryProperty.LetterSpacing;

        // CSS's unset: a property that inherits takes its parent's value, and every other property its initial
        // value. The parent's is written as no inline value until the element's own stylesheets are seen to give
        // it another (TrackInherited).
        private static ArbitraryStyle UnsetOf(Consumer consumer)
        {
            var property = consumer.Property!.Value;
            return !Inherits(property) ? new ArbitraryStyle(property, StyleKeyword.Initial)
                : consumer.Inherited ?? new ArbitraryStyle(property, StyleKeyword.Null);
        }

        // An inherited unset left to the engine shows the parent's value unless a rule of the element's own sets
        // the property, which the browser's unset does not let through: the parent's value is then written, and
        // followed as it changes.
        private static void TrackInherited(VisualElement target, Consumer consumer)
        {
            var parent = target.hierarchy.parent;
            if (parent == null || target.panel == null)
            {
                return;
            }
            var property = consumer.Property!.Value;
            var inherited = ResolvedOf(parent, property);
            var shown = consumer.Inherited ?? ResolvedOf(target, property);
            if (SameValue(shown, inherited))
            {
                return;
            }
            consumer.Inherited = inherited;
            StyleArbitraryValueResolver.Apply(target, inherited, consumer.Key);
            SettleWritten(target, consumer, new List<ArbitraryProperty> { property });
        }

        private static ArbitraryStyle ResolvedOf(VisualElement element, ArbitraryProperty property)
        {
            var style = element.resolvedStyle;
            return property == ArbitraryProperty.TextColor ? new ArbitraryStyle(property, style.color)
                : new ArbitraryStyle(property, property == ArbitraryProperty.FontSize ? style.fontSize : style.letterSpacing,
                    LengthUnit.Pixel);
        }

        private static bool SameValue(ArbitraryStyle a, ArbitraryStyle b)
            => a.Keyword == b.Keyword && a.Value == b.Value && a.Color == b.Color;

        private static bool Matches(StringBuilder built, string applied) => built.Equals(applied.AsSpan());

        // The property an inline utility writes, read by parsing it with a value of the type Tailwind gives it in
        // place of its own: a colour where DefaultHint adds one, else the bracket with each var() read as 0, else
        // a length, a number or an angle; a var() in a modifier is read as 50%. Null when none parses to a layer
        // the unset value can be written to, or where keywordOnly is false, to any layer at all. probe is the
        // utility that parsed.
        private static ArbitraryProperty? WrittenProperty(string utility, UtilityShape shape, string? addedHint,
            out string? probe, bool keywordOnly = true)
        {
            probe = null;
            string[] candidates;
            if (shape.MainOpen < 0)
            {
                candidates = new[] { WithVarsAs(utility, "50%") };
            }
            else
            {
                var head = utility.Substring(0, shape.MainOpen + 1);
                var hint = shape.Hint == null ? string.Empty : shape.Hint + ":";
                var tail = "]" + WithVarsAs(utility.Substring(shape.MainClose + 1), "50%");
                var body = utility.Substring(shape.BodyStart, shape.MainClose - shape.BodyStart);
                candidates = addedHint != null
                    ? new[] { head + addedHint + ":" + (addedHint == "color" ? "#000" : "400") + tail }
                    : new[]
                    {
                        head + hint + WithVarsAs(body, "0") + tail, head + hint + "0px" + tail, head + hint + "0" + tail,
                        head + hint + "0deg" + tail,
                    };
            }
            foreach (var candidate in candidates)
            {
                if (StyleArbitraryValueResolver.TryParseLayer(candidate, out var parsed)
                    && (!keywordOnly || StyleArbitraryValueResolver.WritesKeyword(parsed.Property)))
                {
                    probe = candidate;
                    return parsed.Property;
                }
            }
            return null;
        }

        private static string WithVarsAs(string text, string value)
        {
            var written = new StringBuilder();
            for (var at = 0; at < text.Length;)
            {
                var call = IndexOfVar(text, at, text.Length);
                written.Append(text, at, call - at);
                if (call == text.Length)
                {
                    break;
                }
                var close = MatchingClose(text, call + 3, text.Length);
                if (close < 0)
                {
                    break;
                }
                written.Append(value);
                at = close + 1;
            }
            return written.ToString();
        }

        // The hint Tailwind's typing gives an untyped value opening with var(), which its type inference declines
        // to test, so the utility writes its default: a colour for the utilities StyleColorValueParser.
        // DefaultsToColour names, and a weight for font-. Null where the value is typed or tested, and where a
        // modifier follows, which already makes it a colour.
        private static string? DefaultHint(string utility, in UtilityShape shape)
        {
            if (shape.MainOpen < 0 || shape.Hint != null || shape.HasModifier
                || string.Compare(utility, shape.BodyStart, "var(", 0, 4, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return null;
            }
            var root = utility.Substring(utility[0] == '-' ? 1 : 0, shape.MainOpen - (utility[0] == '-' ? 2 : 1));
            return StyleColorValueParser.DefaultsToColour(root) ? "color" : root == "font" ? "weight" : null;
        }

        // The utility with every var() replaced by what it resolves to on element, spaces written as `_` and an
        // underscore as `\_`, and the hint DefaultHint adds. False when a var() resolves to nothing.
        private static bool TryBuild(VisualElement element, Consumer consumer, StringBuilder into)
        {
            var utility = consumer.Utility;
            var from = consumer.Shape.MainOpen >= 0 ? consumer.Shape.MainOpen + 1 : 0;
            into.Append(utility, 0, from);
            if (consumer.AddedHint != null)
            {
                into.Append(consumer.AddedHint).Append(':');
            }
            s_resolving.Clear();
            return TryAppendSubstituted(element, utility, from, utility.Length, encoded: true, into);
        }

        // Appends text[start, end) with each var() in it substituted. encoded text is a class bracket, whose
        // spaces are already `_`; anything else is CSS text.
        private static bool TryAppendSubstituted(VisualElement scope, string text, int start, int end, bool encoded,
            StringBuilder into)
        {
            if (s_depth >= MaxDepth)
            {
                return false;
            }
            s_depth++;
            try
            {
                return TryAppendCalls(scope, text, start, end, encoded, into);
            }
            finally
            {
                s_depth--;
            }
        }

        private static bool TryAppendCalls(VisualElement scope, string text, int start, int end, bool encoded,
            StringBuilder into)
        {
            var at = start;
            while (at < end)
            {
                var call = IndexOfVar(text, at, end);
                AppendText(text, at, call, encoded, into);
                if (call == end)
                {
                    return true;
                }
                var nameStart = call + 4;
                var nameEnd = nameStart;
                while (nameEnd < end && text[nameEnd] != ',' && text[nameEnd] != ')')
                {
                    nameEnd++;
                }
                var close = MatchingClose(text, call + 3, end);
                if (close < 0)
                {
                    return false;
                }
                var name = encoded
                    ? text.Substring(nameStart, nameEnd - nameStart).Trim(' ', '_')
                    : text.Substring(nameStart, nameEnd - nameStart).Trim();
                var mark = into.Length;
                if (!name.StartsWith("--", StringComparison.Ordinal) || !TryAppendVariable(scope, name, into))
                {
                    into.Length = mark;
                    if (nameEnd >= close)
                    {
                        return false;
                    }
                    var (fallbackStart, fallbackEnd) = Trimmed(text, nameEnd + 1, close, encoded);
                    if (!TryAppendSubstituted(scope, text, fallbackStart, fallbackEnd, encoded, into))
                    {
                        return false;
                    }
                }
                at = close + 1;
            }
            return true;
        }

        // Appends the computed value of the custom property name on element: its own declaration, else the
        // nearest ancestor's, with the declaration's var() substituted on the element declaring it.
        private static bool TryAppendVariable(VisualElement element, string name, StringBuilder into)
        {
            for (var scope = element; scope != null;)
            {
                if (!TryOwn(scope, name, out var raw) && !TryStylesheet(scope, name, out raw))
                {
                    scope = scope.hierarchy.parent;
                    continue;
                }
                var value = raw?.Trim();
                if (value == null || value == "initial")
                {
                    return false;
                }
                if (value is "inherit" or "unset")
                {
                    scope = scope.hierarchy.parent;
                    continue;
                }
                if (IndexOfVar(value, 0, value.Length) == value.Length)
                {
                    AppendText(value, 0, value.Length, encoded: false, into);
                    return true;
                }
                foreach (var resolving in s_resolving)
                {
                    if (ReferenceEquals(resolving.Element, scope) && resolving.Name == name)
                    {
                        return false;
                    }
                }
                s_resolving.Add((scope, name));
                var resolved = TryAppendSubstituted(scope, value, 0, value.Length, encoded: false, into);
                s_resolving.RemoveAt(s_resolving.Count - 1);
                return resolved;
            }
            return false;
        }

        private static bool TryOwn(VisualElement element, string name, out string? value)
        {
            value = null;
            if (!s_declared.TryGetValue(element, out var declared) || !declared.TryGetValue(name, out var layers))
            {
                return false;
            }
            value = layers.Values[layers.Count - 1];
            return true;
        }

        private static int IndexOfVar(string text, int from, int end)
        {
            var at = from >= text.Length ? -1 : text.IndexOf("var(", from, end - from, StringComparison.OrdinalIgnoreCase);
            return at < 0 ? end : at;
        }

        // The index of the parenthesis closing the one at open, or -1 when none does before end.
        private static int MatchingClose(string text, int open, int end)
        {
            var depth = 0;
            var at = open;
            for (; at < end; at++)
            {
                depth += text[at] == '(' ? 1 : text[at] == ')' ? -1 : 0;
                if (depth == 0)
                {
                    break;
                }
            }
            return at < end ? at : -1;
        }

        private static (int Start, int End) Trimmed(string text, int start, int end, bool encoded)
        {
            while (start < end && (text[start] == ' ' || (encoded && text[start] == '_')))
            {
                start++;
            }
            while (end > start && (text[end - 1] == ' ' || (encoded && text[end - 1] == '_')))
            {
                end--;
            }
            return (start, end);
        }

        // CSS text is written into a class bracket the way Tailwind reads one back: a space as `_`, an underscore
        // as `\_`.
        private static void AppendText(string text, int start, int end, bool encoded, StringBuilder into)
        {
            for (var i = start; i < end; i++)
            {
                var c = text[i];
                if (encoded)
                {
                    into.Append(c);
                }
                else if (c == '_')
                {
                    into.Append('\\').Append('_');
                }
                else
                {
                    into.Append(char.IsWhiteSpace(c) ? '_' : c);
                }
            }
        }

        #region Stylesheet declarations
        private static readonly FieldInfo? s_variableContext = EngineMember.ElementVariableContext.ResolveField();
        private static readonly PropertyInfo? s_variables = EngineMember.VariableContextVariables.ResolveProperty();
        private static readonly FieldInfo? s_variableName = EngineMember.StyleVariableName.ResolveField();
        private static readonly FieldInfo? s_variableSheet = EngineMember.StyleVariableSheet.ResolveField();
        private static readonly FieldInfo? s_variableHandles = EngineMember.StyleVariableHandles.ResolveField();
        private static readonly PropertyInfo? s_handleKind = EngineMember.StyleValueHandleKind.ResolveProperty();
        private static readonly MethodInfo? s_readAsString = EngineMember.StyleSheetReadAsString.ResolveMethod();
        private static readonly MethodInfo? s_readColor = EngineMember.StyleSheetReadColor.ResolveMethod();
        private static readonly MethodInfo? s_readFloat = EngineMember.StyleSheetReadFloat.ResolveMethod();

        // One reading per variable context: the engine shares a context between every element whose matched rules
        // declare the same variables, and builds a new one when they change.
        private static readonly ConditionalWeakTable<object, Dictionary<string, (string? Text, object Identity)>> s_contexts = new();
        private static readonly ConditionalWeakTable<object, Dictionary<string, (string? Text, object Identity)>>.CreateValueCallback
            s_readContext = ReadContext;

        // A custom property a stylesheet declares on element itself rather than one it inherits: element's
        // variable context holds it and its parent's does not hold the same declaration. A value Velvet cannot
        // spell (an asset reference) is read as null.
        private static bool TryStylesheet(VisualElement element, string name, out string? value)
        {
            value = null;
            if (!TryReadContext(element, out var context) || !context.TryGetValue(name, out var own))
            {
                return false;
            }
            var parent = element.hierarchy.parent;
            var inherited = parent == null ? null
                : ReferenceEquals(parent, s_traversed) ? Read(s_traversedPrevious)
                : TryReadContext(parent, out var read) ? read
                : null;
            if (inherited != null && inherited.TryGetValue(name, out var outer) && ReferenceEquals(outer.Identity, own.Identity))
            {
                return false;
            }
            value = own.Text;
            return true;
        }

        // The element OnCustomStyle is handling and the context it held before the traversal rebuilt it.
        private static VisualElement? s_traversed;
        private static object? s_traversedPrevious;

        // The context last read on each element, which OnCustomStyle hands TryStylesheet as the declaring element's
        // previous one. An element is listened to from its first reading.
        private static readonly ConditionalWeakTable<VisualElement, object> s_seen = new();
        private static readonly EventCallback<CustomStyleResolvedEvent> s_onCustomStyle = OnCustomStyle;

        private static Dictionary<string, (string? Text, object Identity)>? Read(object? handle)
            => handle == null ? null : s_contexts.GetValue(handle, s_readContext);

        private static bool TryReadContext(VisualElement element, out Dictionary<string, (string? Text, object Identity)> context)
        {
            context = null!;
            if (s_variableContext?.GetValue(element) is not { } handle)
            {
                return false;
            }
            if (!s_seen.TryGetValue(element, out var seen))
            {
                element.RegisterCallback(s_onCustomStyle);
                s_seen.Add(element, handle);
            }
            else if (!ReferenceEquals(seen, handle))
            {
                s_seen.Remove(element);
                s_seen.Add(element, handle);
            }
            context = s_contexts.GetValue(handle, s_readContext);
            return true;
        }

        private static Dictionary<string, (string? Text, object Identity)> ReadContext(object handle)
        {
            var read = new Dictionary<string, (string? Text, object Identity)>(StringComparer.Ordinal);
            if (s_variables?.GetValue(handle) is not IList variables || s_variableName == null
                || s_variableSheet == null || s_variableHandles == null)
            {
                return read;
            }
            // Later entries override earlier ones, as the engine's own lookup reads them from the end.
            foreach (var variable in variables)
            {
                if (s_variableName.GetValue(variable) is string name
                    && s_variableHandles.GetValue(variable) is Array handles)
                {
                    read[name] = (Spell(s_variableSheet.GetValue(variable), handles), handles);
                }
            }
            return read;
        }

        // A declaration's value handles as CSS text: numbers, dimensions, colours, keywords, names and strings,
        // commas, resource() paths and functions with their arguments. A var() with a fallback is stored with the
        // fallback as the rest of the declaration, so its parenthesis closes at the end.
        private static string? Spell(object? sheet, Array handles)
        {
            if (sheet == null || s_handleKind == null || s_readAsString == null || s_readColor == null || s_readFloat == null)
            {
                return null;
            }
            var text = new StringBuilder();
            var opened = 0;
            var index = 0;
            if (!SpellRange(sheet, handles, ref index, handles.Length, text, ref opened))
            {
                return null;
            }
            text.Append(')', opened);
            return text.ToString();
        }

        private static bool SpellRange(object sheet, Array handles, ref int index, int end, StringBuilder text, ref int opened)
        {
            for (; index < end; index++)
            {
                var handle = handles.GetValue(index);
                var kind = s_handleKind!.GetValue(handle)?.ToString();
                if (kind == "CommaSeparator")
                {
                    text.Append(',');
                    continue;
                }
                Separate(text);
                if (kind != "Function")
                {
                    var token = SpellValue(sheet, handle, kind);
                    if (token == null)
                    {
                        return false;
                    }
                    text.Append(token);
                    continue;
                }
                if (index + 1 >= handles.Length)
                {
                    return false;
                }
                var name = s_readAsString!.Invoke(null, new[] { sheet, handle }) as string;
                var arguments = (int)(float)s_readFloat!.Invoke(sheet, new[] { handles.GetValue(index + 1) })!;
                text.Append(name).Append('(');
                index += 2;
                if (name == "var")
                {
                    var spelled = SpellValue(sheet, handles.GetValue(index), "Variable");
                    text.Append(spelled);
                    if (arguments > 1)
                    {
                        opened++;
                    }
                    else
                    {
                        text.Append(')');
                    }
                    continue;
                }
                var argumentsEnd = Math.Min(handles.Length, index + arguments);
                if (!SpellRange(sheet, handles, ref index, argumentsEnd, text, ref opened))
                {
                    return false;
                }
                text.Append(')');
                index--;
            }
            return true;
        }

        private static void Separate(StringBuilder text)
        {
            if (text.Length > 0 && text[text.Length - 1] != ',' && text[text.Length - 1] != '(')
            {
                text.Append(' ');
            }
        }

        private static string? SpellValue(object sheet, object? handle, string? kind)
            => kind switch
            {
                "Float" or "Dimension" or "Keyword" or "Enum" or "String" or "Variable"
                    => s_readAsString!.Invoke(null, new[] { sheet, handle }) as string,
                "ResourcePath" => "resource(\"" + s_readAsString!.Invoke(null, new[] { sheet, handle }) + "\")",
                "Color" => Hex((Color)s_readColor!.Invoke(sheet, new[] { handle })!),
                _ => null,
            };

        private static string Hex(Color color)
        {
            var c = (Color32)color;
            return string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}{3:x2}", c.r, c.g, c.b, c.a);
        }
        #endregion
    }
}
