#if UNITY_EDITOR
#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Mounts a preview story and its assembly environment onto a target <see cref="VisualElement"/>.
    /// </summary>
    public sealed class VelvetPreviewHost : IDisposable
    {
        private readonly VisualElement _target;
        private IDisposable? _environment;
        private MountedTree? _mounted;
        private StyleSheet? _appliedStyleSheet;
        private bool _disposed;

        /// <summary>The currently mounted story, or <c>null</c> before a successful mount and after failure.</summary>
        public VelvetPreviewStory? Story { get; private set; }

        /// <summary>
        /// The exception raised by the latest mount or args update, or <c>null</c> when it succeeded. Lets a
        /// window surface a failing story without throwing out of its layout pass.
        /// </summary>
        public Exception? MountError { get; private set; }

        public VelvetPreviewHost(VisualElement target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>
        /// Tears down the previous story and environment, then mounts <paramref name="story"/> with its default
        /// args. Build and render failures are captured in <see cref="MountError"/> and leave
        /// <see cref="Story"/> null.
        /// </summary>
        public void Mount(VelvetPreviewStory story) => Mount(story, useArgs: false, args: null);

        /// <summary>
        /// Mounts <paramref name="story"/> with <paramref name="args"/>. Parameterless stories ignore the args.
        /// Failures are handled as in <see cref="Mount(VelvetPreviewStory)"/>.
        /// </summary>
        public void Mount(VelvetPreviewStory story, object args) => Mount(story, useArgs: true, args: args);

        /// <summary>
        /// Rebuilds the mounted story with new <paramref name="args"/> without restarting its environment or
        /// replacing its stylesheet. Returns <c>false</c> when no story can be updated. A failed update records
        /// <see cref="MountError"/> and returns <c>true</c> because the update path was taken.
        /// </summary>
        public bool UpdateArgs(object args)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VelvetPreviewHost));
            if (Story == null || _mounted == null) return false;

            var story = Story;
            try
            {
                _mounted.Dispose();
                _mounted = null;
                MountError = null;

                var tree = story.Build(args);
                if (tree == null)
                {
                    MountError = new InvalidOperationException("Story returned a null VNode.");
                    Story = null;
                    return true;
                }

                _mounted = V.Mount(_target, tree);
                Story = story;
            }
            catch (Exception ex)
            {
                MountError = ex;
                Story = null;
            }

            return true;
        }

        private void Mount(VelvetPreviewStory story, bool useArgs, object? args)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VelvetPreviewHost));

            Unmount();
            Story = null;
            MountError = null;
            if (story == null) return;

            try
            {
                _environment = VelvetPreviewRegistry.RunSetupFor(story.Assembly);
                ApplyStyleHint();
                var tree = useArgs ? story.Build(args) : story.Build();
                if (tree == null)
                {
                    MountError = new InvalidOperationException("Story returned a null VNode.");
                    Unmount();
                    return;
                }

                _mounted = V.Mount(_target, tree);
                // Publish Story only after V.Mount succeeds so failure remains distinguishable from a live mount.
                Story = story;
            }
            catch (Exception ex)
            {
                MountError = ex;
                Unmount();
            }
        }

        // Consume the static hint before attaching it so it cannot leak to a later host. Track only a sheet
        // this host added; Unmount must not remove a sheet the target already owned.
        private void ApplyStyleHint()
        {
            var sheet = VelvetStyleHints.PreviewStyleSheet;
            VelvetStyleHints.PreviewStyleSheet = null;
            if (sheet == null || _target.styleSheets.Contains(sheet)) return;
            _target.styleSheets.Add(sheet);
            _appliedStyleSheet = sheet;
        }

        private void Unmount()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_appliedStyleSheet != null)
            {
                _target.styleSheets.Remove(_appliedStyleSheet);
                _appliedStyleSheet = null;
            }

            _environment?.Dispose();
            _environment = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unmount();
            Story = null;
        }
    }
}
#endif
