using System;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// The cleanup a test ref callback returns when it has nothing to release. A callback that returns
    /// <c>null</c> instead is called again with <c>null</c> when its element goes, which a test that reads its
    /// element in the callback is not written for.
    /// </summary>
    public static class RefCleanup
    {
        /// <summary>A cleanup that does nothing.</summary>
        public static readonly Action None = () => { };
    }
}
