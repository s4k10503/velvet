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
    /// unaffected — so <see cref="V.Mount(VisualElement, VNode)"/> warns, once per run, when its target
    /// reaches the panel without the sheet.
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

        private static readonly ConditionalWeakTable<VisualElement, ThemeBinding> _themeBindings = new();

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
        /// Attach before mounting a tree, and to the element whose subtree needs the utilities — a panel
        /// root covers everything under it. Attaching twice is harmless.
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

            if (_themeBindings.TryGetValue(root, out _)) return;
            _themeBindings.Add(root, new ThemeBinding(root));
        }

        private static bool s_missingReported;

        /// <summary>Set from the editor assembly, where the project's build settings live: whether the project
        /// left the sheet's holder out of its builds, which silences the report.</summary>
        internal static Func<bool>? MissingReportSilenced;
        private static FieldInfo? s_importsField;
        private static FieldInfo? s_importedSheetField;
        private static string? s_sheetSignature;

        // Styles resolve on a panel, so that is where the search runs: at once for a target already on one, and
        // on each later arrival, since the panel a target moves to need not carry the sheet.
        internal static void ReportIfMissing(VisualElement target)
        {
            target.RegisterCallback<AttachToPanelEvent>(OnMountTargetAttached);
            if (target.panel != null)
            {
                ReportIfMissingAbove(target);
            }
        }

        internal static void StopReporting(VisualElement target)
            => target.UnregisterCallback<AttachToPanelEvent>(OnMountTargetAttached);

        private static void OnMountTargetAttached(AttachToPanelEvent attached)
            => ReportIfMissingAbove((VisualElement)attached.currentTarget);

        private static void ReportIfMissingAbove(VisualElement target)
        {
            if (s_missingReported) return;

            var sheet = TryResolve();
            // A player that cannot resolve the sheet through its holder has nothing to compare against, and a
            // project that excluded the holder may still reach the sheet from a scene reference.
            if (sheet == null)
            {
                return;
            }

            for (var element = target; element != null; element = element.hierarchy.parent)
            {
                for (var i = 0; i < element.styleSheets.count; i++)
                {
                    if (Reaches(element.styleSheets[i], sheet)) return;
                }
            }

            if (MissingReportSilenced?.Invoke() == true) return;

            s_missingReported = true;
            FiberLogger.LogWarning("VelvetStyleUtilities",
                $"V.Mount's target '{target.name}' is on a panel that does not carry Velvet's utility "
                + "stylesheet, so every utility class the sheet declares resolves to nothing there while "
                + "arbitrary values and the families Velvet realises in C# keep working. Call "
                + "VelvetStyleUtilities.AttachTo on the panel root before V.Mount — Documentation~/setup.md. "
                + "Reported once per run.");
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
        private static string Signature(StyleSheet sheet)
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

        private sealed class ThemeBinding
        {
            private readonly VisualElement _root;
            private bool _subscribed;

            internal ThemeBinding(VisualElement root)
            {
                _root = root;
                root.RegisterCallback<AttachToPanelEvent>(_ => Subscribe());
                root.RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());
                if (root.panel != null)
                {
                    Subscribe();
                }
            }

            // The theme event is static, so the subscription is held only while the element is on a panel: a
            // permanent one would keep every root a closed window or a finished test ever attached the sheet
            // to alive for the lifetime of the domain.
            private void Subscribe()
            {
                if (_subscribed) return;
                VelvetTheme.DarkModeChanged += Apply;
                _subscribed = true;
                Apply();
            }

            private void Unsubscribe()
            {
                if (!_subscribed) return;
                VelvetTheme.DarkModeChanged -= Apply;
                _subscribed = false;
            }

            private void Apply() => _root.EnableInClassList(DarkThemeClass, VelvetTheme.IsDark);
        }
    }
}
