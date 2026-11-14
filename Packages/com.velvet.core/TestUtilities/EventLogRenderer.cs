using System;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// A renderer an <see cref="EventLog"/> fixture mounts its trees through, so that one case runs once per entry
    /// of <see cref="All"/>: the fixture declares <c>[TestFixtureSource(typeof(EventLogRenderer),
    /// nameof(EventLogRenderer.All))]</c> and takes the renderer it runs on in its constructor.
    /// </summary>
    public abstract class EventLogRenderer
    {
        /// <summary>The renderers the parameterised <see cref="EventLog"/> fixtures run on.</summary>
        public static readonly EventLogRenderer[] All = { new UIToolkitRenderer() };

        /// <summary>Mounts <paramref name="tree"/> into a container of its own.</summary>
        public abstract MountedEventLogTree Mount(VNode tree, MountOptions? options = null);

        /// <summary>A fresh container a <c>V.Portal</c> in a mounted tree can render into.</summary>
        public abstract VisualElement CreatePortalTarget();

        private sealed class UIToolkitRenderer : EventLogRenderer
        {
            public override MountedEventLogTree Mount(VNode tree, MountOptions? options = null)
                => new UIToolkitTree(options == null
                    ? V.Mount(new VisualElement(), tree)
                    : V.Mount(new VisualElement(), tree, options));

            public override VisualElement CreatePortalTarget() => new();

            public override string ToString() => "UIToolkit";
        }

        private sealed class UIToolkitTree : MountedEventLogTree
        {
            private readonly MountedTree _tree;

            internal UIToolkitTree(MountedTree tree) => _tree = tree;

            // Bypasses: what MountedTreeTestExtensions.DrainForTest bypasses, which it calls.
            public override void DrainForTest() => _tree.DrainForTest();

            public override void Dispose() => _tree.Dispose();
        }
    }

    /// <summary>A tree an <see cref="EventLogRenderer"/> mounted. Unmounts on Dispose.</summary>
    public abstract class MountedEventLogTree : IDisposable
    {
        /// <summary>
        /// Renders what is queued and runs the passive effects the commits left pending, as
        /// <see cref="MountedTreeTestExtensions.DrainForTest"/> does for a tree <c>V.Mount</c> returned.
        /// </summary>
        // Bypasses: what each renderer's drain bypasses; UIToolkitTree's is MountedTreeTestExtensions.DrainForTest's.
        public abstract void DrainForTest();

        /// <summary>Unmounts the tree.</summary>
        public abstract void Dispose();
    }
}
