#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The custom properties the stylesheets at or above a mount's theme root declare, which StyleThemeClass reads as
    // Tailwind v4 reads the variables of an @theme block: a declared `--color-brand` makes bg-brand, text-brand and
    // the other colour utilities, whatever rule or theme class declares it. Only the names are held: the value is
    // read from the element when the utility resolves (StyleCustomProperties), so a theme class that redeclares a
    // token changes what the utility draws. Each mount holds its own set, rebuilt when a sheet at or above its root
    // is attached, removed or changes.
    internal static class StyleThemeTokens
    {
        private static readonly PropertyInfo? s_rules = EngineMember.StyleSheetRules.ResolveProperty();
        private static readonly PropertyInfo? s_properties = EngineMember.StyleRuleProperties.ResolveProperty();
        private static readonly PropertyInfo? s_name = EngineMember.StylePropertyName.ResolveProperty();
        private static readonly PropertyInfo? s_values = EngineMember.StylePropertyValues.ResolveProperty();

        internal sealed class Scope
        {
            public readonly HashSet<string> Project = new(StringComparer.Ordinal);
            public readonly HashSet<string> Bundled = new(StringComparer.Ordinal);
            // Namespaces a project sheet sets to initial (`--color-*: initial`, as "color-"; `--*: initial` as "all-").
            public readonly HashSet<string> Cleared = new(StringComparer.Ordinal);
            // The classes StyleThemeClass has read against these names, dropped with them.
            public readonly Dictionary<string, string?> Classes = new(StringComparer.Ordinal);
            public int Signature = int.MinValue;
            // Moves whenever a rebuild finds the sheets changed.
            public int Version;
            public bool Polling;
            public IVisualElementScheduledItem? PollItem;

            // The last value a project sheet declares for each --breakpoint-*, --container-* and --animate-* name,
            // as CSS text: what a reader holding no element falls back to.
            public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);

            // How many declarations the project sheets make of each name in Values.
            public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);

            // The length the one declaration of the variable gives, where a single rule declares it: every element
            // reads that value, so a utility can be written with the literal.
            public bool TryGetSinglePx(string property, out float px)
            {
                px = 0f;
                return Counts.TryGetValue(property, out var count) && count == 1 && Values.TryGetValue(property, out var text)
                    && StyleArbitraryValueResolver.TryParseArbitraryPixels(("[" + text + "]").AsSpan(), out px);
            }

            // A length the variable holds: the one the cascade gives the element being read for (Use), else the last a
            // project sheet declared.
            public bool TryGetPx(string property, out float px)
            {
                px = 0f;
                string? text = null;
                // An element reads what the cascade gives it and nothing else: a value declared under a selector it
                // does not match is not its own. A reader holding no element (a class being parsed) reads the last
                // value any rule declares.
                if (s_element != null ? !StyleCustomProperties.TryStylesheetValue(s_element, property, out text)
                    : !Values.TryGetValue(property, out text))
                {
                    return false;
                }
                if (text == null)
                {
                    return false;
                }
                return StyleArbitraryValueResolver.TryParseArbitraryPixels(("[" + text + "]").AsSpan(), out px);
            }

            public bool ClearsNamespace(string ns) => Cleared.Contains(ns) || Cleared.Contains("all-");

            // Whether name in the namespace (such as "color-") is a token of the bundled sheets that its namespace's
            // initial removes: not one the project declares itself.
            public bool ClearsBundled(string ns, string name)
                => ClearsNamespace(ns) && !Project.Contains("--" + ns + name)
                    && (Bundled.Contains("--" + ns + name) || (ns == "color-" && name is "white" or "black")
                        || StyleThemeClass.IsBuiltInName(ns, name));
        }

        private sealed class Read
        {
            public int ContentHash;
            public string[] Names = Array.Empty<string>();
            public readonly List<string> Cleared = new();
            public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
        }

        private static readonly ConditionalWeakTable<StyleSheet, Read> s_reads = new();
        private static readonly ConditionalWeakTable<ReconcilerContext, Scope> s_scopes = new();
        private static Scope? s_current;
        private static VisualElement? s_element;

        // Whether the mount's sheets declare a value a width-driven variant reads, so its variants listen for the
        // element's custom styles resolving (a theme class toggled above it changes what it reads).
        internal static bool ReadsValues(ReconcilerContext ctx)
            => s_scopes.TryGetValue(ctx, out var scope) && scope.Values.Count > 0;

        // Raised after a rebuild finds the sheets a mount reads changed; readers re-evaluate what they took from them.
        internal static event Action<ReconcilerContext>? ScopeChanged;

        private const long PollMs = 100;

        private sealed class Themed
        {
            public readonly List<WeakReference<VisualElement>> Elements = new();
            public readonly ConditionalWeakTable<VisualElement, string[]> Classes = new();
            public int PruneAt = 64;
        }

        private static readonly ConditionalWeakTable<ReconcilerContext, Themed> s_themed = new();

        // Remembers an element whose class list holds a class a theme variable could make, so it is configured again
        // when the sheets change what the variables declare.
        // The context an element is tracked under, so its record goes with the element (Untrack).
        private static readonly ConditionalWeakTable<VisualElement, ReconcilerContext> s_trackedBy = new();

        internal static void Track(ReconcilerContext ctx, VisualElement element, string[] classNames)
        {
            var themed = s_themed.GetValue(ctx, static _ => new Themed());
            if (!themed.Classes.TryGetValue(element, out _))
            {
                if (themed.Elements.Count >= themed.PruneAt)
                {
                    themed.Elements.RemoveAll(static reference => !reference.TryGetTarget(out _));
                    themed.PruneAt = Math.Max(64, themed.Elements.Count * 2);
                }
                themed.Elements.Add(new WeakReference<VisualElement>(element));
            }
            themed.Classes.Remove(element);
            themed.Classes.Add(element, classNames);
            s_trackedBy.Remove(element);
            s_trackedBy.Add(element, ctx);
        }

        // Forgets the class list remembered for element, for an element whose list no longer names a class a theme
        // variable could make and for one cleaned up or returned to a pool.
        internal static void Untrack(VisualElement element)
        {
            if (s_trackedBy.TryGetValue(element, out var ctx))
            {
                s_trackedBy.Remove(element);
                if (s_themed.TryGetValue(ctx, out var themed))
                {
                    themed.Classes.Remove(element);
                }
            }
        }

        // Ends what a mount holds here: its poll, its scope and the class lists it remembered.
        internal static void Release(ReconcilerContext? ctx)
        {
            if (ctx == null)
            {
                return;
            }
            if (s_scopes.TryGetValue(ctx, out var scope))
            {
                scope.PollItem?.Pause();
                s_scopes.Remove(ctx);
            }
            s_themed.Remove(ctx);
            if (ctx.ThemeRoot != null && s_mounts.TryGetValue(ctx.ThemeRoot, out var registered) && ReferenceEquals(registered, ctx))
            {
                s_mounts.Remove(ctx.ThemeRoot);
            }
        }

        private static void Reapply(ReconcilerContext ctx)
        {
            if (!s_themed.TryGetValue(ctx, out var themed))
            {
                return;
            }
            var patcher = new FiberNodePatcher(ctx);
            themed.Elements.RemoveAll(static reference => !reference.TryGetTarget(out _));
            foreach (var reference in themed.Elements.ToArray())
            {
                if (reference.TryGetTarget(out var element) && themed.Classes.TryGetValue(element, out var classes))
                {
                    using (Use(ctx, element))
                    {
                        patcher.ReleaseCustomPropertyTokens(element);
                        patcher.ApplyVariantManipulators(element, classes);
                    }
                }
            }
        }

        // The mount each element a mount was made on belongs to, so a reader holding only an element finds its scope.
        private static readonly ConditionalWeakTable<VisualElement, ReconcilerContext> s_mounts = new();

        internal static void RegisterMount(VisualElement mountPoint, ReconcilerContext ctx)
        {
            s_mounts.Remove(mountPoint);
            s_mounts.Add(mountPoint, ctx);
        }

        // The context of the nearest mount point at or above element, null for an element outside every mount.
        internal static ReconcilerContext? ContextOf(VisualElement? element)
        {
            for (; element != null; element = element.hierarchy.parent)
            {
                if (s_mounts.TryGetValue(element, out var ctx))
                {
                    return ctx;
                }
            }
            return null;
        }

        // The scope the reading in progress asks, null outside a pass of a mount and outside Use.
        internal static Scope? Current => s_current;

        // Makes ctx's scope the one asked until the returned handle is disposed. A reader that runs outside a render
        // pass (a variant turning on, a custom property resolving again) sets it for itself, with the names the last
        // pass read.
        internal static Restore Use(ReconcilerContext? ctx) => Enter(ctx, false);

        // As Use, for a reader that holds an element and may hold no context: the element's own mount decides, and the
        // values a sheet declares are read as the cascade gives them to that element.
        internal static Restore Use(ReconcilerContext? ctx, VisualElement? element)
        {
            var restore = Enter(ctx ?? ContextOf(element), false);
            s_element = element;
            return restore;
        }

        // As Use, reading the sheets again first: a pass of the mount does, so a sheet attached since the last one
        // declares its tokens to it.
        internal static Restore Pass(ReconcilerContext? ctx) => Enter(ctx, true);

        private static Restore Enter(ReconcilerContext? ctx, bool reread)
        {
            var restore = new Restore(s_current, s_element);
            s_element = null;
            if (ctx != null)
            {
                // A pass nested in a pass of the same mount reads what the outer one did.
                var held = s_scopes.TryGetValue(ctx, out var known) ? known : null;
                s_current = held == null || (reread && !ReferenceEquals(held, restore.Scope)) ? Refresh(ctx) : held;
            }
            return restore;
        }

        internal readonly struct Restore : IDisposable
        {
            private readonly Scope? _previous;
            private readonly VisualElement? _element;

            public Restore(Scope? previous, VisualElement? element)
            {
                _previous = previous;
                _element = element;
            }

            public Scope? Scope => _previous;

            public void Dispose()
            {
                s_current = _previous;
                s_element = _element;
            }
        }

        // Rebuilds ctx's names when a sheet at or above its theme root was attached, removed or edited since the last
        // reading, and returns the scope.
        internal static Scope? Refresh(ReconcilerContext? ctx)
        {
            if (ctx?.ThemeRoot == null)
            {
                return null;
            }
            var scope = s_scopes.GetValue(ctx, static _ => new Scope());
            if (!scope.Polling)
            {
                scope.Polling = true;
                var weak = new WeakReference<ReconcilerContext>(ctx);
                scope.PollItem = ctx.ThemeRoot.schedule.Execute(() =>
                {
                    if (weak.TryGetTarget(out var live))
                    {
                        Refresh(live);
                    }
                }).Every(PollMs);
            }
            var signature = SignatureAbove(ctx.ThemeRoot);
            if (signature != scope.Signature)
            {
                var first = scope.Signature == int.MinValue;
                scope.Signature = signature;
                scope.Version++;
                scope.Project.Clear();
                scope.Bundled.Clear();
                scope.Classes.Clear();
                scope.Cleared.Clear();
                scope.Values.Clear();
                scope.Counts.Clear();
                for (var element = ctx.ThemeRoot; element != null; element = element.hierarchy.parent)
                {
                    for (var i = 0; i < element.styleSheets.count; i++)
                    {
                        Observe(scope, element.styleSheets[i], false);
                    }
                }
                if (!first)
                {
                    ScopeChanged?.Invoke(ctx);
                    var weak = new WeakReference<ReconcilerContext>(ctx);
                    ctx.ThemeRoot.schedule.Execute(() =>
                    {
                        if (weak.TryGetTarget(out var live))
                        {
                            Reapply(live);
                        }
                    });
                }
            }
            return scope;
        }

        private static int SignatureAbove(VisualElement root)
        {
            var hash = 17;
            for (var element = root; element != null; element = element.hierarchy.parent)
            {
                for (var i = 0; i < element.styleSheets.count; i++)
                {
                    var sheet = element.styleSheets[i];
                    hash = unchecked(hash * 31 + (sheet == null ? 0 : sheet.GetInstanceID()) * 7 + (sheet == null ? 0 : sheet.contentHash));
                }
            }
            return hash;
        }

        // A sheet that reaches the bundled utilities through its imports contributes those as bundled.
        private static void Observe(Scope scope, StyleSheet? sheet, bool bundled)
        {
            if (sheet == null)
            {
                return;
            }
            var into = bundled || VelvetStyleUtilities.IsUtilities(sheet) ? scope.Bundled : scope.Project;
            var read = ReadOf(sheet);
            if (into == scope.Project)
            {
                foreach (var value in read.Values)
                {
                    scope.Values[value.Key] = value.Value;
                }
                foreach (var count in read.Counts)
                {
                    scope.Counts[count.Key] = (scope.Counts.TryGetValue(count.Key, out var before) ? before : 0) + count.Value;
                }
            }
            foreach (var name in read.Names)
            {
                into.Add(name);
            }
            if (into == scope.Project)
            {
                foreach (var ns in read.Cleared)
                {
                    scope.Cleared.Add(ns);
                }
            }
            foreach (var imported in VelvetStyleUtilities.ImportsOf(sheet))
            {
                Observe(scope, imported, into == scope.Bundled);
            }
        }

        private static void ReadDeclaration(StyleSheet sheet, object property, List<string> names, Read read)
        {
            if (s_name?.GetValue(property) is not string name || !name.StartsWith("--", StringComparison.Ordinal))
            {
                return;
            }
            names.Add(name);
            var wildcard = name.EndsWith("*", StringComparison.Ordinal);
            if (!wildcard && !name.StartsWith("--breakpoint-", StringComparison.Ordinal)
                && !name.StartsWith("--container-", StringComparison.Ordinal)
                && !name.StartsWith("--animate-", StringComparison.Ordinal)
                && name != "--spacing" && !name.StartsWith("--spacing-", StringComparison.Ordinal)
                && !(name.StartsWith("--text-", StringComparison.Ordinal) && name.EndsWith("--line-height", StringComparison.Ordinal)))
            {
                return;
            }
            if (s_values?.GetValue(property) is not Array handles || StyleCustomProperties.Spell(sheet, handles) is not { } text)
            {
                return;
            }
            if (!wildcard)
            {
                read.Values[name] = text;
                read.Counts[name] = read.Counts.TryGetValue(name, out var seen) ? seen + 1 : 1;
            }
            else if (text.Trim() == "initial")
            {
                // `--color-*: initial` clears the namespace, `--*: initial` every one.
                read.Cleared.Add(name == "--*" ? "all-" : name.Substring(2, name.Length - 3));
            }
        }

        private static Read ReadOf(StyleSheet sheet)
        {
            if (s_reads.TryGetValue(sheet, out var read) && read.ContentHash == sheet.contentHash)
            {
                return read;
            }
            s_reads.Remove(sheet);
            read = new Read { ContentHash = sheet.contentHash };
            var names = new List<string>();
            if (s_rules != null && s_properties != null && s_name != null)
            {
                foreach (var rule in (Array?)s_rules.GetValue(sheet) ?? Array.Empty<object>())
                {
                    foreach (var property in (Array?)s_properties.GetValue(rule) ?? Array.Empty<object>())
                    {
                        ReadDeclaration(sheet, property, names, read);
                    }
                }
            }
            read.Names = names.ToArray();
            s_reads.Add(sheet, read);
            return read;
        }
    }
}
