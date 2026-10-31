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

            // The last value a project sheet declares for each --breakpoint-*, --container-* and --animate-* name,
            // as CSS text: what a reader holding no element falls back to.
            public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);

            // A length the variable holds: the one the cascade gives the element being read for (Use), else the last a
            // project sheet declared.
            public bool TryGetPx(string property, out float px)
            {
                px = 0f;
                string? text = null;
                if (s_element != null)
                {
                    StyleCustomProperties.TryStylesheetValue(s_element, property, out text);
                }
                if (text == null && !Values.TryGetValue(property, out text))
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
            public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
        }

        private static readonly ConditionalWeakTable<StyleSheet, Read> s_reads = new();
        private static readonly ConditionalWeakTable<ReconcilerContext, Scope> s_scopes = new();
        private static Scope? s_current;
        private static VisualElement? s_element;

        // Raised after a rebuild finds the sheets a mount reads changed; readers re-evaluate what they took from them.
        internal static event Action<ReconcilerContext>? ScopeChanged;

        private const long PollMs = 100;

        private sealed class Themed
        {
            public readonly List<WeakReference<VisualElement>> Elements = new();
            public readonly ConditionalWeakTable<VisualElement, string[]> Classes = new();
        }

        private static readonly ConditionalWeakTable<ReconcilerContext, Themed> s_themed = new();

        // Remembers an element whose class list holds a class a theme variable could make, so it is configured again
        // when the sheets change what the variables declare.
        internal static void Track(ReconcilerContext ctx, VisualElement element, string[] classNames)
        {
            var themed = s_themed.GetValue(ctx, static _ => new Themed());
            if (!themed.Classes.TryGetValue(element, out _))
            {
                themed.Elements.Add(new WeakReference<VisualElement>(element));
            }
            themed.Classes.Remove(element);
            themed.Classes.Add(element, classNames);
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
                ctx.ThemeRoot.schedule.Execute(() => Refresh(ctx)).Every(PollMs);
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
                    ctx.ThemeRoot.schedule.Execute(() => Reapply(ctx));
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
                && !name.StartsWith("--animate-", StringComparison.Ordinal))
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
