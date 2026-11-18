#if UNITY_EDITOR
using System;

namespace Velvet
{
    /// <summary>
    /// Marks a static method that prepares the shared environment for a story mount in its assembly.
    /// </summary>
    /// <remarks>
    /// The annotated method must be <c>static</c>, take no parameters, and return either an
    /// <see cref="IDisposable"/> or an <see cref="Action"/> teardown (or <c>void</c> when nothing needs undoing).
    /// A full mount invokes every valid setup the story's assembly declares, ordered by declaring type and then
    /// method name, and disposes their returned handles in the reverse order on unmount. Args-only updates
    /// retain the existing environment.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class VelvetPreviewSetupAttribute : Attribute
    {
    }
}
#endif
