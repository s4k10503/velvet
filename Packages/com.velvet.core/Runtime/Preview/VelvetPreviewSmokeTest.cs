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
            if (stories == null) throw new ArgumentNullException(nameof(stories));
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

            var target = new VisualElement();
            VelvetStyleUtilities.AttachTo(target);
            Application.logMessageReceived += OnLog;
            try
            {
                using var host = new VelvetPreviewHost(target, options);
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
