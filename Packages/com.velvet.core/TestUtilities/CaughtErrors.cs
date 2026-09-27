using System;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// For a case that arranges an error boundary's catch and asserts what the boundary did with it: the caught
    /// error goes unlogged, while an error no boundary catches is still logged, and the case fails on that log
    /// as unhandled.
    /// </summary>
    public static class CaughtErrors
    {
        /// <summary>An <see cref="MountOptions.OnCaughtError"/> that does nothing.</summary>
        public static Action<Exception, ErrorInfo> Ignore { get; } = static (_, _) => { };

        /// <summary>Mount options carrying <see cref="Ignore"/>.</summary>
        public static MountOptions Unlogged { get; } = new(Ignore);
    }
}
