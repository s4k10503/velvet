using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The re-derive triggers a manipulator of a text leaf shares: attach, the target's own and its parent's
    // GeometryChangedEvent, and ChangeEvent<string>. A subclass derives from the target's text and the
    // parent's box and guards its re-derive with a signature over its inputs.
    //
    // The parent's event reaches the leaves through one ParentHub per parent, so a container of N text
    // leaves carries one callback rather than N, as ColorReferenceWatch's Hub does for its readers.
    internal abstract class StyleTextItemManipulator : Manipulator
    {
        // Answers whether the target's parent is a grid container, whose manipulator writes the same slots.
        private readonly ReconcilerContext _ctx;

        protected StyleTextItemManipulator(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // What the target shows, which is its text unless the text-effect resolver gave the engine another string.
        protected string DisplayedText(TextElement textElement)
            => StyleTextEffectResolver.DisplayedText(_ctx, textElement);

        // Subclasses compare a freshly computed signature against these and record it once a derive settles.
        protected int _lastSignature;
        protected bool _hasSignature;

        // Tracked so the leaf can leave the hub of the exact element it joined.
        private VisualElement? _subscribedParent;

        // Whether a change of the parent's box, with its direction unmoved, can change what Derive writes. A
        // subclass narrows it once a derive has read what it depends on; until then the parent's event derives.
        protected virtual bool ReadsParentBox => true;

        // The leaves of one parent, which hears the parent's GeometryChangedEvent once for all of them.
        private sealed class ParentHub
        {
            public readonly VisualElement Parent;
            public readonly List<StyleTextItemManipulator> Leaves = new();
            // The direction the parent had at the last event, which a leaf that ignores a plain resize still
            // re-derives on: a flip changes which axis the leaf writes.
            public FlexDirection? Direction;

            public ParentHub(VisualElement parent) => Parent = parent;
        }

        private static readonly ConditionalWeakTable<VisualElement, ParentHub> s_hubs = new();

        private static readonly EventCallback<GeometryChangedEvent, ParentHub> s_onParentGeometry = (evt, hub) =>
        {
            var direction = hub.Parent.resolvedStyle.flexDirection;
            var moved = hub.Direction != direction;
            hub.Direction = direction;
            // A leaf's derive can move it to another parent, which shortens the list under this loop.
            for (var i = hub.Leaves.Count - 1; i >= 0; i--)
            {
                if (i < hub.Leaves.Count)
                {
                    hub.Leaves[i].OnParentGeometryChanged(moved);
                }
            }
        };

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
            _hasSignature = false;
            // Below a discarded teardown root the target and its parent leave together, and so do the callbacks
            // registered on either (ReconcilerContext.IsBelowDiscardedRoot). The leaf still leaves its hub's
            // list, so a parent that is mounted again finds no leaf of a removed element there.
            if (_ctx.IsBelowDiscardedRoot(target))
            {
                if (_subscribedParent != null && s_hubs.TryGetValue(_subscribedParent, out var hub))
                {
                    hub.Leaves.Remove(this);
                }
                _subscribedParent = null;
                return;
            }
            SyncParentSubscription(null);
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

        private void OnParentGeometryChanged(bool directionMoved)
        {
            if (directionMoved || !_hasSignature || ReadsParentBox)
            {
                Apply();
            }
        }

        // Re-pointed from every Apply, not only from AttachToPanelEvent, so a mid-life reparent is caught
        // without depending on how UI Toolkit sequences Attach/Detach for a same-panel reparent.
        protected void SyncParentSubscription(VisualElement? parent)
        {
            if (ReferenceEquals(parent, _subscribedParent))
            {
                return;
            }
            if (_subscribedParent != null && s_hubs.TryGetValue(_subscribedParent, out var left)
                && left.Leaves.Remove(this) && left.Leaves.Count == 0)
            {
                _subscribedParent.UnregisterCallback(s_onParentGeometry);
            }
            _subscribedParent = parent;
            if (parent == null)
            {
                return;
            }
            var hub = s_hubs.GetValue(parent, static p => new ParentHub(p));
            if (hub.Leaves.Count == 0)
            {
                hub.Direction = null;
                parent.RegisterCallback(s_onParentGeometry, hub);
            }
            hub.Leaves.Add(this);
        }
    }
}
