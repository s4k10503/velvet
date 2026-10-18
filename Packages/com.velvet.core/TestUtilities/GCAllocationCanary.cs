using System;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// An allocation of known size, counted before a fixture trusts a zero from the probe, since an
    /// instrument stuck at zero satisfies a guard asserting one.
    /// </summary>
    /// <remarks>
    /// A fixture calls this from a case of its own, so a run filtered to that fixture still carries the proof.
    /// </remarks>
    public static class GCAllocationCanary
    {
        /// <summary>
        /// Counts the blocks <see cref="GCAllocationProbe"/> charges to a delegate allocating one 16-byte array.
        /// </summary>
        public static int BlocksForKnownArray()
        {
            Action canary = () => GC.KeepAlive(new byte[16]);
            canary();
            return GCAllocationProbe.MedianBlocksDuring(canary);
        }
    }
}
