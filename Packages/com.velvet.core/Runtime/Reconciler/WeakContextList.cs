#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // The contexts a registry read across roots holds, none of which it keeps alive: a context nothing else holds goes,
    // and its entry with it at the next Add.
    internal sealed class WeakContextList
    {
        private readonly List<WeakReference<ReconcilerContext>> _items = new();

        internal int Count => _items.Count;

        internal bool TryGet(int index, out ReconcilerContext ctx) => _items[index].TryGetTarget(out ctx!);

        internal void Add(ReconcilerContext ctx)
        {
            // MUTANT_SURVIVES(equivalent, boundary): an entry the walk never reaches is a context added again, which a
            // reader then visits twice against the same tree, or a gone one, which every reader skips.
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (!_items[i].TryGetTarget(out var held))
                {
                    // MUTANT_SURVIVES(equivalent, line removed): every reader skips an entry TryGet hands nothing back
                    // for.
                    _items.RemoveAt(i);
                }
                else if (ReferenceEquals(held, ctx))
                {
                    return;
                }
            }
            _items.Add(new WeakReference<ReconcilerContext>(ctx));
        }
    }
}
