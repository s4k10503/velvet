#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A framework-owned host panel backing a layer portal or a world-space node. The UIDocument is
    // the one stored handle — the GameObject and PanelSettings derive from it. BaseOrder is the
    // sorting base the host was anchored to, recorded so a portal declared INSIDE this host anchors
    // to the SAME base instead of compounding this host's own layer offset on top.
    // DeclaringResolved is false while the host was configured from defaults because no declaring
    // panel settings were resolvable yet; the passes that touch the host keep re-syncing through
    // PanelHostFactory.SyncDeclaring (late resolution and runtime drift alike). Held in
    // ReconcilerContext.LayerHosts / WorldSpaceBindings; destroyed by FiberElementCleaner
    // (world-space, with its placeholder) and the reconciler dispose sweep.
    internal sealed class PanelHostRecord
    {
        public readonly UIDocument Document;
        public float BaseOrder;
        public bool DeclaringResolved;

        public GameObject? Host => Document != null ? Document.gameObject : null;
        public PanelSettings? Settings => Document != null ? Document.panelSettings : null;

        public PanelHostRecord(UIDocument document)
        {
            Document = document;
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

        // One empty theme shared by every host whose declaring panel resolves none: panel creation
        // warns loudly about a missing theme, and per-host instances would pile up one
        // ScriptableObject per host for identical default styling. Never destroyed (records do not
        // own it); HideAndDontSave keeps it out of scenes and saves, and a domain reload simply
        // recreates it on demand.
        private static ThemeStyleSheet? s_sharedEmptyTheme;

        private static ThemeStyleSheet SharedEmptyTheme
        {
            get
            {
                if (s_sharedEmptyTheme == null)
                {
                    s_sharedEmptyTheme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                    s_sharedEmptyTheme.name = "VelvetSharedEmptyTheme";
                    s_sharedEmptyTheme.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_sharedEmptyTheme;
            }
        }

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

        // Late resolution and runtime drift, handled at one recurring re-sync point: a host created
        // while the declaring panel was unresolvable (a headless mount, a panel that gained settings
        // later) keeps defaults until the first resolution lands here, and an already-resolved host
        // re-copies whenever the declaring panel's settings were mutated at runtime (a theme swap, a
        // scale change) — the copy is a snapshot, so the passes that touch the host re-check it.
        // Layer hosts also re-anchor their sorting; world-space callers pass null (their panels
        // depth-sort in the scene, not by sorting order).
        public static void SyncDeclaring(PanelHostRecord record, UILayer? layer, IPanel? declaringPanel, ReconcilerContext ctx)
        {
            var (declaring, baseOrder) = ResolveDeclaring(declaringPanel, ctx);
            var settings = record.Settings;
            if (declaring == null || settings == null)
            {
                return;
            }
            // Compare-and-assign inside the copy: one field list serves both drift detection and the
            // re-copy (twin lists would drift apart), and untouched fields never re-dirty the panel.
            var copied = CopyDeclaringSettings(declaring, settings);
            var rebased = !record.DeclaringResolved || record.BaseOrder != baseOrder;
            record.BaseOrder = baseOrder;
            record.DeclaringResolved = true;
            if ((copied || rebased) && layer is { } resolvedLayer)
            {
                settings.sortingOrder = baseOrder + SortingOffset(resolvedLayer);
            }
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
            if (settings.themeStyleSheet == null)
            {
                // No resolvable theme (a headless declaring panel, or one whose settings carry
                // none): panel creation warns loudly about a missing theme, so hand it the shared
                // empty one — default styling, quiet creation.
                settings.themeStyleSheet = SharedEmptyTheme;
            }
            return (new PanelHostRecord(document), settings);
        }

        // Assigning panelSettings is the attach: it inserts the document's root into the settings'
        // (lazily created) panel. Kept last so the panel comes up with the finished configuration.
        private static void AttachDocument(UIDocument document, PanelSettings settings)
            => document.panelSettings = settings;

        // The base configuration a host copies from the panel its portal was declared on, written
        // compare-and-assign so the return value doubles as the drift signal. The shared empty theme
        // stands in when the declaring panel carries none — writing a null theme through would
        // re-trigger the loud missing-theme warning on the live panel.
        private static bool CopyDeclaringSettings(PanelSettings from, PanelSettings to)
        {
            var changed = false;
            var theme = from.themeStyleSheet != null ? from.themeStyleSheet : SharedEmptyTheme;
            if (to.themeStyleSheet != theme)
            {
                to.themeStyleSheet = theme;
                changed = true;
            }
            if (to.scaleMode != from.scaleMode)
            {
                to.scaleMode = from.scaleMode;
                changed = true;
            }
            if (to.scale != from.scale)
            {
                to.scale = from.scale;
                changed = true;
            }
            if (to.referenceResolution != from.referenceResolution)
            {
                to.referenceResolution = from.referenceResolution;
                changed = true;
            }
            if (to.screenMatchMode != from.screenMatchMode)
            {
                to.screenMatchMode = from.screenMatchMode;
                changed = true;
            }
            if (to.match != from.match)
            {
                to.match = from.match;
                changed = true;
            }
            if (to.referenceDpi != from.referenceDpi)
            {
                to.referenceDpi = from.referenceDpi;
                changed = true;
            }
            if (to.fallbackDpi != from.fallbackDpi)
            {
                to.fallbackDpi = from.fallbackDpi;
                changed = true;
            }
            if (to.textSettings != from.textSettings)
            {
                to.textSettings = from.textSettings;
                changed = true;
            }
            if (to.targetDisplay != from.targetDisplay)
            {
                to.targetDisplay = from.targetDisplay;
                changed = true;
            }
            return changed;
        }

        // Resolves the declaring panel's own PanelSettings plus the sorting BASE that portals
        // declared on that panel anchor to. Resolution goes through the public UIDocument surface
        // (the document whose live root sits on the panel; FindObjectsOfTypeAll sees hidden
        // documents). When that document is one of OUR host records, the record's stored BaseOrder
        // is reused as the base — a Background portal declared inside a Topmost host must still
        // sort against the ORIGINAL panel rather than compound the Topmost offset. Successful
        // resolutions are cached per declaring panel (one scan per distinct panel per reconciler);
        // failures are remembered only for the current top-level pass, so a panel that gains a
        // driving document later still resolves on a later pass (the late-declaring upgrade path).
        private static (PanelSettings? Settings, float BaseOrder) ResolveDeclaring(IPanel? declaringPanel, ReconcilerContext ctx)
        {
            if (declaringPanel == null)
            {
                return (null, 0f);
            }
            if (ctx.DeclaringSettingsCache.TryGetValue(declaringPanel, out var cachedDocument)
                && cachedDocument != null && cachedDocument.panelSettings != null)
            {
                // Only the document lookup is cached; the settings and the sorting base are re-read
                // live so a runtime sortingOrder change (or a nested host re-anchoring) reaches
                // later syncs. A cached document killed externally falls through to a fresh scan.
                return (cachedDocument.panelSettings, DeriveBaseOrder(cachedDocument, ctx));
            }
            if (ctx.DeclaringResolveMisses.Contains(declaringPanel))
            {
                return (null, 0f);
            }
            foreach (var document in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (document == null
                    || document.rootVisualElement?.panel != declaringPanel
                    || document.panelSettings == null)
                {
                    continue;
                }
                ctx.DeclaringSettingsCache[declaringPanel] = document;
                return (document.panelSettings, DeriveBaseOrder(document, ctx));
            }
            ctx.DeclaringResolveMisses.Add(declaringPanel);
            return (null, 0f);
        }

        // The sorting base portals declared on this document anchor to: one of OUR host records
        // anchors to that record's own live base (a Background portal declared inside a Topmost host
        // must sort against the ORIGINAL panel rather than compound the Topmost offset), and a plain
        // user panel anchors to its settings' current sortingOrder.
        private static float DeriveBaseOrder(UIDocument document, ReconcilerContext ctx)
        {
            foreach (var record in ctx.LayerHosts.Values)
            {
                if (record.Document == document)
                {
                    return record.BaseOrder;
                }
            }
            foreach (var record in ctx.WorldSpaceBindings.Values)
            {
                if (record.Document == document)
                {
                    return record.BaseOrder;
                }
            }
            return document.panelSettings.sortingOrder;
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
        }
    }
}
