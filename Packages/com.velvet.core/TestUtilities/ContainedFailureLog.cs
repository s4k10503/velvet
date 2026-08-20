using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// The log a contained failure leaves behind. <c>FiberLogger.LogException</c> reports on two lines, and
    /// a case that expects one of them fails on the other as an unhandled error.
    /// </summary>
    public static class ContainedFailureLog
    {
        /// <param name="tag">The <c>FiberLogger</c> tag of the site that caught the exception.</param>
        /// <param name="message">The message the case arranged the throw with.</param>
        public static void Expect<TException>(string tag, string message)
            where TException : System.Exception
        {
            LogAssert.Expect(LogType.Error, $"[{tag}] An exception occurred. See the next line for details.");
            LogAssert.Expect(LogType.Exception, $"{typeof(TException).Name}: {message}");
        }
    }
}
