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
        private static FieldInfo? s_importsField;
        private static FieldInfo? s_importedSheetField;
        private static string? s_sheetSignature;

        // Styles resolve on a panel, so that is where the search runs: at the panel's next scheduler tick rather
        // than at the call, so a sheet attached after the call is seen, and again after each arrival, since the
        // panel a target moves to need not carry the sheet. Each caller gets its own watch, so ending one leaves
        // any other on the same target running.
        internal static IDisposable WatchForMissingSheet(VisualElement target) => new MissingSheetWatch(target);

        /// <summary>Brings a panel host Velvet creates for a portal in line with the position the portal was
        /// declared at: the host root gets the sheet where <paramref name="declaredAt"/> reaches it, and the
        /// <see cref="DarkThemeClass"/> class <paramref name="declaredAt"/> resolves.</summary>
        internal static void CarryToHost(VisualElement declaredAt, VisualElement hostRoot)
        {
            s_hostDeclarations.Remove(hostRoot);
            s_hostDeclarations.Add(hostRoot, declaredAt);
            SyncHost(declaredAt, hostRoot);
            // Asked again at the declaring panel's next tick, for the reason WatchForMissingSheet waits for one.
            declaredAt.schedule.Execute(() => SyncHost(declaredAt, hostRoot));
        }

        // The position each host was last carried from, so a check on a target inside a host can bring the host
        // up to date first rather than depend on which of the two panels ticks first.
        private static readonly ConditionalWeakTable<VisualElement, VisualElement> s_hostDeclarations = new();

        internal static void SyncHost(VisualElement declaredAt, VisualElement hostRoot)
        {
            if (IsBoundAtOrAbove(declaredAt)) BindThemeTo(hostRoot);
            else hostRoot.EnableInClassList(DarkThemeClass, IsDarkAtOrAbove(declaredAt));

            var sheet = TryResolve();
            if (sheet == null)
            {
                return;
            }

            // Left on a host that reaches the utilities some other way, such as a copied theme importing them, the
            // sheet would outrank that theme's own overrides of them.
            if (ReachesSheetBesides(hostRoot, sheet)) hostRoot.styleSheets.Remove(sheet);
            else if (ReachesSheet(declaredAt, sheet)) hostRoot.styleSheets.Add(sheet);
        }

        private static void SyncHostAbove(VisualElement target)
        {
            for (var element = target; element != null; element = element.hierarchy.parent)
            {
                if (s_hostDeclarations.TryGetValue(element, out var declaredAt))
                {
                    SyncHost(declaredAt, element);
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

        private static bool IsDarkAtOrAbove(VisualElement from)
        {
            for (var element = from; element != null; element = element.hierarchy.parent)
            {
                if (element.ClassListContains(DarkThemeClass)) return true;
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

        private static bool ReachesSheetBesides(VisualElement hostRoot, StyleSheet sheet)
        {
            for (var i = 0; i < hostRoot.styleSheets.count; i++)
            {
                var own = hostRoot.styleSheets[i];
                if (own != sheet && Reaches(own, sheet)) return true;
            }

            return ReachesSheet(hostRoot.hierarchy.parent, sheet);
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
            s_sheetSignature ??= Signature(sheet);
            if (Signature(from) == s_sheetSignature) return true;

            foreach (var imported in Imports(from))
            {
                if (Reaches(imported, sheet)) return true;
            }

            return false;
        }

        // The name and the imports' names rather than the reference, because an asset bundle carries its own copy
        // of the sheet, and of each partial, beside the ones the holder resolves.
        private static string Signature(StyleSheet sheet) => s_signatures.GetValue(sheet, ComputeSignature);

        // Cached because a portal patch asks again, and every ask walks the same sheets.
        private static readonly ConditionalWeakTable<StyleSheet, string> s_signatures = new();

        private static string ComputeSignature(StyleSheet sheet)
            => sheet.name + ":" + string.Join(",", Imports(sheet).Select(imported => imported.name));

        private static IEnumerable<StyleSheet> Imports(StyleSheet sheet)
        {
            s_importsField ??= typeof(StyleSheet).GetField("imports", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var import in s_importsField?.GetValue(sheet) as Array ?? Array.Empty<object>())
            {
                s_importedSheetField ??= import.GetType().GetField("styleSheet");
                var imported = s_importedSheetField?.GetValue(import) as StyleSheet;
                if (imported != null) yield return imported;
            }
        }

#if UNITY_EDITOR
        // Re-armed for the reason VelvetShaders re-arms its missing-shader warning.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RearmMissingReport() => s_missingReported = false;
#endif

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
