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
        private const string InitialMarker = "--velvet-initial-";

        private static readonly PropertyInfo? s_rules = EngineMember.StyleSheetRules.ResolveProperty();
        private static readonly PropertyInfo? s_properties = EngineMember.StyleRuleProperties.ResolveProperty();
        private static readonly PropertyInfo? s_name = EngineMember.StylePropertyName.ResolveProperty();
        private static readonly PropertyInfo? s_values = EngineMember.StylePropertyValues.ResolveProperty();

        internal sealed class Scope
        {
            public readonly HashSet<string> Project = new(StringComparer.Ordinal);
            public readonly HashSet<string> Bundled = new(StringComparer.Ordinal);
            // The classes StyleThemeClass has read against these names, dropped with them.
            // Namespaces a sheet set to initial through a `--velvet-initial-<namespace>` marker, `all` for every one.
            public readonly HashSet<string> Cleared = new(StringComparer.Ordinal);
            public readonly Dictionary<string, string?> Classes = new(StringComparer.Ordinal);
            public int Signature = int.MinValue;
            // Moves whenever a rebuild finds the sheets changed, which readers of a declared value compare to know
            // theirs is stale.
            public int Version;
            public long CheckedAt = long.MinValue;

            // The last value a project sheet declares for each --breakpoint-*, --container-* and --animate-* name, as CSS text.
            public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);

            // A length a project sheet declared for the variable, in px, where it reads as one.
            public bool TryGetPx(string property, out float px)
            {
                px = 0f;
                return Values.TryGetValue(property, out var text)
                    && StyleArbitraryValueResolver.TryParseArbitraryPixels(("[" + text + "]").AsSpan(), out px);
            }

            // Whether name in the namespace (such as "color-") is a bundled token its namespace's initial removes: not
            // one the project declares itself.
            public bool ClearsBundled(string ns, string name)
                => (Cleared.Contains(ns) || Cleared.Contains("all-")) && !Project.Contains("--" + ns + name)
                    && (Bundled.Contains("--" + ns + name) || (ns == "color-" && name is "white" or "black"));
        }

        private sealed class Read
        {
            public int ContentHash;
            public string[] Names = Array.Empty<string>();
            public readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);
        }

        private static readonly ConditionalWeakTable<StyleSheet, Read> s_reads = new();
        private static readonly ConditionalWeakTable<ReconcilerContext, Scope> s_scopes = new();
        private static Scope? s_current;

        // The mount each element a mount was made on belongs to, so a reader holding only an element finds its scope.
        private static readonly ConditionalWeakTable<VisualElement, ReconcilerContext> s_mounts = new();

        // A scope is reread at most this often outside a pass (Changed); a pass rereads every time.
        private const long RereadIntervalMs = 100;

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

        // Whether the sheets ctx's scope reads changed since seen, which Changed moves to the scope's current version.
        internal static bool Changed(ReconcilerContext? ctx, ref int seen)
        {
            if (ctx?.ThemeRoot == null)
            {
                return false;
            }
            var scope = s_scopes.GetValue(ctx, static _ => new Scope());
            var now = System.Diagnostics.Stopwatch.GetTimestamp() * 1000L / System.Diagnostics.Stopwatch.Frequency;
            if (now - scope.CheckedAt >= RereadIntervalMs || scope.Signature == int.MinValue)
            {
                Refresh(ctx);
                scope.CheckedAt = now;
            }
            if (scope.Version == seen)
            {
                return false;
            }
            seen = scope.Version;
            return true;
        }

        // The scope the reading in progress asks, null outside a pass of a mount and outside Use.
        internal static Scope? Current => s_current;

        // Makes ctx's scope the one asked until the returned handle is disposed. A reader that runs outside a render
        // pass (a variant turning on, a custom property resolving again) sets it for itself, with the names the last
        // pass read.
        internal static Restore Use(ReconcilerContext? ctx) => Enter(ctx, false);

        // As Use, for a reader that holds an element and may hold no context: the element's own mount decides.
        internal static Restore Use(ReconcilerContext? ctx, VisualElement? element) => Enter(ctx ?? ContextOf(element), false);

        // As Use, reading the sheets again first: a pass of the mount does, so a sheet attached since the last one
        // declares its tokens to it.
        internal static Restore Pass(ReconcilerContext? ctx) => Enter(ctx, true);

        private static Restore Enter(ReconcilerContext? ctx, bool reread)
        {
            var previous = s_current;
            if (ctx != null)
            {
                s_current = reread || !s_scopes.TryGetValue(ctx, out var held) ? Refresh(ctx) : held;
            }
            return new Restore(previous);
        }

        internal readonly struct Restore : IDisposable
        {
            private readonly Scope? _previous;

            public Restore(Scope? previous) => _previous = previous;

            public void Dispose() => s_current = _previous;
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
            var signature = SignatureAbove(ctx.ThemeRoot);
            if (signature != scope.Signature)
            {
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
                if (name.StartsWith(InitialMarker, StringComparison.Ordinal))
                {
                    scope.Cleared.Add(name.Substring(InitialMarker.Length) + "-");
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
            if ((name.StartsWith("--breakpoint-", StringComparison.Ordinal)
                    || name.StartsWith("--container-", StringComparison.Ordinal)
                    || name.StartsWith("--animate-", StringComparison.Ordinal))
                && s_values?.GetValue(property) is Array handles && StyleCustomProperties.Spell(sheet, handles) is { } text)
            {
                read.Values[name] = text;
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
