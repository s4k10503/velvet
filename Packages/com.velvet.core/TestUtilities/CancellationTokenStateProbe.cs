using System;
using System.Threading;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Tells a token whose source was released from one whose source was cancelled and left open.
    /// </summary>
    public static class CancellationTokenStateProbe
    {
        /// <summary>
        /// Reports what <paramref name="token"/> answers to a read its source has to serve:
        /// <c>"released"</c> where the source behind it has been disposed, and otherwise
        /// <c>"cancelled"</c> or <c>"not-cancelled"</c>. <c>CancellationTokenReleaseTests</c> pins that
        /// the wait handle is what tells a released source from a cancelled one.
        /// </summary>
        public static string ReadTokenState(CancellationToken token)
        {
            try
            {
                _ = token.WaitHandle;
            }
            catch (ObjectDisposedException)
            {
                return "released";
            }

            return token.IsCancellationRequested ? "cancelled" : "not-cancelled";
        }
    }
}
