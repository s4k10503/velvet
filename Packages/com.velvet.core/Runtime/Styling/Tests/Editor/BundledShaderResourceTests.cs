using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that every shader the package ships can be reached from a built player. A shader nothing
    /// references is not put into a build at all, so one added outside the <c>Resources</c> folder resolves
    /// in the editor and to null in a player, where its paint draws nothing and only a log warning says so.
    /// <para>
    /// Both cases enumerate the tree rather than a written list, and both fold the enumeration's size into
    /// the assertion: an empty walk would otherwise satisfy every "for each" below.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class BundledShaderResourceTests
    {
        private const string RuntimeRoot = "Packages/com.velvet.core/Runtime";
        private const string ResourcesRoot = RuntimeRoot + "/Resources";
        private const string ShaderRoot = ResourcesRoot + "/Velvet";

        private static readonly Regex DeclaredNamePattern =
            new(@"^\s*Shader\s+""([^""]+)""", RegexOptions.Multiline | RegexOptions.Compiled);

        private static string[] ShaderFilesUnder(string root)
            => Directory.Exists(root)
                ? Directory.GetFiles(root, "*.shader", SearchOption.AllDirectories)
                    .Select(p => p.Replace('\\', '/'))
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();

        [Test]
        public void Given_ThePackagesRuntimeTree_When_ShaderFilesAreEnumerated_Then_EveryOneSitsUnderResources()
        {
            // Arrange
            var all = ShaderFilesUnder(RuntimeRoot);

            // Act
            var unreachable = all.Where(p => !p.StartsWith(ShaderRoot + "/", StringComparison.Ordinal));

            // Assert
            Assert.That((all.Length > 0, string.Join(", ", unreachable)), Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_EachBundledShader_When_LoadedByItsResourcesPath_Then_ItResolvesUnderItsDeclaredName()
        {
            // Arrange — the Resources-relative path is the same string as the declared Shader name, which is
            // what lets one identifier be both what puts the shader in a build and what looks it up.
            var shipped = ShaderFilesUnder(ShaderRoot);
            var problems = new List<string>();

            // Act
            foreach (var path in shipped)
            {
                var resourcesPath = path.Substring(ResourcesRoot.Length + 1);
                resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - ".shader".Length);
                var match = DeclaredNamePattern.Match(File.ReadAllText(path));
                if (!match.Success || match.Groups[1].Value != resourcesPath)
                {
                    problems.Add($"{path} declares '{(match.Success ? match.Groups[1].Value : "<none>")}'"
                        + $" but loads as '{resourcesPath}'");
                    continue;
                }
                if (VelvetShaders.Find(resourcesPath) == null)
                {
                    problems.Add($"{resourcesPath} did not load");
                }
            }

            // Assert
            Assert.That((shipped.Length > 0, string.Join(", ", problems)), Is.EqualTo((true, string.Empty)));
        }
    }
}
