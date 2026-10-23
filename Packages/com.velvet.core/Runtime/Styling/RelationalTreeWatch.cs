#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // Retargets a context's relational consumers once a `group` or `peer` source joins or leaves a panel, or a
    // parent reorders its children, by anything other than one of the context's own passes, whose end retargets
    // already (StyleRelationalVariantManipulator.RetargetAll). The retarget is scheduled on the panel rather than run
    // in the announcement, which UI Toolkit sends while the change is still under way: a removal is announced before
    // the child leaves the list, and a payload the retarget lights can itself change the tree.
    internal sealed class RelationalTreeWatch
    {
        private static readonly MethodInfo? s_add = EngineMember.PanelHierarchyChangedAdd.ResolveMethod();
        private static readonly MethodInfo? s_remove = EngineMember.PanelHierarchyChangedRemove.ResolveMethod();

        // Without every change kind told apart, an announcement of one kind would be read as another.
        private static readonly bool s_kindsResolved = EngineMember.HierarchyAddedToParent.ResolveField() != null
            && EngineMember.HierarchyRemovedFromParent.ResolveField() != null
            && EngineMember.HierarchyChildrenReordered.ResolveField() != null;

        private readonly ReconcilerContext _ctx;
        private readonly Dictionary<IPanel, Delegate> _watched = new();
        private bool _scheduled;

        public RelationalTreeWatch(ReconcilerContext ctx) => _ctx = ctx;

        public void Watch(IPanel? panel)
        {
            if (panel == null || s_add == null || s_remove == null || !s_kindsResolved || _watched.ContainsKey(panel))
            {
                return;
            }
            var handlerType = s_add.GetParameters()[0].ParameterType;
            var changeType = handlerType.GetMethod(nameof(Action.Invoke))?.GetParameters()[1].ParameterType;
            if (changeType == null || !changeType.IsEnum)
            {
                return;
            }
            var handler = (IHandler)Activator.CreateInstance(typeof(Handler<>).MakeGenericType(changeType), this);
            var bound = handler.Bind(handlerType);
            s_add.Invoke(panel, new object[] { bound });
            _watched[panel] = bound;
        }

        public void UnwatchAll()
        {
            foreach (var pair in _watched)
            {
                s_remove?.Invoke(pair.Key, new object[] { pair.Value });
            }
            _watched.Clear();
        }

        private void OnChanged(VisualElement element, bool reordered)
        {
            if (_scheduled || _ctx.SharedReconcileDepth > 0 || _ctx.PointerEventsWalkHeld > 0)
            {
                return;
            }
            if (!reordered && !IsSource(element))
            {
                return;
            }
            var root = element.panel?.visualTree;
            if (root == null)
            {
                return;
            }
            _scheduled = true;
            root.schedule.Execute(Retarget);
        }

        private void Retarget()
        {
            _scheduled = false;
            StyleRelationalVariantManipulator.RetargetAll(_ctx);
        }

        // Whether element carries a class a group or peer binding looks for (SourceClassFor), named or not.
        private static bool IsSource(VisualElement element)
        {
            foreach (var cls in element.GetClasses())
            {
                if (IsSourceClass(cls, StyleRelationalVariantManipulator.GroupClass)
                    || IsSourceClass(cls, StyleRelationalVariantManipulator.PeerClass))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsSourceClass(string cls, string baseClass)
            => cls.StartsWith(baseClass, StringComparison.Ordinal)
                && (cls.Length == baseClass.Length || cls[baseClass.Length] == '/');

        private interface IHandler
        {
            Delegate Bind(Type handlerType);
        }

        // The panel's event passes its change as an internal enum, which a delegate binds only to a method taking
        // that same type, so the handler is closed over it.
        private sealed class Handler<TChange> : IHandler where TChange : struct
        {
            private readonly RelationalTreeWatch _watch;
            private readonly TChange _added;
            private readonly TChange _removed;
            private readonly TChange _reordered;

            public Handler(RelationalTreeWatch watch)
            {
                _watch = watch;
                _added = ValueOf(EngineMember.HierarchyAddedToParent);
                _removed = ValueOf(EngineMember.HierarchyRemovedFromParent);
                _reordered = ValueOf(EngineMember.HierarchyChildrenReordered);
            }

            private static TChange ValueOf(EngineMember member) => (TChange)member.ResolveField()!.GetValue(null);

            public Delegate Bind(Type handlerType)
            {
                Action<VisualElement, TChange, IReadOnlyList<VisualElement>?> on = On;
                return Delegate.CreateDelegate(handlerType, this, on.Method);
            }

            private void On(VisualElement element, TChange change, IReadOnlyList<VisualElement>? additionalContext)
            {
                var comparer = EqualityComparer<TChange>.Default;
                if (comparer.Equals(change, _reordered))
                {
                    _watch.OnChanged(element, reordered: true);
                }
                else if (comparer.Equals(change, _added) || comparer.Equals(change, _removed))
                {
                    _watch.OnChanged(element, reordered: false);
                }
            }
        }
    }
}
