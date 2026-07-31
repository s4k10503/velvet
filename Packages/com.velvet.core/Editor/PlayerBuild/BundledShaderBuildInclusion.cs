using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Velvet.Editor
{
    /// <summary>
    /// Adds the package's shaders to Graphics Settings' Always Included Shaders while a player build runs and
    /// takes them out again when it ends, so a consumer installs the package and does nothing else.
    /// </summary>
    /// <remarks>
    /// Writing the entries permanently was the alternative: it works, and it leaves the consumer a diff in a
    /// <c>ProjectSettings</c> file the package does not own. The mechanism and its cost are described in
    /// <c>Documentation~/player-builds.md</c>; <c>BundledShaderInclusionTests</c> pins it.
    /// </remarks>
    internal sealed class BundledShaderBuildInclusion : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string GraphicsSettingsAsset = "ProjectSettings/GraphicsSettings.asset";
        private const string AlwaysIncludedShaders = "m_AlwaysIncludedShaders";

        // What this build added, so the revert removes exactly that and leaves alone an entry the consumer had
        // listed themselves. It is a file rather than editor state because the injection is saved to disk and
        // an editor that dies mid-build would otherwise take with it the only record of what to undo.
        private const string RecordFile = "Library/VelvetBundledShaderInclusion.txt";

        // Set while the session that wrote the record is still alive. Without it the repair below could not
        // tell a record left by a dead session from one a mid-build domain reload just walked past, and
        // removing the entries mid-build would put the defect back.
        private const string LiveSessionKey = "Velvet.BundledShaderBuildInclusion.Live";

        public int callbackOrder => 0;

        // Reverting before injecting is what stops a leftover from being adopted: injection is additive, so an
        // entry already present is treated as the consumer's own and never removed again.
        public void OnPreprocessBuild(BuildReport report)
        {
            Revert();
            Inject();
        }

        public void OnPostprocessBuild(BuildReport report) => Revert();

        [InitializeOnLoadMethod]
        internal static void RevertWhatAnEndedSessionLeft()
        {
            if (SessionState.GetString(LiveSessionKey, string.Empty).Length == 0)
            {
                Revert();
            }
        }

        // Refusing before anything is written is the point: the injection is saved to disk, so a revert that
        // cannot write would leave the consumer holding the permanent diff this mechanism exists to avoid.
        private static void RequireWritableSettings()
        {
            var file = new FileInfo(GraphicsSettingsAsset);
            if (file.Exists && file.IsReadOnly)
            {
                throw new BuildFailedException(
                    $"{GraphicsSettingsAsset} is read-only, so Velvet cannot add its shaders to Always " +
                    "Included Shaders for this build and could not take them out again afterwards. Make the " +
                    "file writable — check it out of version control if that is what holds it — and build " +
                    "again.");
            }
        }

        internal static void Inject()
        {
            RequireWritableSettings();
            var settings = new SerializedObject(
                AssetDatabase.LoadAssetAtPath<GraphicsSettings>(GraphicsSettingsAsset));
            var included = settings.FindProperty(AlwaysIncludedShaders);
            var added = new List<string>();
            foreach (var name in VelvetShaders.Names)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    throw new BuildFailedException(
                        $"Velvet declares a shader named {name} that this project cannot resolve. The player " +
                        "would build without it and the paint behind it would draw nothing.");
                }
                if (IndexOf(included, shader) >= 0)
                {
                    continue;
                }
                included.InsertArrayElementAtIndex(included.arraySize);
                included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
                added.Add(name);
            }
            settings.ApplyModifiedProperties();
            File.WriteAllLines(RecordFile, added);
            SessionState.SetString(LiveSessionKey, "1");

            var unreached = Unreached();
            if (unreached.Length > 0)
            {
                throw new BuildFailedException(
                    $"Velvet could not add {string.Join(", ", unreached)} to Always Included Shaders in " +
                    $"{GraphicsSettingsAsset}. The player would build without them and every shader-backed " +
                    "paint would draw nothing.");
            }
        }

        internal static void Revert()
        {
            if (!File.Exists(RecordFile))
            {
                return;
            }
            var settings = new SerializedObject(
                AssetDatabase.LoadAssetAtPath<GraphicsSettings>(GraphicsSettingsAsset));
            var included = settings.FindProperty(AlwaysIncludedShaders);
            foreach (var name in File.ReadAllLines(RecordFile))
            {
                var shader = Shader.Find(name);
                var index = shader == null ? -1 : IndexOf(included, shader);
                if (index >= 0)
                {
                    included.GetArrayElementAtIndex(index).objectReferenceValue = null;
                    included.DeleteArrayElementAtIndex(index);
                }
            }
            settings.ApplyModifiedProperties();
            // The build writes project settings to disk while it runs, so undoing the injection in the
            // loaded object alone would leave the entries in the file the consumer sees.
            AssetDatabase.SaveAssets();
            File.Delete(RecordFile);
            SessionState.EraseString(LiveSessionKey);
        }

        /// <summary>The bundled shader names Always Included Shaders does not currently carry.</summary>
        internal static string[] Unreached()
        {
            var settings = new SerializedObject(
                AssetDatabase.LoadAssetAtPath<GraphicsSettings>(GraphicsSettingsAsset));
            var included = settings.FindProperty(AlwaysIncludedShaders);
            return VelvetShaders.Names
                .Where(name =>
                {
                    var shader = Shader.Find(name);
                    return shader == null || IndexOf(included, shader) < 0;
                })
                .ToArray();
        }

        private static int IndexOf(SerializedProperty array, UnityEngine.Object item)
        {
            for (var i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == item)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
