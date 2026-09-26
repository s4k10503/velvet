using System;

namespace Velvet
{
    /// <summary>
    /// Options passed to <see cref="V.Mount(UnityEngine.UIElements.VisualElement, VNode, MountOptions)"/>, as
    /// React's <c>createRoot</c> takes an options object.
    /// </summary>
    /// <param name="OnCaughtError">
    /// Called once for each error an error boundary in this tree catches, after the boundary has rendered its
    /// fallback — React's <c>onCaughtError</c>. It receives the caught exception and an <see cref="ErrorInfo"/>
    /// naming the boundary and carrying the component stack. When null, the error is logged with
    /// <c>Debug.LogException</c>, together with the boundary's name and the component stack. An exception
    /// the handler throws is logged and does not reach the tree.
    /// </param>
    public sealed record MountOptions(Action<Exception, ErrorInfo>? OnCaughtError = null);
}
