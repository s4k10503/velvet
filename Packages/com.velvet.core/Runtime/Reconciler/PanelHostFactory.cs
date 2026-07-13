#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A framework-owned host panel backing a layer portal or a world-space node. The UIDocument is
    // the one stored handle — the GameObject and PanelSettings derive from it — and CreatedTheme is
    // the empty theme created when no declaring theme was resolvable (tracked so disposal destroys
    // exactly what was created). BaseOrder is the sorting base the host was anchored to, recorded so
    // a portal declared INSIDE this host anchors to the SAME base instead of compounding this host's
    // own layer offset on top. DeclaringResolved is false while the host was configured from
    // defaults because no declaring panel settings were resolvable yet; a later drain that touches
    // the host retries the resolution and re-copies once. Held in ReconcilerContext.LayerHosts /
    // WorldSpaceBindings; destroyed by FiberElementCleaner (world-space, with its placeholder) and
    // the reconciler dispose sweep.
    internal sealed class PanelHostRecord
    {
        public readonly UIDocument Document;
        public readonly ThemeStyleSheet? CreatedTheme;
        public float BaseOrder;
        public bool DeclaringResolved;

        public GameObject? Host => Document != null ? Document.gameObject : null;
        public PanelSettings? Settings => Document != null ? Document.panelSettings : null;

        public PanelHostRecord(UIDocument document, ThemeStyleSheet? createdTheme)
        {
            Document = document;
            CreatedTheme = createdTheme;
        }
    }

    // Creates and destroys the framework-owned host panels behind V.Portal(layer:) and
    // V.WorldSpace. A host is an ordinary UIDocument-driven runtime panel whose base settings
    // (theme, scaling, text settings, target display) copy the panel the portal was DECLARED on, so
    // layer and world-space UI resolves the same styles as the tree it logically belongs to.
    internal static class PanelHostFactory
    {
        // Layer sorting relative to the resolved sorting base. The gaps leave room for user panels
        // to slot between the framework layers without colliding with them.
        private const float BackgroundSortingOffset = -100f;
        private const float OverlaySortingOffset = 100f;
        private const float TopmostSortingOffset = 200f;

        private static float SortingOffset(UILayer layer) => layer switch
        {
            UILayer.Background => BackgroundSortingOffset,
            UILayer.Topmost => TopmostSortingOffset,
            _ => OverlaySortingOffset,
        };

        // One screen-space host panel for a UILayer, sorted around the resolved base order.
        public static PanelHostRecord CreateLayerHost(UILayer layer, IPanel? declaringPanel, ReconcilerContext ctx)
        {
            var (declaring, baseOrder) = ResolveDeclaring(declaringPanel, ctx);
            var (record, settings) = CreateHostParts($"VelvetLayer-{layer}", declaring);
            record.BaseOrder = baseOrder;
            record.DeclaringResolved = declaring != null;
            settings.sortingOrder = baseOrder + SortingOffset(layer);
            AttachDocument(record.Document, settings);
            return record;
        }

        // One world-space host panel for a V.WorldSpace instance: render mode WorldSpace, a fixed
        // virtual panel resolution, and the node's transform on the host GameObject (a root
        // world-space document follows its GameObject transform).
        public static PanelHostRecord CreateWorldSpaceHost(WorldSpaceNode node, IPanel? declaringPanel, ReconcilerContext ctx)
        {
            var (declaring, baseOrder) = ResolveDeclaring(declaringPanel, ctx);
            var (record, settings) = CreateHostParts("VelvetWorldSpace", declaring);
            record.BaseOrder = baseOrder;
            record.DeclaringResolved = declaring != null;
            settings.renderMode = PanelRenderMode.WorldSpace;
            record.Document.transform.SetPositionAndRotation(node.Position, node.Rotation);
            AttachDocument(record.Document, settings);
            // The document derives its root sizing from (settings, size mode, size) but only
            // re-derives on a VALUE change, and the attach itself never re-runs it — so both size
            // settings are driven after the attach, with the mode round-tripped so the fixed
            // sizing is derived exactly once against the live world-space settings even when the
            // requested size equals the document's own defaults.
            record.Document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Dynamic;
            record.Document.worldSpaceSize = node.PanelSize;
            record.Document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
            return record;
        }

        // A host created while the declaring panel was unresolvable (a headless mount, a panel that
        // gained settings later) keeps defaults and base 0 otherwise: retry the resolution and
        // re-copy the declaring configuration — theme, scaling, text settings and sorting — the
        // first time it resolves. The empty created theme is superseded on the settings but still
        // destroyed with the record, so nothing leaks either way.
        public static void TryUpgradeDeclaring(PanelHostRecord record, UILayer layer, IPanel? declaringPanel, ReconcilerContext ctx)
        {
            var (declaring, baseOrder) = ResolveDeclaring(declaringPanel, ctx);
            var settings = record.Settings;
            if (declaring == null || settings == null)
            {
                return;
            }
            CopyDeclaringSettings(declaring, settings);
            record.BaseOrder = baseOrder;
            record.DeclaringResolved = true;
            settings.sortingOrder = baseOrder + SortingOffset(layer);
        }

        // The shared skeleton: the hidden GameObject + document + a runtime PanelSettings carrying
        // the declaring panel's base configuration. The settings are returned unattached — callers
        // finish configuring them first, then AttachDocument assigns them (the assignment is what
        // creates the backing panel, so it must see the final configuration).
        private static (PanelHostRecord Record, PanelSettings Settings) CreateHostParts(string name, PanelSettings? declaring)
        {
            var hostObject = new GameObject(name);
            VelvetObjectUtil.HideFrameworkSceneObject(hostObject);
            var document = hostObject.AddComponent<UIDocument>();
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            if (declaring != null)
            {
                CopyDeclaringSettings(declaring, settings);
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
            return (new PanelHostRecord(document, createdTheme), settings);
        }

        // Assigning panelSettings is the attach: it inserts the document's root into the settings'
        // (lazily created) panel. Kept last so the panel comes up with the finished configuration.
        private static void AttachDocument(UIDocument document, PanelSettings settings)
            => document.panelSettings = settings;

        // The base configuration a host copies from the panel its portal was declared on.
        private static void CopyDeclaringSettings(PanelSettings from, PanelSettings to)
        {
            to.themeStyleSheet = from.themeStyleSheet;
            to.scaleMode = from.scaleMode;
            to.scale = from.scale;
            to.referenceResolution = from.referenceResolution;
            to.screenMatchMode = from.screenMatchMode;
            to.match = from.match;
            to.textSettings = from.textSettings;
            to.targetDisplay = from.targetDisplay;
        }

        // Resolves the declaring panel's own PanelSettings plus the sorting BASE that portals
        // declared on that panel anchor to. Resolution goes through the public UIDocument surface
        // (the document whose live root sits on the panel; FindObjectsOfTypeAll sees hidden
        // documents). When that document is one of OUR host records, the record's stored BaseOrder
        // is reused as the base — a Background portal declared inside a Topmost host must still
        // sort against the ORIGINAL panel rather than compound the Topmost offset. Successful
        // resolutions are cached per declaring panel (one scan per distinct panel per reconciler);
        // failures are NOT cached, so a panel that gains a driving document later resolves on retry
        // (the late-declaring upgrade path).
        private static (PanelSettings? Settings, float BaseOrder) ResolveDeclaring(IPanel? declaringPanel, ReconcilerContext ctx)
        {
            if (declaringPanel == null)
            {
                return (null, 0f);
            }
            if (ctx.DeclaringSettingsCache.TryGetValue(declaringPanel, out var cached))
            {
                return cached;
            }
            foreach (var document in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (document == null
                    || document.rootVisualElement?.panel != declaringPanel
                    || document.panelSettings == null)
                {
                    continue;
                }
                var result = (Settings: document.panelSettings, BaseOrder: document.panelSettings.sortingOrder);
                foreach (var record in ctx.LayerHosts.Values)
                {
                    if (record.Document == document)
                    {
                        result.BaseOrder = record.BaseOrder;
                    }
                }
                foreach (var record in ctx.WorldSpaceBindings.Values)
                {
                    if (record.Document == document)
                    {
                        result.BaseOrder = record.BaseOrder;
                    }
                }
                ctx.DeclaringSettingsCache[declaringPanel] = result;
                return result;
            }
            return (null, 0f);
        }

        // Destroys everything a record owns: the host GameObject (detaching the document from its
        // panel) and the runtime-created assets. The settings are captured BEFORE the GameObject
        // dies — they derive from the document, which reads as dead afterwards. Null-tolerant so a
        // partially dead record (scene unload) still releases the rest.
        public static void Destroy(PanelHostRecord record)
        {
            var settings = record.Settings;
            var host = record.Host;
            if (host != null)
            {
                VelvetObjectUtil.Destroy(host);
            }
            if (settings != null)
            {
                VelvetObjectUtil.Destroy(settings);
            }
            if (record.CreatedTheme != null)
            {
                VelvetObjectUtil.Destroy(record.CreatedTheme);
            }
        }
    }
}
