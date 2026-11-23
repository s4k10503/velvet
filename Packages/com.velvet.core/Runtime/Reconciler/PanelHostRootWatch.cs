#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Applies PanelHostFactory.ApplyRootSetup to each root a host's UIDocument builds after the one it was created
    // with. Pinned by FocusReplacedHostPanelTests.
    [ExecuteAlways]
    internal sealed class PanelHostRootWatch : MonoBehaviour
    {
        private PanelHostRecord? _record;
        private ReconcilerContext? _ctx;
        private VisualElement? _root;

        internal static void Watch(PanelHostRecord record, ReconcilerContext ctx)
        {
            var watch = record.Document.gameObject.AddComponent<PanelHostRootWatch>();
            watch._record = record;
            watch._ctx = ctx;
            watch._root = record.Document.rootVisualElement;
        }

        private void LateUpdate()
        {
            if (_record == null || _record.Document == null || _ctx == null || _ctx.IsDisposed)
            {
                return;
            }
            var root = _record.Document.rootVisualElement;
            if (root == null || root.panel == null || ReferenceEquals(root, _root))
            {
                return;
            }
            var previous = _root;
            _root = root;
            PanelHostFactory.ApplyRootSetup(_record, previous, _ctx);
        }
    }
}
