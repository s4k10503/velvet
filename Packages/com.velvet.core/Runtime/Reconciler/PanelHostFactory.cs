#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A framework-owned host panel backing a layer portal or a world-space node: the hidden
    // GameObject carrying the UIDocument, the runtime-created PanelSettings, and the empty theme
    // created when no declaring theme was resolvable (tracked so disposal destroys exactly what was
    // created). Held in ReconcilerContext.LayerHosts / WorldSpaceBindings; destroyed by
    // FiberElementCleaner (world-space, with its placeholder) and the reconciler dispose sweep.
    internal sealed class PanelHostRecord
    {
        public readonly GameObject Host;
        public readonly UIDocument Document;
        public readonly PanelSettings Settings;
        public readonly ThemeStyleSheet? CreatedTheme;

        public PanelHostRecord(GameObject host, UIDocument document, PanelSettings settings, ThemeStyleSheet? createdTheme)
        {
            Host = host;
            Document = document;
            Settings = settings;
            CreatedTheme = createdTheme;
        }
    }

    // Creates and destroys the framework-owned host panels behind V.Portal(layer:) and
    // V.WorldSpace. A host is an ordinary UIDocument-driven runtime panel whose base settings
    // (theme, scaling, target display) copy the panel the portal was DECLARED on, so layer and
    // world-space UI resolves the same styles as the tree it logically belongs to.
    internal static class PanelHostFactory
    {
        // Layer sorting relative to the declaring panel's own sorting order. The gaps leave room
        // for user panels to slot between the framework layers without colliding with them.
        private const float BackgroundSortingOffset = -100f;
        private const float OverlaySortingOffset = 100f;
        private const float TopmostSortingOffset = 200f;

        // One screen-space host panel for a UILayer, sorted around the declaring panel.
        public static PanelHostRecord CreateLayerHost(UILayer layer, IPanel? declaringPanel)
        {
            var declaring = ResolveDeclaringSettings(declaringPanel);
            var (host, document, settings, createdTheme) = CreateHostParts($"VelvetLayer-{layer}", declaring);
            var baseOrder = declaring != null ? declaring.sortingOrder : 0f;
            settings.sortingOrder = baseOrder + layer switch
            {
                UILayer.Background => BackgroundSortingOffset,
                UILayer.Topmost => TopmostSortingOffset,
                _ => OverlaySortingOffset,
            };
            AttachDocument(document, settings);
            return new PanelHostRecord(host, document, settings, createdTheme);
        }

        // One world-space host panel for a V.WorldSpace instance: render mode WorldSpace, a fixed
        // virtual panel resolution, and the node's transform on the host GameObject (a root
        // world-space document follows its GameObject transform).
        public static PanelHostRecord CreateWorldSpaceHost(WorldSpaceNode node, IPanel? declaringPanel)
        {
            var declaring = ResolveDeclaringSettings(declaringPanel);
            var (host, document, settings, createdTheme) = CreateHostParts("VelvetWorldSpace", declaring);
            settings.renderMode = PanelRenderMode.WorldSpace;
            host.transform.SetPositionAndRotation(node.Position, node.Rotation);
            AttachDocument(document, settings);
            // The document derives its root sizing from (settings, size mode, size) but only
            // re-derives on a VALUE change, and the attach itself never re-runs it — so both size
            // settings are driven after the attach, with the mode round-tripped so the fixed
            // sizing is derived exactly once against the live world-space settings even when the
            // requested size equals the document's own defaults.
            document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Dynamic;
            document.worldSpaceSize = node.PanelSize;
            document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            return new PanelHostRecord(host, document, settings, createdTheme);
        }

        // The shared skeleton: the hidden GameObject + document + a runtime PanelSettings carrying
        // the declaring panel's base configuration. The document is NOT attached yet — callers
        // finish configuring the settings first, then AttachDocument assigns them (the assignment
        // is what creates the backing panel, so it must see the final configuration).
        private static (GameObject host, UIDocument document, PanelSettings settings, ThemeStyleSheet? createdTheme)
            CreateHostParts(string name, PanelSettings? declaring)
        {
            var host = new GameObject(name);
            // Hidden from the hierarchy and excluded from editor scene saves, but deliberately NOT
            // the full HideAndDontSave: the DontSaveInBuild flag pulls a GameObject out of its scene
            // entirely (scene.IsValid() turns false), and the host must stay an ordinary scene
            // object — it is framework-owned, destroyed with its reconciler, and a
            // runtime-instantiated object never reaches a build's serialized data anyway.
            host.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
            var document = host.AddComponent<UIDocument>();
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            if (declaring != null)
            {
                settings.themeStyleSheet = declaring.themeStyleSheet;
                settings.scaleMode = declaring.scaleMode;
                settings.scale = declaring.scale;
                settings.referenceResolution = declaring.referenceResolution;
                settings.targetDisplay = declaring.targetDisplay;
            }
            ThemeStyleSheet? createdTheme = null;
            if (settings.themeStyleSheet == null)
            {
                // No resolvable theme (a headless declaring panel, or one whose settings carry
                // none): panel creation warns loudly about a missing theme, so hand it an empty
                // runtime-created one — default styling, quiet creation.
                createdTheme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                settings.themeStyleSheet = createdTheme;
            }
            return (host, document, settings, createdTheme);
        }

        // Assigning panelSettings is the attach: it inserts the document's root into the settings'
        // (lazily created) panel. Kept last so the panel comes up with the finished configuration.
        private static void AttachDocument(UIDocument document, PanelSettings settings)
            => document.panelSettings = settings;

        // The declaring panel's own PanelSettings, resolved through the public UIDocument surface:
        // the document whose live root sits on that panel carries them. FindObjectsOfTypeAll sees
        // hidden documents, so a portal declared FROM a framework host inherits the same base.
        // Null for panels no document drives (headless editor panels) — defaults apply.
        private static PanelSettings? ResolveDeclaringSettings(IPanel? declaringPanel)
        {
            if (declaringPanel == null)
            {
                return null;
            }
            foreach (var document in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (document != null
                    && document.rootVisualElement?.panel == declaringPanel
                    && document.panelSettings != null)
                {
                    return document.panelSettings;
                }
            }
            return null;
        }

        // Destroys everything a record owns: the host GameObject (detaching the document from its
        // panel) and the runtime-created assets. Null-tolerant so a partially dead record (scene
        // unload) still releases the rest.
        public static void Destroy(PanelHostRecord record)
        {
            if (record.Host != null)
            {
                VelvetObjectUtil.Destroy(record.Host);
            }
            if (record.Settings != null)
            {
                VelvetObjectUtil.Destroy(record.Settings);
            }
            if (record.CreatedTheme != null)
            {
                VelvetObjectUtil.Destroy(record.CreatedTheme);
            }
        }
    }
}
