using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Compilation;
using UnityEngine;

namespace Velvet.Editor.PlayerBuild
{
    /// <summary>
    /// Compiles every runtime assembly the way a player does, which is the one compile the suite never
    /// performs.
    /// </summary>
    /// <remarks>
    /// EditMode and PlayMode both run inside the editor, where <c>UNITY_EDITOR</c> is defined, so a call
    /// to a member declared under <c>#if UNITY_EDITOR</c> from outside one compiles there and nowhere
    /// else. Measured on a branch carrying such a call: clean with the define, <c>CS0103</c> without it.
    /// What a consumer hits is a package that fails to build, with no workaround short of editing it.
    /// <para>
    /// Scripts rather than a whole player: the question is which defines the compile runs under, and a
    /// player build would answer it alongside a scene load, an asset import and a linker pass with
    /// failure modes of their own.
    /// </para>
    /// <para>
    /// Failure is read as an assembly that did not arrive rather than off a message list, because
    /// <see cref="ScriptCompilationResult"/> carries no messages: a compile error drops the assembly
    /// from the result, and the editor's own log carries the diagnostic. The expected set comes from
    /// <see cref="CompilationPipeline"/>, so a package assembly nobody built is named here rather than
    /// counted.
    /// </para>
    /// </remarks>
    public static class PlayerScriptCompilation
    {
        /// <summary>Exits non-zero when a player assembly did not come out, so batchmode reports it.</summary>
        public static void CompileForPlayer()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var output = Path.Combine(Path.GetTempPath(), "velvet-player-scripts");
            Directory.CreateDirectory(output);

            var wanted = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Select(assembly => Path.GetFileName(assembly.outputPath))
                .ToList();

            ScriptCompilationResult result;
            try
            {
                result = PlayerBuildInterface.CompilePlayerScripts(
                    new ScriptCompilationSettings { target = target, group = group }, output);
            }
            catch (Exception failure)
            {
                Debug.LogError($"Player script compilation did not run: {failure}");
                EditorApplication.Exit(1);
                return;
            }

            var built = new HashSet<string>(
                result.assemblies ?? (IEnumerable<string>)Array.Empty<string>(), StringComparer.Ordinal);
            var missing = wanted.Where(name => !built.Contains(name)).ToList();

            // An empty wanted list would leave nothing missing however the compile went, which reads as
            // success from the comparison alone.
            if (wanted.Count == 0)
            {
                Debug.LogError("No player assemblies were named, so this compared nothing.");
                EditorApplication.Exit(1);
                return;
            }

            if (missing.Count > 0)
            {
                Debug.LogError($"Player script compilation produced {built.Count} of {wanted.Count} "
                               + "assemblies. Missing: " + string.Join(", ", missing)
                               + "\nThe compiler's own diagnostics are above this line.");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"Player script compilation produced all {wanted.Count} assemblies for {target}.");
            EditorApplication.Exit(0);
        }
    }
}
