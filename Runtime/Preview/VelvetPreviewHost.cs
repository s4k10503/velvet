#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Mounts a single preview story onto a target <see cref="VisualElement"/> and keeps it live: it opens the
    /// story's assembly environment, performs the initial <see cref="V.Mount"/>, and tears both down on
    /// <see cref="Dispose"/>. Holding the host separate from the window lets the headless capture path mount the
    /// exact same way without an <c>EditorWindow</c>.
    /// </summary>
    public sealed class VelvetPreviewHost : IDisposable
    {
        private readonly VisualElement _target;
        private IDisposable _environment;
        private MountedTree _mounted;
        private StyleSheet _appliedStyleSheet;
        private bool _disposed;

        /// <summary>The story currently mounted by this host, or <c>null</c> before the first mount and after a
        /// failed mount — so a caller polling this never repaints / re-mounts a broken story.</summary>
        public VelvetPreviewStory Story { get; private set; }

        /// <summary>
        /// The exception the last mount attempt raised (story build or initial render), or <c>null</c> when the
        /// last mount succeeded. Lets a window surface a failing story without throwing out of its layout pass.
        /// </summary>
        public Exception MountError { get; private set; }

        public VelvetPreviewHost(VisualElement target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>
        /// Tears down the previous story (if any) and mounts <paramref name="story"/> in its place at its default
        /// args, re-running the story's assembly environment so each mount starts from a clean store / font /
        /// resolver state. A build or render failure is captured into <see cref="MountError"/> rather than thrown,
        /// and leaves <see cref="Story"/> null (the mount did not take effect).
        /// </summary>
        public void Mount(VelvetPreviewStory story) => Mount(story, useArgs: false, args: null);

        /// <summary>
        /// Mounts <paramref name="story"/> driving an args-story with the supplied live <paramref name="args"/>
        /// instance (the preview controls call this on every edit). For a parameterless story
        /// <paramref name="args"/> is ignored. Same failure handling as <see cref="Mount(VelvetPreviewStory)"/>.
        /// </summary>
        public void Mount(VelvetPreviewStory story, object args) => Mount(story, useArgs: true, args: args);

        /// <summary>
        /// Re-renders the live story's TREE with new <paramref name="args"/> WITHOUT tearing down the assembly
        /// environment — so a controls knob edited per keystroke does not re-register fonts / re-seed the store /
        /// recreate the dummy API (and its cancellation source) each time. Only the VNode tree is rebuilt and
        /// re-mounted; the environment and any applied stylesheet stay open. Returns <c>false</c> when there is no
        /// successfully-mounted story to update (the caller should fall back to a full <see cref="Mount"/>); a
        /// build/render failure during the update is captured into <see cref="MountError"/> and returns true
        /// (the update path was taken).
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

        private void Mount(VelvetPreviewStory story, bool useArgs, object args)
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
                // Set only after the mount actually succeeds: a failed mount leaves Story null so the window
                // does not keep repainting / re-mounting a story that cannot render.
                Story = story;
            }
            catch (Exception ex)
            {
                MountError = ex;
                Unmount();
            }
        }

        // Attaches the stylesheet the active environment published, then consumes the hint (clears the static)
        // so it cannot leak onto a later, unrelated host. It is added after Velvet's utilities — which the
        // caller put on the canvas first — so later source order lets the app's :root overrides win. The host
        // remembers exactly the sheet it added so Unmount removes that one and not a sheet the caller owns.
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
