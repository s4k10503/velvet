#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Mounts each story with its default args, the way Storybook's test runner smoke-tests a story that has no
    /// play function. A story fails when building or mounting it throws, or when an exception no error boundary
    /// in it catches is logged while it mounts and its pending effects run. An error a boundary catches does not
    /// fail it.
    /// </summary>
    public static class VelvetPreviewSmokeTest
    {
        /// <summary>Smoke-tests every story <see cref="VelvetPreviewRegistry.DiscoverStories"/> finds.</summary>
        /// <exception cref="InvalidOperationException">Two stories share a <c>Group/Name</c> id.</exception>
        public static IReadOnlyList<VelvetPreviewSmokeResult> Run() => Run(VelvetPreviewRegistry.DiscoverStories());

        /// <summary>Smoke-tests <paramref name="stories"/> in order, one result each.</summary>
        public static IReadOnlyList<VelvetPreviewSmokeResult> Run(IEnumerable<VelvetPreviewStory> stories)
        {
            var results = new List<VelvetPreviewSmokeResult>();
            foreach (var story in stories) results.Add(new VelvetPreviewSmokeResult(story, Mount(story)));
            return results;
        }

        private static string? Mount(VelvetPreviewStory story)
        {
            var catching = false;
            string? uncaught = null;

            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Exception && !catching) uncaught ??= condition;
            }

            // A boundary's catch is still logged the way MountOptions' default logs it; only the flag around it
            // keeps that log from reading as uncaught.
            var options = new MountOptions((exception, info) =>
            {
                catching = true;
                try
                {
                    FiberErrorBoundary.LogCaughtError(exception, info);
                }
                finally
                {
                    catching = false;
                }
            });

            Application.logMessageReceived += OnLog;
            try
            {
                using var panel = new SmokePanel();
                using var host = new VelvetPreviewHost(panel.Root, options);
                host.Mount(story);
                if (host.MountError != null) return Describe(host.MountError);
                host.Settle();
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            return uncaught;
        }

        private static string Describe(Exception exception) => exception.GetType().Name + ": " + exception.Message;

        // A runtime panel, the kind the game renders through, so attach-time and panel-reading code runs as it
        // would there. It renders into a texture rather than a display, and leaves nothing behind in the scene or
        // the project.
        private sealed class SmokePanel : IDisposable
        {
            private const int Width = 1280;
            private const int Height = 720;

            private readonly GameObject _host;
            private readonly PanelSettings _settings;
            private readonly ThemeStyleSheet _theme;
            private readonly RenderTexture _texture;

            public SmokePanel()
            {
                _texture = new RenderTexture(Width, Height, 0) { hideFlags = HideFlags.HideAndDontSave };
                _theme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                _theme.hideFlags = HideFlags.HideAndDontSave;
                _settings = ScriptableObject.CreateInstance<PanelSettings>();
                _settings.hideFlags = HideFlags.HideAndDontSave;
                _settings.themeStyleSheet = _theme;
                _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                _settings.targetTexture = _texture;
                _host = new GameObject("VelvetPreviewSmokeTest") { hideFlags = HideFlags.HideAndDontSave };
                var document = _host.AddComponent<UIDocument>();
                document.panelSettings = _settings;
                Root = document.rootVisualElement;
            }

            public VisualElement Root { get; }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_host);
                UnityEngine.Object.DestroyImmediate(_settings);
                UnityEngine.Object.DestroyImmediate(_theme);
                UnityEngine.Object.DestroyImmediate(_texture);
            }
        }
    }

    /// <summary>One story's outcome in a <see cref="VelvetPreviewSmokeTest"/> run.</summary>
    public sealed class VelvetPreviewSmokeResult
    {
        internal VelvetPreviewSmokeResult(VelvetPreviewStory story, string? failure)
        {
            Story = story;
            Failure = failure;
        }

        /// <summary>The story that was mounted.</summary>
        public VelvetPreviewStory Story { get; }

        /// <summary>Why the story failed, or <c>null</c> when it passed.</summary>
        public string? Failure { get; }
    }
}
#endif
