using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The payloads written under one container query prefix on an element (`@md:p-4 @md:gap-2` is one binding of
    // two payloads), with each payload's rule place (see VariantDeclarations).
    internal sealed class ContainerBinding
    {
        public ContainerBinding(string spelling, StyleContainerQuery query, string[] payloads, int[] declarations)
        {
            Spelling = spelling;
            Query = query;
            Payloads = payloads;
            Declarations = declarations;
        }

        // The prefix as written. Two spellings of one threshold (`@md`, `@min-md`) are two bindings.
        public string Spelling { get; }
        public StyleContainerQuery Query { get; }
        public string[] Payloads { get; }
        public int[] Declarations { get; }
    }

    // Toggles container query variant payloads (@sm:/@md:/@max-lg:/@min-[475px]:/@md/main:…), driven by the width
    // of the container each one asks for: the nearest ancestor marked @container, or for a named query the
    // nearest marked @container/<name> (StyleResponsiveScope.ResolveContainer). A query with no container above
    // it matches nothing. Mirrors StyleConditionalVariantManipulator's lifecycle (tracked in
    // ReconcilerContext.ContainerVariantManipulators, removed on cleanup / dispose), except that it watches one
    // width source per container name rather than one for the element, and re-resolves each of them when a
    // reconcile toggles a container marker (StyleResponsiveScope.ScopesChanged).
    internal sealed class StyleContainerVariantManipulator : Manipulator
    {
        private readonly ReconcilerContext _ctx;
        private ContainerBinding[] _bindings;
        private bool[] _on;
        // Keyed by container name, "" for the nearest container of any name.
        private readonly Dictionary<string, ResponsiveWidthSource> _sources = new();
        // The owner a binding's stacked payloads (`@md:dark:p-2`) are keyed by, one per spelling and kept across
        // UpdateBindings: the stacked key carries the owner and a rank two spellings share, so a single owner
        // would make `@md:dark:p-2 @lg:dark:p-2` one stacked manipulator with one outer gate.
        private readonly Dictionary<string, object> _owners = new();

        public StyleContainerVariantManipulator(ReconcilerContext ctx, ContainerBinding[] bindings)
        {
            _ctx = ctx;
            _bindings = bindings;
            _on = new bool[bindings.Length];
        }

        public void UpdateBindings(ContainerBinding[] bindings)
        {
            ResetApplied();
            _bindings = bindings;
            if (_on.Length != bindings.Length)
            {
                _on = new bool[bindings.Length];
            }
            if (target?.panel != null)
            {
                Rebind();
                Evaluate();
            }
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            StyleResponsiveScope.ScopesChanged += OnScopesChanged;

            if (target.panel != null)
            {
                Rebind();
                Evaluate();
            }
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            ResetApplied();
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            StyleResponsiveScope.ScopesChanged -= OnScopesChanged;
            UnhookSources();
            foreach (var owner in _owners.Values)
            {
                _ctx.DropStackedVariants(owner);
            }
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            Rebind();
            Evaluate();
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            ResetApplied();
            UnhookSources();
        }

        private void OnScopesChanged()
        {
            if (target?.panel == null)
            {
                return;
            }
            Rebind();
            Evaluate();
        }

        // Points each name's source at the container it now resolves to.
        private void Rebind()
        {
            UnhookSources();
            foreach (var binding in _bindings)
            {
                var name = binding.Query.Name;
                if (!_sources.TryGetValue(name, out var source))
                {
                    source = new ResponsiveWidthSource(Evaluate);
                    _sources[name] = source;
                }
                source.Hook(StyleResponsiveScope.ResolveContainer(target, name));
            }
        }

        // The instances stay, unhooked: Rebind takes one up again for a name that comes back.
        private void UnhookSources()
        {
            foreach (var source in _sources.Values)
            {
                source.Unhook();
            }
        }

        private void Evaluate()
        {
            if (target == null)
            {
                return;
            }

            for (var i = 0; i < _bindings.Length; i++)
            {
                var query = _bindings[i].Query;
                var on = _sources.TryGetValue(query.Name, out var source) && source.Satisfies(query);
                if (on != _on[i])
                {
                    _on[i] = on;
                    ApplyPayloads(_bindings[i], on);
                }
            }
        }

        private void ResetApplied()
        {
            if (target == null)
            {
                return;
            }

            for (var i = 0; i < _on.Length; i++)
            {
                if (_on[i])
                {
                    _on[i] = false;
                    ApplyPayloads(_bindings[i], false);
                }
            }
        }

        // Layers by the query's rank: a floor over a ceiling, and a container query over any viewport breakpoint.
        private void ApplyPayloads(ContainerBinding binding, bool on)
            => StyleVariantPayload.Apply(target, binding.Payloads, on, binding.Query.Rank, _ctx,
                OwnerOf(binding.Spelling), binding.Declarations);

        private object OwnerOf(string spelling)
        {
            if (!_owners.TryGetValue(spelling, out var owner))
            {
                owner = new object();
                _owners[spelling] = owner;
            }
            return owner;
        }
    }
}
