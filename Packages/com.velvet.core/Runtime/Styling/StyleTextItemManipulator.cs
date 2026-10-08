using UnityEngine.UIElements;

namespace Velvet
{
    // The re-derive triggers StyleTextBalanceManipulator and StyleFlexMinSizeManipulator share: attach, the
    // target's own and its PARENT's GeometryChangedEvent (an ancestor changing size moves nothing on a
    // target whose own rect a write pinned), and ChangeEvent<string>. Each derives from the target's text
    // and the parent's box, writes an inline value, and guards its re-derive with a signature over its
    // inputs so the GeometryChangedEvent its own write provokes falls through.
    internal abstract class StyleTextItemManipulator : Manipulator
    {
        // Subclasses compare a freshly computed signature against these and record it once a derive settles.
        protected int _lastSignature;
        protected bool _hasSignature;

        // Tracked so the callback can be unregistered from the exact element it was registered on.
        private VisualElement? _subscribedParent;

        // Derives for a target that is a TextElement with a parent; a target without one defers, and re-arms
        // the signature so a later resolve is never skipped as a false repeat, mirroring StyleGridManipulator's
        // off-panel deferral.
        protected abstract void Derive(TextElement textElement, VisualElement parent);

        protected void Apply()
        {
            if (target is not TextElement textElement)
            {
                return;
            }

            var parent = textElement.parent;
            SyncParentSubscription(parent);
            if (parent == null)
            {
                _hasSignature = false;
                return;
            }
            Derive(textElement, parent);
        }

        // Releases whatever the subclass wrote. The parent subscription and the signature are the base's.
        protected abstract void Clear();

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            target.RegisterCallback<ChangeEvent<string>>(OnTextChanged);
            Apply();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            Clear();
            SyncParentSubscription(null);
            _hasSignature = false;
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            target.UnregisterCallback<ChangeEvent<string>>(OnTextChanged);
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            _hasSignature = false;
            Apply();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => Apply();

        private void OnTextChanged(ChangeEvent<string> evt) => Apply();

        private void OnParentGeometryChanged(GeometryChangedEvent evt) => Apply();

        // Re-pointed from every Apply, not only from AttachToPanelEvent, so a mid-life reparent is caught
        // without depending on how UI Toolkit sequences Attach/Detach for a same-panel reparent.
        protected void SyncParentSubscription(VisualElement? parent)
        {
            if (ReferenceEquals(parent, _subscribedParent))
            {
                return;
            }
            _subscribedParent?.UnregisterCallback<GeometryChangedEvent>(OnParentGeometryChanged);
            _subscribedParent = parent;
            _subscribedParent?.RegisterCallback<GeometryChangedEvent>(OnParentGeometryChanged);
        }
    }
}
