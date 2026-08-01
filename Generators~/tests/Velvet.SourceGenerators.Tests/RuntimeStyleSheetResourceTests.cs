using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Pins the two chains runtime code walks to reach the bundled utilities: the holder a build preloads for
    /// a player, the sheet the editor loads directly, and that both end at the same asset.
    /// </summary>
    /// <remarks>
    /// Every link fails silently on its own, and the player-side one fails only in a player. A constant naming
    /// an asset that is not there throws when something calls it, which no compile does; a holder whose
    /// reference was cleared still loads, still preloads and still publishes itself, so the sheet is simply
    /// absent and the utilities it declares resolve to nothing while the C#-realised families and
    /// arbitrary-value classes keep working — a partial styling bug in a build, and one the editor is
    /// insulated from because it falls back to the asset path. The license-free generator workflow fires on
    /// any change under Runtime/, so a break is caught there rather than only in a Unity run.
    /// </remarks>
    public sealed class RuntimeStyleSheetResourceTests
    {
        private static readonly Regex RuntimeAssetsPathPattern =
            new(@"RuntimeAssetsPath\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex StyleSheetAssetPathPattern =
            new(@"StyleSheetAssetPath\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        private static readonly Regex HolderReferencePattern =
            new(@"_styleUtilities:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]+)", RegexOptions.Compiled);

        private static readonly Regex MetaGuidPattern = new(@"^guid:\s*([0-9a-f]+)", RegexOptions.Multiline);

        [Fact]
        public void Given_TheRuntimeAssetsPathConstant_When_ResolvedFromTheProjectRoot_Then_TheHolderIsThere()
        {
            // Act
            var declared = DeclaredPath(RuntimeAssetsPathPattern);
            var holder = Path.Combine(SolutionPaths.ProjectRoot(), declared);

            // Assert — the constant is carried alongside the resolved location, so a failure says which of
            // the two moved rather than only that they disagree.
            Assert.Equal(
                ("Packages/com.velvet.core/Runtime/Assets/VelvetRuntimeAssets.asset", true),
                (declared, File.Exists(holder)));
        }

        [Fact]
        public void Given_TheStyleSheetAssetPathConstant_When_ResolvedFromTheProjectRoot_Then_TheSheetIsThere()
        {
            // Act
            var declared = DeclaredPath(StyleSheetAssetPathPattern);
            var sheet = Path.Combine(SolutionPaths.ProjectRoot(), declared);

            // Assert
            Assert.Equal(
                ("Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss", true),
                (declared, File.Exists(sheet)));
        }

        [Fact]
        public void Given_TheHolderAsset_When_ItsReferenceIsResolved_Then_ItIsTheSheetTheEditorLoads()
        {
            // Arrange — both sides are derived from the constants rather than written again here, so this
            // guard cannot pass against a pair the runtime does not actually load.
            var projectRoot = SolutionPaths.ProjectRoot();
            var holder = File.ReadAllText(Path.Combine(projectRoot, DeclaredPath(RuntimeAssetsPathPattern)));
            var meta = File.ReadAllText(
                Path.Combine(projectRoot, DeclaredPath(StyleSheetAssetPathPattern) + ".meta"));

            // Act
            var reference = HolderReferencePattern.Match(holder);
            var sheetGuid = MetaGuidPattern.Match(meta).Groups[1].Value;

            // Assert — the object id takes part only to separate a live reference from a cleared one. Which
            // object inside the file it names is not decided here; BundledStyleSheetInclusionTests compares
            // the two loaded objects, which is the only place that can.
            Assert.Equal(
                (sheetGuid, true),
                (reference.Groups[2].Value, reference.Groups[1].Value != "0"));
        }

        private static string DeclaredPath(Regex pattern) =>
            pattern
                .Match(File.ReadAllText(
                    Path.Combine(SolutionPaths.RuntimeRoot(), "Styling", "VelvetStyleUtilities.cs")))
                .Groups[1].Value;
    }
}
