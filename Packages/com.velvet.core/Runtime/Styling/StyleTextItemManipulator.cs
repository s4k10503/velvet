using UnityEngine.UIElements;

namespace Velvet
{
    // The re-derive triggers StyleTextBalanceManipulator and StyleFlexMinSizeManipulator share: attach, the
    // target's own and its parent's GeometryChangedEvent, and ChangeEvent<string>. Each derives from the
    // target's text and the parent's box, writes an inline value, and guards its re-derive with a signature
    // over its inputs.
    internal abstract class StyleTextItemManipulator : Manipulator
    {
        // Answers whether the target's parent is a grid container, whose manipulator writes the same slots.
        private readonly ReconcilerContext _ctx;

        protected StyleTextItemManipulator(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Subclasses compare a freshly computed signature against these and record it once a derive settles.
        protected int _lastSignature;
        protected bool _hasSignature;

        // Tracked so the callback can be unregistered from the exact element it was registered on.
        private VisualElement? _subscribedParent;

        // StyleGridManipulator writes its children's own style.width. Asks the registry of attached grid
        // manipulators rather than re-deriving the grid's class condition, so the two cannot drift apart.
        // Walks ancestors because the grid sizes the children of GetChildContainer(target), and on any
        // widget carrying a contentContainer redirect — ScrollView, Foldout, TabView, … — that inner box
        // sits below the element the manipulator is keyed on; the match is that container being this
        // element's own parent, so no unrelated ancestor grid can claim it.
        protected bool IsSizedByGridParent(VisualElement parent)
        {
            if (_ctx.GridManipulators.Count == 0)
            {
                return false;
            }
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (_ctx.GridManipulators.ContainsKey(ancestor)
                    && ReferenceEquals(FiberNodePatcher.GetChildContainer(ancestor), parent))
                {
                    return true;
                }
            }
            return false;
        }

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
