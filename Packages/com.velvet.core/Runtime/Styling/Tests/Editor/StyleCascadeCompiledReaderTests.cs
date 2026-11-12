using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that StyleCascade compiles its background layer reader in the editor, which is what keeps BackgroundOf's
    /// reflective fallback to IL2CPP alone.
    /// </summary>
    internal sealed class StyleCascadeCompiledReaderTests
    {
        // GREEN_ON_BASE(characterization): the base compiles the background layer reader in the editor.
        [Test]
        public void Given_TheEditor_When_StyleCascadeLoads_Then_TheBackgroundLayerReaderIsCompiled()
        {
            // Arrange
            var field = typeof(StyleCascade).GetField("s_compiledLayer", BindingFlags.NonPublic | BindingFlags.Static);

            // Act
            var reader = field?.GetValue(null);

            // Assert
            Assert.That(reader, Is.Not.Null);
        }
    }
}
