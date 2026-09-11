using System;
using System.Threading;

namespace Velvet
{
    // The source behind a token the router hands to a Loader or a Blocker. Either can go on reading the token
    // after the source is cancelled, and nothing the router holds says when the last such read has happened,
    // so the source is never disposed: disposing it is what makes a read of the token's wait handle throw,
    // which CancellationTokenReleaseTests pins. It is linked by a registration rather than created as a linked
    // source because only a registration can be dropped without disposing the source.
    internal sealed class RouteCancellationSource
    {
        private static readonly Action<object?> CancelLinked = source => ((CancellationTokenSource)source!).Cancel();

        private readonly CancellationTokenSource _source = new();
        private CancellationTokenRegistration _link;

        // The link is registered with execution-context flow suppressed: capturing the context costs four
        // allocation blocks a navigation, which RouterNavigationAllocationEditorTests pins. SuppressFlow throws
        // where flow is suppressed already, which RouterTests pins for a caller that suppressed it.
        internal RouteCancellationSource(CancellationToken linkedTo)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                _link = linkedTo.Register(CancelLinked, _source);
                return;
            }
            using (ExecutionContext.SuppressFlow())
            {
                _link = linkedTo.Register(CancelLinked, _source);
            }
        }

        internal CancellationToken Token => _source.Token;

        // Unlinked first: the cancellation runs callbacks that can throw, and a link left behind lets the token
        // this source was linked to keep it alive.
        internal void Cancel()
        {
            _link.Dispose();
            _source.Cancel();
        }

        internal void Unlink() => _link.Dispose();
    }
}
