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
    /// A full mount invokes the selected setup and disposes its returned handle on unmount. Args-only updates
    /// retain the existing environment.
    /// <para>
    /// At most one setup per assembly is honored; a second is ignored with a warning so the environment a story
    /// mounts into stays unambiguous.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class VelvetPreviewSetupAttribute : Attribute
    {
    }
}
#endif
