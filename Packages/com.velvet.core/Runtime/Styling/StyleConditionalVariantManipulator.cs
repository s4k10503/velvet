using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Toggles "ambient condition" variant payloads that do not depend on element-local pointer/focus
    // state: responsive min-width variants (sm:/md:/lg:/xl:/2xl:), driven by the resolved responsive-scope
    // width, and dark:, driven by VelvetTheme.IsDark. Mirrors StyleVariantManipulator's lifecycle (tracked in
    // ReconcilerContext.ConditionalVariantManipulators, removed on cleanup / dispose). Responsive
    // breakpoints watch the width source resolved at attach (the nearest responsive-scope ancestor, else the
    // panel root) via a GeometryChangedEvent registered on that source, attached/detached with the element.
    // Payloads are USS classes or arbitrary values, the same as the other variant manipulators.
    internal sealed class StyleConditionalVariantManipulator : Manipulator
    {
        private static readonly StyleVariantKind[] Breakpoints =
        {
            StyleVariantKind.Sm, StyleVariantKind.Md, StyleVariantKind.Lg,
            StyleVariantKind.Xl, StyleVariantKind.Xxl,
        };

        // Payload arrays aligned to Breakpoints (length 5) plus the dark payloads.
        private string[][] _responsive;
        private string[] _dark;

        private readonly bool[] _bpOn = new bool[Breakpoints.Length];
        private bool _darkOn;
        private readonly ResponsiveWidthSource _widthSource;

        private readonly ReconcilerContext _ctx;

        public StyleConditionalVariantManipulator(ReconcilerContext ctx, string[][] responsive, string[] dark)
        {
            _ctx = ctx;
            _responsive = responsive ?? new string[Breakpoints.Length][];
            _dark = dark ?? Array.Empty<string>();
            _widthSource = new ResponsiveWidthSource(EvaluateResponsive);
        }

        public void UpdatePayloads(string[][] responsive, string[] dark)
        {
            ResetApplied();
            _responsive = responsive ?? new string[Breakpoints.Length][];
            _dark = dark ?? Array.Empty<string>();
            Evaluate();
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            VelvetTheme.DarkModeChanged += OnDarkChanged;

            if (target.panel != null)
            {
                _widthSource.Hook(StyleResponsiveScope.ResolveWidthSource(target, target.panel.visualTree));
                Evaluate();
            }
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            ResetApplied();
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            VelvetTheme.DarkModeChanged -= OnDarkChanged;
            _widthSource.Unhook();
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            _widthSource.Hook(StyleResponsiveScope.ResolveWidthSource(target, evt.destinationPanel?.visualTree));
            Evaluate();
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            ResetApplied();
            _widthSource.Unhook();
        }

        private void OnDarkChanged() => EvaluateDark();

        private void Evaluate()
        {
            EvaluateResponsive();
            EvaluateDark();
        }

        private void EvaluateResponsive()
        {
            if (target == null)
            {
                return;
            }

            var width = _widthSource.Width;
            for (var i = 0; i < Breakpoints.Length; i++)
            {
                var on = width >= StyleVariantClass.BreakpointPx(Breakpoints[i]);
                if (on != _bpOn[i])
                {
                    _bpOn[i] = on;
                    ApplyPayloads(_responsive[i], on);
                }
            }
        }

        private void EvaluateDark()
        {
            if (target == null)
            {
                return;
            }

            var on = VelvetTheme.IsDark;
            if (on != _darkOn)
            {
                _darkOn = on;
                ApplyPayloads(_dark, on);
            }
        }

        private void ResetApplied()
        {
            if (target == null)
            {
                return;
            }

            for (var i = 0; i < _bpOn.Length; i++)
            {
                if (_bpOn[i])
                {
                    _bpOn[i] = false;
                    ApplyPayloads(_responsive[i], false);
                }
            }

            if (_darkOn)
            {
                _darkOn = false;
                ApplyPayloads(_dark, false);
            }
        }

        private void ApplyPayloads(string[] payloads, bool on)
            => StyleVariantPayload.Apply(target, payloads, on, PriorityFor(payloads), _ctx, this);

        // Arbitrary-value layering priority: dark, or a responsive breakpoint (a larger min-width wins). Keyed
        // by reference to the payload array passed, so a higher breakpoint's arbitrary value layers over a lower
        // one and over the base, and dropping it falls back rather than wiping the property.
        private int PriorityFor(string[] payloads)
        {
            if (ReferenceEquals(payloads, _dark)) return StyleLayerPriority.Dark;
            for (var i = 0; i < _responsive.Length; i++)
            {
                if (ReferenceEquals(payloads, _responsive[i])) return StyleLayerPriority.ResponsiveSm + i;
            }
            return StyleLayerPriority.Base;
        }
    }
}
