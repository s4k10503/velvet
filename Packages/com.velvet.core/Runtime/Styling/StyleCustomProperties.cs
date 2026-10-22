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
    // consumer is resolved on its element into the utility it would be with each var() replaced, and that utility
    // is applied as a payload at the consumer's own layer. A consumer that cannot be resolved, or whose result the
    // utility's property rejects, is invalid at computed-value time: one writing a style property writes that
    // property's unset value, and one a class-driven pass paints (a gradient stop, a shadow) applies nothing.
    internal static class StyleCustomProperties
    {
        // Nesting of var() through fallbacks and declarations past this depth is declined rather than followed.
        private const int MaxDepth = 32;

        // The tick RelativeLengthPoll polls at.
        private const long TickMs = 16;

        private static readonly ConditionalWeakTable<VisualElement, Dictionary<string, SortedList<long, string>>> s_declared = new();
        private static readonly ConditionalWeakTable<VisualElement, Consumers> s_consumers = new();
        private static readonly EventCallback<AttachToPanelEvent> s_onAttach = OnAttach;

        // Reused by every resolution: one runs alone and nothing it calls resolves re-entrantly.
        private static readonly StringBuilder s_built = new();
        private static readonly List<(VisualElement Element, string Name)> s_resolving = new();
        private static int s_depth;
        private static int s_held;
        private static readonly List<VisualElement> s_heldRoots = new();

        // The number of elements holding a consumer, so a declaration change on a tree with none walks nothing.
        private static int s_consumerElements;

        private sealed class Consumers
        {
            public readonly List<Consumer> Entries = new();
            public IVisualElementScheduledItem? Poll;
            // The variable context the poll last resolved against. The engine hands an element a new context
            // whenever a variable in scope of it changes, so an unchanged one leaves nothing for the poll to do.
            public object? Context;
        }

        // The class list a reconcile last applied to the element, whose literal base utilities a base-layer
        // consumer hands its layer back to (Withdraw).
        private static readonly ConditionalWeakTable<VisualElement, string[]> s_classes = new();

        private sealed class Consumer
        {
            public readonly string Utility;
            public readonly long Priority;
            public readonly int Declaration;
            public readonly ReconcilerContext? Context;
            // The property the utility writes as an inline layer (WrittenProperty), which an unresolvable var()
            // writes the unset value of. Null for a utility that reads no var(), whose value is as written.
            public readonly ArbitraryProperty? Property;
            // For a utility a class-driven pass paints, the one standing for what an unresolvable var() computes
            // to (PaintedUnset).
            public readonly string? UnsetUtility;
            // Whether the resolved bracket is written behind a colour hint (ColourByDefault).
            public readonly bool AddsColourHint;
            public string? Applied;
            public bool Unset;
            // The last resolution the property rejected, so a poll that finds it again writes nothing.
            public string? Rejected;

            public Consumer(string utility, long priority, int declaration, ReconcilerContext? context)
            {
                Utility = utility;
                Priority = priority;
                Declaration = declaration;
                Context = context;
                var open = utility.IndexOf('[');
                AddsColourHint = ColourByDefault(utility, open, utility.Substring(open + 1, utility.Length - open - 2),
                    out var hint) && hint == null;
                if (utility.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Property = WrittenProperty(utility);
                    UnsetUtility = Property == null ? PaintedUnset(utility.Substring(0, open), hint) : null;
                }
            }

            public long Key => StyleLayerPriority.WithRule(Priority, Declaration);

            // The layer the element's literal base utilities are applied at (ApplyClassToken), which this shares.
            public bool OnBaseLayer => (Key & ~StyleLayerPriority.Important) == StyleLayerPriority.Base;
        }

        // Applies (on) or withdraws one token StyleCustomPropertyClass.IsCustomPropertyToken accepts, at the
        // priority and rule place a payload carries (StyleVariantPayload.Apply).
        public static void Toggle(VisualElement target, string core, bool on, long priority, int declaration,
            ReconcilerContext? context)
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
                Bind(target, new Consumer(utility, priority, declaration, context));
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

        // A stylesheet's declarations move at a style pass, whose custom-style event the engine sends only to an
        // element matching a rule that declares one, so the panel's scheduler asks again each tick; attaching to a
        // panel is when the element first has the ancestors its declarations come from.
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

        private static void Poll(VisualElement target)
        {
            if (!s_consumers.TryGetValue(target, out var consumers))
            {
                return;
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
            if (resolved && ((consumer.Applied != null && Matches(s_built, consumer.Applied))
                    || (consumer.Unset && consumer.Rejected != null && Matches(s_built, consumer.Rejected))))
            {
                return;
            }
            var payload = resolved ? s_built.ToString() : null;
            var unset = false;
            if (consumer.Property is { } property)
            {
                unset = payload == null || !StyleArbitraryValueResolver.TryParseLayer(payload, out var parsed)
                    || parsed.Property != property;
                consumer.Rejected = unset ? payload : null;
                payload = unset ? null : payload;
            }
            else if (payload == null)
            {
                payload = consumer.UnsetUtility;
            }
            if (payload == consumer.Applied && unset == consumer.Unset)
            {
                return;
            }
            Withdraw(target, consumer);
            consumer.Applied = payload;
            consumer.Unset = unset;
            if (unset)
            {
                StyleArbitraryValueResolver.Apply(target, UnsetOf(consumer.Property!.Value), consumer.Key);
            }
            else if (payload != null)
            {
                StyleVariantPayload.Apply(target, new[] { payload }, true, consumer.Priority, consumer.Context,
                    declarations: new[] { consumer.Declaration });
            }
        }

        // A consumer on the base layer shares it with the literal base utilities writing the same property, so
        // what it clears is handed back to them.
        private static void Withdraw(VisualElement target, Consumer consumer)
        {
            var (applied, unset) = (consumer.Applied, consumer.Unset);
            consumer.Applied = null;
            consumer.Unset = false;
            ArbitraryProperty? written = null;
            if (unset)
            {
                written = consumer.Property!.Value;
                StyleArbitraryValueResolver.Clear(target, written.Value, consumer.Key);
            }
            else if (applied != null)
            {
                if (StyleArbitraryValueResolver.TryParseLayer(applied, out var parsed))
                {
                    written = parsed.Property;
                }
                StyleVariantPayload.Apply(target, new[] { applied }, false, consumer.Priority, consumer.Context,
                    declarations: new[] { consumer.Declaration });
            }
            if (written != null && consumer.OnBaseLayer && s_classes.TryGetValue(target, out var classes))
            {
                FiberNodePatcher.ReapplyArbitraryValues(target, classes,
                    affectedLonghands: StyleArbitraryLonghands.Of(written.Value));
            }
        }

        // Records the class list a reconcile applied to element, for Withdraw.
        public static void RecordClasses(VisualElement element, string[] classNames)
        {
            s_classes.Remove(element);
            s_classes.Add(element, classNames);
        }

        // Re-applies element's base-layer consumers writing one of longhands, or every one when longhands is null,
        // after the reconciler re-applied the literal base utilities over the layer they share.
        public static void ReassertBase(VisualElement element, StyleLonghandSet? longhands)
        {
            if (!s_consumers.TryGetValue(element, out var consumers))
            {
                return;
            }
            foreach (var consumer in consumers.Entries)
            {
                if (!consumer.OnBaseLayer)
                {
                    continue;
                }
                ArbitraryStyle style;
                if (consumer.Unset)
                {
                    style = UnsetOf(consumer.Property!.Value);
                }
                else if (consumer.Applied == null || !StyleArbitraryValueResolver.TryParseLayer(consumer.Applied, out style))
                {
                    continue;
                }
                if (longhands is not { } affected || StyleArbitraryLonghands.Of(style.Property).Overlaps(affected))
                {
                    StyleArbitraryValueResolver.Apply(element, style, consumer.Key);
                }
            }
        }

        // What an unresolvable var() computes to, as a utility, for the painted utilities where that is not
        // nothing: a gradient stop's colour and position are custom properties Tailwind registers with a
        // transparent initial value and with 0%, 50% and 100%, a box shadow's one with an initial value that
        // draws none, and gap's initial value is no gap.
        private static string? PaintedUnset(string prefix, string? hint)
            => prefix switch
            {
                "from-" when hint is "length" or "percentage" => "from-0%",
                "via-" when hint is "length" or "percentage" => "via-50%",
                "to-" when hint is "length" or "percentage" => "to-100%",
                "from-" or "via-" or "to-" => prefix + "transparent",
                "shadow-" when hint != "color" => "shadow-none",
                "gap-" or "gap-x-" or "gap-y-" => prefix + "0",
                _ => null,
            };

        // CSS's unset: a property that inherits takes its parent's value, written as no inline value, and every
        // other property its initial value.
        private static ArbitraryStyle UnsetOf(ArbitraryProperty property)
            => new(property, property is ArbitraryProperty.TextColor or ArbitraryProperty.FontSize
                or ArbitraryProperty.LetterSpacing
                ? StyleKeyword.Null
                : StyleKeyword.Initial);

        private static bool Matches(StringBuilder built, string applied) => built.Equals(applied.AsSpan());

        // The property an inline utility writes, read by parsing it with a value of the type Tailwind gives it in
        // place of its own: a colour where ColourByDefault holds, else the bracket with each var() read as 0, else
        // a length, a number or an angle. Null when none parses to a layer the unset value can be written to.
        private static ArbitraryProperty? WrittenProperty(string utility)
        {
            var open = utility.IndexOf('[');
            var body = utility.Substring(open + 1, utility.Length - open - 2);
            var probes = ColourByDefault(utility, open, body, out var hint)
                ? new[] { "color:#000" }
                : new[] { WithVarsAsZero(body), "0px", "0", "0deg" };
            foreach (var probe in probes)
            {
                var value = hint != null && !probe.StartsWith(hint + ":", StringComparison.Ordinal) && probe != "color:#000"
                    ? hint + ":" + probe
                    : probe;
                if (StyleArbitraryValueResolver.TryParseLayer(utility.Substring(0, open + 1) + value + "]", out var parsed)
                    && StyleArbitraryValueResolver.WritesKeyword(parsed.Property))
                {
                    return parsed.Property;
                }
            }
            return null;
        }

        private static string WithVarsAsZero(string body)
        {
            var text = new StringBuilder();
            for (var at = 0; at < body.Length;)
            {
                var call = IndexOfVar(body, at, body.Length);
                text.Append(body, at, call - at);
                if (call == body.Length)
                {
                    break;
                }
                var close = MatchingClose(body, call + 3, body.Length);
                if (close < 0)
                {
                    break;
                }
                text.Append('0');
                at = close + 1;
            }
            return text.ToString();
        }

        // Whether Tailwind writes this bracket as a colour without testing it: a utility whose untyped value
        // defaults to a colour, holding a value that opens with var(), which Tailwind's type inference declines
        // to test.
        private static bool ColourByDefault(string utility, int open, string body, out string? hint)
        {
            StyleArbitraryValueResolver.TrySplitTypeHint(body.AsSpan(), out hint, out var value);
            var root = utility.Substring(utility[0] == '-' ? 1 : 0, open - (utility[0] == '-' ? 2 : 1));
            return (hint == null || hint == "color")
                && StyleColorValueParser.DefaultsToColour(root)
                && (hint != null || value.TrimStart("_ ".AsSpan()).StartsWith("var(".AsSpan(), StringComparison.OrdinalIgnoreCase));
        }

        // The utility with every var() in its bracket replaced by what it resolves to on element, spaces written
        // as `_`, and a colour hint added where ColourByDefault holds. False when a var() resolves to nothing.
        private static bool TryBuild(VisualElement element, Consumer consumer, StringBuilder into)
        {
            var utility = consumer.Utility;
            var open = utility.IndexOf('[');
            var close = utility.Length - 1;
            into.Append(utility, 0, open + 1);
            if (consumer.AddsColourHint)
            {
                into.Append("color:");
            }
            s_resolving.Clear();
            if (!TryAppendSubstituted(element, utility, open + 1, close, encoded: true, into))
            {
                return false;
            }
            into.Append(']');
            return true;
        }

        // Appends text[start, end) with each var() in it substituted. encoded text is a class bracket, whose
        // spaces are already `_`; anything else is CSS text, whose spaces are written as `_`.
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

        private static void AppendText(string text, int start, int end, bool encoded, StringBuilder into)
        {
            for (var i = start; i < end; i++)
            {
                var c = text[i];
                into.Append(!encoded && char.IsWhiteSpace(c) ? '_' : c);
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
        // spell (an asset, a function other than var()) is read as null.
        private static bool TryStylesheet(VisualElement element, string name, out string? value)
        {
            value = null;
            if (!TryReadContext(element, out var context) || !context.TryGetValue(name, out var own))
            {
                return false;
            }
            var parent = element.hierarchy.parent;
            if (parent != null && TryReadContext(parent, out var inherited) && inherited.TryGetValue(name, out var outer)
                && ReferenceEquals(outer.Identity, own.Identity))
            {
                return false;
            }
            value = own.Text;
            return true;
        }

        private static bool TryReadContext(VisualElement element, out Dictionary<string, (string? Text, object Identity)> context)
        {
            context = null!;
            if (s_variableContext?.GetValue(element) is not { } handle)
            {
                return false;
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

        // A declaration's value handles as CSS text: numbers, dimensions, colours, keywords and names, commas, and
        // a var() with its fallback, which the engine stores as its last argument.
        private static string? Spell(object? sheet, Array handles)
        {
            if (sheet == null || s_handleKind == null || s_readAsString == null || s_readColor == null || s_readFloat == null)
            {
                return null;
            }
            var text = new StringBuilder();
            var opened = 0;
            for (var i = 0; i < handles.Length; i++)
            {
                var handle = handles.GetValue(i);
                var kind = s_handleKind.GetValue(handle)?.ToString();
                if (kind == "CommaSeparator")
                {
                    text.Append(',');
                    continue;
                }
                var token = kind == "Function" ? SpellVar(sheet, handles, ref i, ref opened) : SpellValue(sheet, handle, kind);
                if (token == null)
                {
                    return null;
                }
                if (text.Length > 0 && text[text.Length - 1] != ',')
                {
                    text.Append(' ');
                }
                text.Append(token);
            }
            text.Append(')', opened);
            return text.ToString();
        }

        private static string? SpellValue(object sheet, object handle, string? kind)
            => kind switch
            {
                "Float" or "Dimension" or "Keyword" or "Enum" or "String" or "Variable"
                    => s_readAsString!.Invoke(null, new[] { sheet, handle }) as string,
                "Color" => Hex((Color)s_readColor!.Invoke(sheet, new[] { handle })!),
                _ => null,
            };

        // `var(` and the name, leaving the parenthesis open when a fallback follows: the engine stores the fallback
        // as the rest of the declaration, so it closes at the end.
        private static string? SpellVar(object sheet, Array handles, ref int i, ref int opened)
        {
            if (s_readAsString!.Invoke(null, new[] { sheet, handles.GetValue(i) }) as string != "var" || i + 2 >= handles.Length)
            {
                return null;
            }
            var arguments = (int)(float)s_readFloat!.Invoke(sheet, new[] { handles.GetValue(i + 1) })!;
            var token = "var(" + s_readAsString.Invoke(null, new[] { sheet, handles.GetValue(i + 2) });
            i += 2;
            if (arguments > 1)
            {
                opened++;
                return token;
            }
            return token + ")";
        }

        private static string Hex(Color color)
        {
            var c = (Color32)color;
            return string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}{3:x2}", c.r, c.g, c.b, c.a);
        }
        #endregion
    }
}
