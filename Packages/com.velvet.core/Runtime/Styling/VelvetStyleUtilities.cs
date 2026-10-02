#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Resolves and attaches Velvet's bundled utility stylesheet from runtime code, in the editor and in a
    /// player alike. Every utility the sheet declares resolves to nothing on a panel that does not carry
    /// it, while arbitrary values and the many families Velvet resolves itself rather than declaring are
    /// unaffected — so <see cref="V.Mount(VisualElement, VNode)"/> and a portal into an element the app owns
    /// warn, once per run, when the sheet is missing at their target panel's next update, or at the next
    /// update after the target is added to a panel.
    /// <para>
    /// <c>Documentation~/setup.md</c> owns when to call this, which utilities sit on which side of that
    /// split (with the command that answers it for any one class), and the alternative of referencing the
    /// asset from a scene instead.
    /// </para>
    /// </summary>
    public static class VelvetStyleUtilities
    {
        /// <summary>
        /// The package path of the holder a build preloads, which is what carries the sheet into a player.
        /// </summary>
        internal const string RuntimeAssetsPath =
            "Packages/com.velvet.core/Runtime/Assets/VelvetRuntimeAssets.asset";

        /// <summary>The package path of the sheet itself, which an editor reads when the holder cannot
        /// answer.</summary>
        internal const string StyleSheetAssetPath =
            "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        /// <summary>
        /// The class the sheet keys its dark token set on. Public so an application that sets the class from
        /// its own code — a UXML root, a scene hierarchy — names the same one the sheet declares.
        /// </summary>
        public const string DarkThemeClass = "dark";

        private static StyleSheet? _sheet;

        /// <summary>
        /// The bundled utility stylesheet. Loads on first access and is held for the lifetime of the domain.
        /// </summary>
        /// <exception cref="InvalidOperationException">Neither the preloaded holder nor, in the editor, the
        /// asset itself could be resolved.</exception>
        public static StyleSheet Sheet
        {
            get
            {
                var sheet = TryResolve();
                if (sheet == null)
                {
                    throw new InvalidOperationException(
                        "Velvet's bundled utility stylesheet was not found. In a player it arrives "
                        + $"through '{RuntimeAssetsPath}', which the package's build step adds to "
                        + "PlayerSettings' preloaded assets; a build that cannot find it was made with "
                        + "the holder excluded under Project Settings ▸ Velvet, with that step disabled, "
                        + "with the asset removed from the package, or with the asset's own reference to "
                        + "the stylesheet broken.");
                }

                return sheet;
            }
        }

        private static StyleSheet? TryResolve()
        {
            // The `== null` re-check is Unity's overloaded operator, not a reference test: it also catches a
            // destroyed asset, which a domain-surviving static would otherwise keep handing out.
            if (_sheet == null)
            {
                _sheet = Load();
            }

            return _sheet;
        }

        // The holder first, because it is the only thing a player has; the asset database second, because
        // an editor may not have loaded the holder at all. An editor that has loaded it still falls through
        // when its reference is broken, so no editor run can see that break — which is why
        // BundledStyleSheetInclusionTests pins the holder against the sheet instead of waiting for one.
        private static StyleSheet? Load()
        {
            var preloaded = VelvetRuntimeAssets.Instance;
            if (preloaded != null && preloaded.StyleUtilities != null)
            {
                return preloaded.StyleUtilities;
            }
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetAssetPath);
#else
            return null;
#endif
        }

        /// <summary>
        /// Adds <see cref="Sheet"/> to <paramref name="root"/>'s <see cref="VisualElement.styleSheets"/>.
        /// Attach to the element whose subtree needs the utilities — a panel root covers everything under
        /// it. Attaching twice is harmless.
        /// </summary>
        public static void AttachTo(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            // No already-attached guard: Unity's styleSheets.Add is a complete no-op for a sheet the set
            // already holds — measured, it neither adds a duplicate entry nor moves the existing one to the
            // end of the cascade — so a guard here would only mirror the engine.
            root.styleSheets.Add(Sheet);
            BindThemeTo(root);
        }

        /// <summary>
        /// Keeps <paramref name="root"/> carrying the <see cref="DarkThemeClass"/> class exactly while
        /// <see cref="VelvetTheme.IsDark"/> holds, which is what selects the sheet's dark token set for
        /// <paramref name="root"/>'s subtree. <see cref="AttachTo"/> calls this; a project that reaches the
        /// sheet from a scene reference instead calls it itself. Binding one element twice is harmless.
        /// </summary>
        public static void BindThemeTo(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            s_themeRoots.RemoveAll(bound => IsCollectedOr(bound, root));
            s_themeRoots.Add(new WeakReference<VisualElement>(root));
            ApplyTheme(root);
        }

        // Held weakly because the theme event is static, so a root it reached strongly would live as long as the
        // domain. Unsubscribing on detach from a panel instead left the root of a disposed panel held, which is
        // the case ThemeBindingTests arranges.
        private static readonly List<WeakReference<VisualElement>> s_themeRoots = new();

        static VelvetStyleUtilities() => VelvetTheme.DarkModeChanged += ApplyThemeToBoundRoots;

        private static void ApplyThemeToBoundRoots()
        {
            foreach (var bound in s_themeRoots)
            {
                if (bound.TryGetTarget(out var root)) ApplyTheme(root);
            }
        }

        private static bool IsCollectedOr(WeakReference<VisualElement> bound, VisualElement root)
        {
            bound.TryGetTarget(out var live);
            return live == null || live == root;
        }

        private static void ApplyTheme(VisualElement root) => root.EnableInClassList(DarkThemeClass, VelvetTheme.IsDark);

        private static bool s_missingReported;

        /// <summary>Set from the editor assembly, where the project's build settings live: whether the project
        /// left the sheet's holder out of its builds, which silences the report.</summary>
        internal static Func<bool>? MissingReportSilenced;
        private static readonly FieldInfo? s_importsField = EngineMember.StyleSheetImports.ResolveField();
        private static readonly FieldInfo? s_importedSheetField = EngineMember.ImportedStyleSheet.ResolveField();

        // Styles resolve on a panel, so that is where the search runs: at the panel's next scheduler tick rather
        // than at the call, so a sheet attached after the call is seen, and again after each arrival, since the
        // panel a target moves to need not carry the sheet. Each caller gets its own watch, so ending one leaves
        // any other on the same target running.
        internal static IDisposable WatchForMissingSheet(VisualElement target) => new MissingSheetWatch(target);

        /// <summary>Brings a panel host Velvet creates for a portal in line with the document the portal's tree sits
        /// in: the host root carries the stylesheets on <paramref name="declaringRoot"/> and its ancestors, less the
        /// sheets the host's panel already holds, and the classes of that panel's root and of the root's child the
        /// tree sits under. Asked now, and again at each tick of <paramref name="declaringRoot"/>'s panel while the
        /// host is on one, so a sheet or a class the app adds or removes there later is followed.</summary>
        /// <remarks>A DOM portal's children sit under <c>body</c>: every stylesheet of the document reaches them, and
        /// so do the classes of <c>html</c> and <c>body</c>, but not those of the component that rendered them. The
        /// panel root and the element under it stand for those two, so every portal of a tree sharing a layer host
        /// asks for the same thing, and none decides it by being synced last.</remarks>
        internal static void CarryToHost(VisualElement declaringRoot, VisualElement hostRoot)
        {
            // Every call for one host has to name the same root: the first fixes it.
            if (!s_hostCarries.TryGetValue(hostRoot, out var carry))
            {
                var follow = s_rootFollows.GetValue(declaringRoot, root => new RootFollow(root));
                carry = new HostCarry(declaringRoot, hostRoot, follow);
                PinBox(hostRoot);
                s_hostCarries.Add(hostRoot, carry);
                follow.Add(carry);
            }

            Sync(carry);
        }

        // Also what lets a check on a target inside a host bring the host up to date first rather than depend on
        // which of the two panels ticks first.
        private static readonly ConditionalWeakTable<VisualElement, HostCarry> s_hostCarries = new();

        // One follow per root, so the root's chain is read once a tick however many hosts its tree has.
        private static readonly ConditionalWeakTable<VisualElement, RootFollow> s_rootFollows = new();

        private static void Sync(HostCarry carry)
        {
            var declaringRoot = carry.DeclaringRoot;
            var hostRoot = carry.HostRoot;
            carry.Stamp = ChainStamp(declaringRoot);
            var (top, body) = DocumentRoots(declaringRoot);
            if (IsBoundAtOrAbove(declaringRoot)) BindThemeTo(hostRoot);
            else hostRoot.EnableInClassList(DarkThemeClass, IsDark(top) || IsDark(body));

            var classes = new HashSet<string>();
            AddDocumentClasses(top, classes);
            if (body != null) AddDocumentClasses(body, classes);
            foreach (var name in carry.CarriedClasses)
            {
                if (!classes.Contains(name)) hostRoot.RemoveFromClassList(name);
            }

            foreach (var name in classes) hostRoot.AddToClassList(name);
            carry.CarriedClasses = classes;

            var sheets = SheetsToCarry(carry);
            if (sheets.SequenceEqual(carry.Carried)) return;

            // Taken off and put back whole, because adding a sheet the set already holds leaves it where it was,
            // and the order is the cascade's.
            foreach (var sheet in carry.Carried) hostRoot.styleSheets.Remove(sheet);
            foreach (var sheet in sheets) hostRoot.styleSheets.Add(sheet);
            carry.Carried = sheets;
        }

        // The html and the body of the document the tree sits in: the panel's root, and the chain element directly
        // under it, which is where a panel puts a UIDocument's root.
        private static (VisualElement Top, VisualElement? Body) DocumentRoots(VisualElement declaringRoot)
        {
            VisualElement? body = null;
            var top = declaringRoot;
            while (top.hierarchy.parent is { } parent)
            {
                body = top;
                top = parent;
            }

            return (top, body);
        }

        private static bool IsDark(VisualElement? element) => element?.ClassListContains(DarkThemeClass) == true;

        // The dark class is decided with the theme binding above; a unity- class names the element that carries it.
        private static void AddDocumentClasses(VisualElement element, HashSet<string> into)
        {
            foreach (var name in element.GetClasses())
            {
                if (name != DarkThemeClass && !name.StartsWith("unity-", StringComparison.Ordinal)) into.Add(name);
            }
        }

        // The host root is a panel of its own, drawn over the app or placed in the scene, where html and body sit
        // beneath a page. So a carried :root rule or a document class may hand custom and inherited properties to
        // the portal's children but may not paint, move or size the root: an inline value outranks every sheet.
        private static void PinBox(VisualElement hostRoot)
        {
            var style = hostRoot.style;
            style.backgroundColor = Color.clear;
            style.backgroundImage = StyleKeyword.None;
            style.borderTopWidth = style.borderRightWidth = style.borderBottomWidth = style.borderLeftWidth = 0f;
            style.borderTopLeftRadius = style.borderTopRightRadius = 0f;
            style.borderBottomRightRadius = style.borderBottomLeftRadius = 0f;
            style.paddingTop = style.paddingRight = style.paddingBottom = style.paddingLeft = 0f;
            style.marginTop = style.marginRight = style.marginBottom = style.marginLeft = 0f;
            style.flexDirection = FlexDirection.Column;
            style.flexWrap = Wrap.NoWrap;
            style.alignItems = Align.Stretch;
            style.alignContent = Align.FlexStart;
            style.justifyContent = Justify.FlexStart;
            style.opacity = 1f;
            style.display = DisplayStyle.Flex;
            style.overflow = Overflow.Visible;
            style.translate = StyleKeyword.None;
            style.rotate = StyleKeyword.None;
            style.scale = StyleKeyword.None;

            // Pinned to the values that fill the panel. A world-space document writes its own size inline, and that
            // stays.
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0f;
            if (style.width.keyword == StyleKeyword.Null) style.width = StyleKeyword.Auto;
            if (style.height.keyword == StyleKeyword.Null) style.height = StyleKeyword.Auto;
            style.minWidth = style.minHeight = StyleKeyword.Auto;
            style.maxWidth = style.maxHeight = StyleKeyword.None;
        }

        // Outermost first, which is the order the declaring panel's cascade meets them in. A sheet the host's panel
        // already holds above the host root, such as the theme PanelHostFactory copies into its settings, is left
        // there.
        private static List<StyleSheet> SheetsToCarry(HostCarry carry)
        {
            var declaringRoot = carry.DeclaringRoot;
            var utilities = TryResolve();
            var chain = new List<VisualElement>();
            for (var element = declaringRoot; element != null; element = element.hierarchy.parent) chain.Add(element);

            var sheets = new List<StyleSheet>();
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                for (var j = 0; j < chain[i].styleSheets.count; j++)
                {
                    var sheet = chain[i].styleSheets[j];
                    if (ReachesAbove(carry.HostRoot, sheet)) continue;
                    sheets.Remove(sheet);
                    sheets.Add(sheet);
                }
            }

            // MUTANT_SURVIVES(unreachable): an editor reads the sheet from the asset database whenever the holder
            // cannot answer, so no editor run resolves it to null.
            if (utilities == null) return sheets;

            var at = sheets.IndexOf(utilities);
            if (at >= 0) sheets.RemoveAt(at);
            // Left on a host that reaches the utilities some other way, such as a copied theme importing them, the
            // sheet would outrank that theme's own overrides of them.
            if (ReachesSheet(declaringRoot, utilities) && !HostReachesBesides(carry, sheets, utilities))
            {
                sheets.Insert(Math.Max(at, 0), utilities);
            }

            return sheets;
        }

        private static bool ReachesAbove(VisualElement hostRoot, StyleSheet sheet)
        {
            for (var element = hostRoot.hierarchy.parent; element != null; element = element.hierarchy.parent)
            {
                if (element.styleSheets.Contains(sheet)) return true;
            }

            return false;
        }

        private static bool HostReachesBesides(HostCarry carry, List<StyleSheet> sheets, StyleSheet utilities)
        {
            var hostRoot = carry.HostRoot;
            for (var i = 0; i < hostRoot.styleSheets.count; i++)
            {
                var own = hostRoot.styleSheets[i];
                if (!carry.Carried.Contains(own) && Reaches(own, utilities)) return true;
            }

            return sheets.Any(sheet => Reaches(sheet, utilities)) || ReachesSheet(hostRoot.hierarchy.parent, utilities);
        }

        // Which sheets the chain carries, what each and everything it imports holds, the document roots' classes and
        // whether the chain is bound to the theme, in one number the follow can compare every tick without allocating.
        private static int ChainStamp(VisualElement declaringRoot)
        {
            var stamp = 0;
            for (var element = declaringRoot; element != null; element = element.hierarchy.parent)
            {
                for (var i = 0; i < element.styleSheets.count; i++) stamp = SheetStamp(stamp, element.styleSheets[i]);
            }

            var (top, body) = DocumentRoots(declaringRoot);
            stamp = ClassStamp(ClassStamp(stamp, top), body);
            return HashCode.Combine(stamp, IsBoundAtOrAbove(declaringRoot));
        }

        private static int ClassStamp(int stamp, VisualElement? element)
        {
            if (element?.GetClasses() is not List<string> names) return stamp;
            foreach (var name in names) stamp = HashCode.Combine(stamp, name);
            return stamp;
        }

        private static int SheetStamp(int stamp, StyleSheet sheet)
        {
            stamp = HashCode.Combine(stamp, sheet, sheet.contentHash);
            foreach (var imported in Facts(sheet).Imports) stamp = SheetStamp(stamp, imported);
            return stamp;
        }

        private static void SyncHostAbove(VisualElement target)
        {
            for (var element = target; element != null; element = element.hierarchy.parent)
            {
                if (s_hostCarries.TryGetValue(element, out var carry))
                {
                    Sync(carry);
                    return;
                }
            }
        }

        private static bool IsBound(VisualElement element)
        {
            foreach (var bound in s_themeRoots)
            {
                bound.TryGetTarget(out var root);
                if (root == element) return true;
            }

            return false;
        }

        private static bool IsBoundAtOrAbove(VisualElement from)
        {
            for (var element = from; element != null; element = element.hierarchy.parent)
            {
                if (IsBound(element)) return true;
            }

            return false;
        }

        private static void ReportIfMissingAbove(VisualElement target)
        {
            if (s_missingReported) return;

            SyncHostAbove(target);
            var sheet = TryResolve();
            // A player that cannot resolve the sheet through its holder has nothing to compare against, and a
            // project that excluded the holder may still reach the sheet from a scene reference.
            if (sheet == null || ReachesSheet(target, sheet)) return;

            if (MissingReportSilenced?.Invoke() == true) return;

            s_missingReported = true;
            FiberLogger.LogWarning("VelvetStyleUtilities",
                $"The V.Mount or V.Portal target '{target.name}' is on a panel that does not carry Velvet's "
                + "utility stylesheet, so every utility class the sheet declares resolves to nothing there while "
                + "arbitrary values and the families Velvet realises in C# keep working. Call "
                + "VelvetStyleUtilities.AttachTo on the panel root — Documentation~/setup.md. "
                + "Reported once per run.");
        }

        private static bool ReachesSheet(VisualElement? from, StyleSheet sheet)
        {
            for (var element = from; element != null; element = element.hierarchy.parent)
            {
                for (var i = 0; i < element.styleSheets.count; i++)
                {
                    if (Reaches(element.styleSheets[i], sheet)) return true;
                }
            }

            return false;
        }

        // A theme or project sheet that @imports the utilities carries them too.
        private static bool Reaches(StyleSheet from, StyleSheet sheet)
        {
            if (Signature(from) == Signature(sheet)) return true;

            foreach (var imported in Imports(from))
            {
                if (Reaches(imported, sheet)) return true;
            }

            return false;
        }

        // The name and the imports' names rather than the reference, because an asset bundle carries its own copy
        // of the sheet, and of each partial, beside the ones the holder resolves.
        private static string Signature(StyleSheet sheet) => Facts(sheet).Signature;

        private static List<StyleSheet> Imports(StyleSheet sheet) => Facts(sheet).Imports;

        private static SheetFacts Facts(StyleSheet sheet)
        {
            var facts = s_facts.GetValue(sheet, unread => new SheetFacts(unread));
            // Read again once the content hash moves, which the importer sets from the file's text.
            if (facts.ContentHash != sheet.contentHash) facts.Read(sheet);
            return facts;
        }

        // Cached because a portal patch and the follow ask again, and every ask walks the same sheets.
        private static readonly ConditionalWeakTable<StyleSheet, SheetFacts> s_facts = new();

        private sealed class SheetFacts
        {
            internal int ContentHash;
            internal string Signature = "";
            internal List<StyleSheet> Imports = new();

            internal SheetFacts(StyleSheet sheet) => Read(sheet);

            internal void Read(StyleSheet sheet)
            {
                ContentHash = sheet.contentHash;
                Imports = ReadImports(sheet).ToList();
                Signature = sheet.name + ":" + string.Join(",", Imports.Select(imported => imported.name));
            }
        }

        private static IEnumerable<StyleSheet> ReadImports(StyleSheet sheet)
        {
            foreach (var import in s_importsField?.GetValue(sheet) as Array ?? Array.Empty<object>())
            {
                var imported = s_importedSheetField?.GetValue(import) as StyleSheet;
                if (imported != null) yield return imported;
            }
        }

#if UNITY_EDITOR
        // Re-armed for the reason VelvetShaders re-arms its missing-shader warning.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RearmMissingReport() => s_missingReported = false;
#endif

        private sealed class HostCarry
        {
            internal readonly VisualElement DeclaringRoot;
            internal readonly VisualElement HostRoot;
            internal readonly RootFollow Follow;
            internal List<StyleSheet> Carried = new();
            internal HashSet<string> CarriedClasses = new();
            internal int Stamp;

            internal HostCarry(VisualElement declaringRoot, VisualElement hostRoot, RootFollow follow)
            {
                DeclaringRoot = declaringRoot;
                HostRoot = hostRoot;
                Follow = follow;
            }
        }

        private sealed class RootFollow
        {
            private readonly VisualElement _root;
            private readonly List<HostCarry> _hosts = new();

            internal RootFollow(VisualElement root)
            {
                _root = root;
                root.schedule.Execute(Tick).Every(0);
            }

            internal void Add(HostCarry carry) => _hosts.Add(carry);

            private void Tick()
            {
                // Dropped here rather than kept, since the root outlives a world-space host and holds this follow.
                _hosts.RemoveAll(carry => carry.HostRoot.panel == null);
                // MUTANT_SURVIVES(equivalent): with no host the loop below does nothing, so reading the stamp anyway
                // costs time and changes no result.
                if (_hosts.Count == 0) return;

                var stamp = ChainStamp(_root);
                foreach (var carry in _hosts)
                {
                    if (carry.Stamp != stamp) Sync(carry);
                }
            }
        }

        private sealed class MissingSheetWatch : IDisposable
        {
            private readonly VisualElement _target;
            private readonly IVisualElementScheduledItem _check;

            internal MissingSheetWatch(VisualElement target)
            {
                _target = target;
                _check = target.schedule.Execute(() => ReportIfMissingAbove(_target));
                target.RegisterCallback<AttachToPanelEvent>(Rearm);
            }

            private void Rearm(AttachToPanelEvent attached) => _check.ExecuteLater(0);

            public void Dispose()
            {
                _target.UnregisterCallback<AttachToPanelEvent>(Rearm);
                _check.Pause();
            }
        }
    }
}
