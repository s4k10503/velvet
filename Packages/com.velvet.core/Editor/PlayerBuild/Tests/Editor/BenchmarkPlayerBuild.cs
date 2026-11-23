using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.TestTools;
using Velvet.Tests;

[assembly: TestPlayerBuildModifier(typeof(BenchmarkPlayerBuild))]

namespace Velvet.Tests
{
    /// <summary>
    /// Turns a test player build into the IL2CPP, full-count benchmark player that
    /// <c>.github/workflows/player-benchmarks.yml</c> measures, where <see cref="EnvironmentVariable"/> is
    /// set to 1 in the editor's environment and into nothing otherwise. The backend is set on the editor's
    /// in-memory settings for the build and nothing here saves it.
    /// </summary>
    internal sealed class BenchmarkPlayerBuild : ITestPlayerBuildModifier
    {
        internal const string EnvironmentVariable = "VELVET_PLAYER_BENCHMARKS";

        // A player is not handed the editor's command line, so the full-count flag that BenchmarkRun reads
        // there reaches it as this scripting define instead.
        internal const string FullCountsDefine = "VELVET_BENCHMARK_FULL";

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions playerOptions)
        {
            if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
                return playerOptions;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);
            playerOptions.extraScriptingDefines = (playerOptions.extraScriptingDefines ?? Array.Empty<string>())
                .Append(FullCountsDefine).ToArray();
            return playerOptions;
        }
    }
}
