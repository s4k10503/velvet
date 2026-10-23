using UnityEngine.UIElements;

namespace Velvet
{
    // A composite widget reconciles its children into an inner box (FiberNodePatcher.GetChildContainer), so a
    // direction or wrap class written on the widget would lay out only the widget's own box. A CSS flex utility
    // lays out the element's own children, so this writes the verdict of the widget's direction classes
    // (StyleFlexDirectionResolver.FromClasses) and wrap classes (WrapFromClasses) inline onto the inner box, and
    // takes each back once no such class is left.
    //
    // It watches the widget's own class list (StyleArbitraryValueResolver.Watch), so a variant toggling a class
    // there moves the inner box without a reconcile. A gap on the widget reads the inner box's inline value
    // before a class (StyleGapManipulator.DirectionOf), so Reapply re-applies that gap once a forwarded value moves.
    //
    // Lifecycle mirrors StyleGapManipulator: the reconciler attaches one per widget whose child container is not
    // the widget itself, keeps it in ReconcilerContext.CompositeFlowManipulators, and removes it on cleanup /
    // dispose.
    internal sealed class StyleCompositeFlowManipulator : Manipulator, IChildClassWatcher
    {
        private readonly ReconcilerContext _ctx;

        // The inner box written to, and the value written on each property; null where nothing was written.
        private VisualElement? _inner;
        private FlexDirection? _direction;
        private Wrap? _wrap;

        public StyleCompositeFlowManipulator(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            StyleArbitraryValueResolver.Watch(target, this);
            Forward();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            StyleArbitraryValueResolver.Unwatch(target, this);
            TakeBack();
        }

        // The widget's class list changed.
        public void Reapply()
        {
            if (!Forward())
            {
                return;
            }
            if (_ctx.GapManipulators.TryGetValue(target, out var gap))
            {
                gap.Reapply();
            }
        }

        // Writes the widget's current verdicts onto its inner box; true when either moved.
        public bool Forward()
        {
            var inner = FiberNodePatcher.GetChildContainer(target);
            if (!ReferenceEquals(inner, _inner))
            {
                TakeBack();
                _inner = ReferenceEquals(inner, target) ? null : inner;
            }
            if (_inner == null)
            {
                return false;
            }
            var direction = StyleFlexDirectionResolver.FromClasses(target);
            var wrap = StyleFlexDirectionResolver.WrapFromClasses(target);
            var moved = direction != _direction || wrap != _wrap;
            if (direction != null)
            {
                _inner.style.flexDirection = direction.Value;
            }
            else
            {
                TakeBackDirection(_inner);
            }
            if (wrap != null)
            {
                _inner.style.flexWrap = wrap.Value;
            }
            else
            {
                TakeBackWrap(_inner);
            }
            _direction = direction;
            _wrap = wrap;
            return moved;
        }

        private void TakeBack()
        {
            if (_inner == null)
            {
                return;
            }
            TakeBackDirection(_inner);
            TakeBackWrap(_inner);
            _direction = null;
            _wrap = null;
            _inner = null;
        }

        // A value code outside Velvet wrote over the forwarded one since is left where it is.
        private void TakeBackDirection(VisualElement inner)
        {
            var written = inner.style.flexDirection;
            if (_direction != null && written.keyword == StyleKeyword.Undefined && written.value == _direction.Value)
            {
                inner.style.flexDirection = StyleKeyword.Null;
            }
        }

        private void TakeBackWrap(VisualElement inner)
        {
            var written = inner.style.flexWrap;
            if (_wrap != null && written.keyword == StyleKeyword.Undefined && written.value == _wrap.Value)
            {
                inner.style.flexWrap = StyleKeyword.Null;
            }
        }
    }
}
