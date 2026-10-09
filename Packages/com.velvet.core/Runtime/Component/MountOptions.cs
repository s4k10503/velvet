using System;

namespace Velvet
{
    /// <summary>
    /// Options passed to <see cref="V.Mount(UnityEngine.UIElements.VisualElement, VNode, MountOptions)"/>, as
    /// React's <c>createRoot</c> takes an options object.
    /// </summary>
    /// <param name="OnCaughtError">
    /// Called when an error boundary in this tree catches an error — React's <c>onCaughtError</c>. It is
    /// called in the commit that shows the boundary's fallback, after the fallback's layout effects and before
    /// those of the boundary's ancestors; a boundary that an ancestor boundary replaces before that commit
    /// reports nothing. A boundary that a time-sliced transition mounted or re-rendered, catching before the
    /// transition completes, reports in the commit that completes it, after its own layout effects.
    /// It receives the caught exception and an <see cref="ErrorInfo"/> naming the boundary and carrying the
    /// component stack. When null, the error is logged with <c>Debug.LogException</c>, together with the
    /// boundary's name and the component stack. An exception the handler throws is logged and does not
    /// reach the tree.
    /// </param>
    public sealed record MountOptions(Action<Exception, ErrorInfo>? OnCaughtError = null)
    {
        /// <summary>
        /// The clock this tree's motion advances on, from its first render on.
        /// <see cref="Velvet.MotionClock.Realtime"/> by default; <see cref="Velvet.MotionClock.GameTime"/> or a
        /// clock of the application's own keeps that motion in step with game time. See <see cref="Velvet.MotionClock"/>.
        /// </summary>
        public MotionClock MotionClock { get; init; } = MotionClock.Realtime;
    }
}
