#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Attaches the focus navigator to the panel of each root a host's UIDocument builds after the one it was
    // created with. Pinned by FocusReplacedHostPanelTests.
    internal sealed class PanelHostRootWatch : MonoBehaviour
    {
        private UIDocument? _document;
        private ReconcilerContext? _ctx;
        private VisualElement? _root;

        internal static void Watch(UIDocument document, ReconcilerContext ctx)
        {
            var watch = document.gameObject.AddComponent<PanelHostRootWatch>();
            watch._document = document;
            watch._ctx = ctx;
            watch._root = document.rootVisualElement;
        }

        private void LateUpdate()
        {
            if (_document == null || _ctx == null || _ctx.IsDisposed)
            {
                return;
            }
            var root = _document.rootVisualElement;
            if (root == null || ReferenceEquals(root, _root))
            {
                return;
            }
            _root = root;
            FiberFocusNavigator.EnsureAttached(root, _ctx);
        }
    }
}
