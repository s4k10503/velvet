#if UNITY_EDITOR
using UnityEditor;

namespace Velvet.EditorTools
{
    /// <summary>
    /// Imports the Visual Effect Graph package's textures without Crunch compression. The player
    /// script-compilation jobs have twice crashed the editor inside the Crunch compressor while importing one
    /// of the package's template thumbnails into a cold <c>Library</c>, before any build started, and those
    /// thumbnails were the only textures a passing macOS job's import crunched.
    /// </summary>
    internal sealed class VisualEffectGraphTextureImport : AssetPostprocessor
    {
        private const string PackageRoot = "Packages/com.unity.visualeffectgraph/";

        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(PackageRoot, System.StringComparison.Ordinal)
                && assetImporter is TextureImporter importer)
            {
                importer.crunchedCompression = false;
            }
        }
    }
}
#endif
