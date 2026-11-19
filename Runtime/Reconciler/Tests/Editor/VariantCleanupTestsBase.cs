using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Shared harness for the dark-mode / variant cleanup fixtures (variant-manipulator, stacked-variant, and
    /// pooled element/widget reuse). They all drive the same mode-store mount pattern — a <see cref="ModeStore"/>
    /// flipping a single <c>Mode</c> int to mount, then remove, the element under test — and the dark-mode cases
    /// additionally poke the process-wide static <see cref="VelvetTheme.DarkModeChanged"/> event. This base owns
    /// that plumbing once: the mode store, the static render hooks, the root element, and (critically) a guaranteed
    /// save/restore of <see cref="VelvetTheme.IsDark"/> around every test so a dark flip cannot leak into the next
    /// fixture. Subclasses only declare their own component shape + the specific assertions.
    /// </summary>
    /// <remarks>
    /// <see cref="s_store"/> / <see cref="s_render"/> are static because the components that read them are
    /// <c>[Component] static</c> methods; the base <see cref="SetUp"/> nulls them before every test so a previous
    /// test cannot pollute a later one whether run solo or in sequence.
    /// </remarks>
    internal abstract class VariantCleanupTestsBase
    {
        protected readonly record struct ModeState(int Mode);

        protected sealed class ModeStore : Store<ModeState>
        {
            public ModeStore() : base(new ModeState(0)) { }
            public void Set(int mode) => SetState(_ => new ModeState(mode));
            protected override void ResetCore() => SetState(_ => new ModeState(0));
        }

        protected static ModeStore s_store;
        protected static Func<int, VNode> s_render;
        protected VisualElement _root;
        private bool _darkBefore;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_render = null;
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        [TearDown]
        public void TearDown()
        {
            VelvetTheme.IsDark = _darkBefore;
        }

        // Mode 0 renders the variant-bearing subtree; mode 1 renders nothing (the leaf is removed → cleaned up).
        [Component]
        protected static VNode VariantHost()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            return V.Div(name: "host", children: new VNode[]
            {
                mode == 0 ? s_render(mode) : V.Fragment(Array.Empty<VNode>()),
            });
        }

        protected MountedTree MountHost(Func<int, VNode> render, out FiberBatchScheduler scheduler, out ReconcilerContext ctx)
        {
            s_render = render;
            var store = new ModeStore();
            s_store = store;
            var mounted = V.Mount(_root, V.Component(VariantHost, key: "host"));
            scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            ctx = mounted.Root.Reconciler.Context;
            return mounted;
        }

        /// <summary>
        /// Reads the live subscriber count of <see cref="VelvetTheme.DarkModeChanged"/>. A field-like event compiles
        /// to a private static backing delegate field of the same name; counting its invocation list is the only way
        /// to observe a subscription leak (a public event exposes no read access). Internal API, hence reflection.
        /// </summary>
        protected static int DarkModeChangedSubscriberCount()
        {
            var field = typeof(VelvetTheme).GetField("DarkModeChanged", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "Could not find VelvetTheme.DarkModeChanged's backing field. The event declaration may have changed.");
            return field.GetValue(null) is Action handler ? handler.GetInvocationList().Length : 0;
        }
    }
}
