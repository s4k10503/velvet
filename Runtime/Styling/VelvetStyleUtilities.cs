#nullable enable
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Resolves and attaches Velvet's bundled utility stylesheet from runtime code, in the editor and in a
    /// player alike. Every utility the sheet declares resolves to nothing on a panel that does not carry
    /// it, while arbitrary values and the many families Velvet resolves itself rather than declaring are
    /// unaffected — which is why a missing sheet reads as a partial styling bug.
    /// <para>
    /// <c>Documentation~/setup.md</c> owns when to call this, which utilities sit on which side of that
    /// split (with the command that answers it for any one class), and the alternative of referencing the
    /// asset from a scene instead.
    /// </para>
    /// </summary>
    public static class VelvetStyleUtilities
    {
        /// <summary>
        /// The <see cref="Resources"/> path of the sheet, without extension. Public so a project that builds
        /// its own asset pipeline around <c>Resources.Load</c> can address the same asset.
        /// </summary>
        public const string ResourcePath = "Velvet/StyleUtilities";

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
        /// <exception cref="InvalidOperationException">The package's <c>Resources</c> asset is absent — the
        /// build stripped it, or the package was vendored without <c>Runtime/Resources/</c>.</exception>
        public static StyleSheet Sheet
        {
            get
            {
                // The `== null` re-check is Unity's overloaded operator, not a reference test: it also catches a
                // destroyed asset, which a domain-surviving static would otherwise keep handing out.
                if (_sheet == null)
                {
                    _sheet = Resources.Load<StyleSheet>(ResourcePath);
                    if (_sheet == null)
                    {
                        throw new InvalidOperationException(
                            $"Velvet's bundled utility stylesheet was not found at Resources path " +
                            $"'{ResourcePath}'. It ships in com.velvet.core under Runtime/Resources/; a build " +
                            $"that cannot find it has had the package's Resources folder removed.");
                    }
                }

                return _sheet;
            }
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
