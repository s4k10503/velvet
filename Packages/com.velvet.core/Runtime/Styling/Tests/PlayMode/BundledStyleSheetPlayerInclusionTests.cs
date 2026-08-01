using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that the bundled utility stylesheet can be resolved from running game code. Without it every
    /// plain utility class resolves to nothing, so a build styles only what Velvet realises from C# and the
    /// rest of the layout collapses silently.
    /// </summary>
    /// <remarks>
    /// This case only discriminates when the suite is run with <c>-testPlatform StandaloneOSX</c>: in the
    /// editor the sheet is read from the asset database whether or not anything put it in a build, and the
    /// player path — the preloaded holder — is the one that can be missing.
    /// <c>BundledStyleSheetInclusionTests</c> is what guards the editor-side mechanism on every run.
    /// </remarks>
    [TestFixture]
    internal sealed class BundledStyleSheetPlayerInclusionTests
    {
        [Test]
        public void Given_TheRunningPlayer_When_TheBundledSheetIsResolved_Then_ItIsThere()
        {
            // Act
            Exception refused = null;
            object sheet = null;
            try
            {
                sheet = VelvetStyleUtilities.Sheet;
            }
            catch (Exception exception)
            {
                refused = exception;
            }

            // Assert — the exception type rides along so a failure says whether the sheet was absent or
            // whether resolving it broke some other way.
            Assert.That((refused?.GetType(), sheet != null), Is.EqualTo(((Type)null, true)));
        }
    }
}
