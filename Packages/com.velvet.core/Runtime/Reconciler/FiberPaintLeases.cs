using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Who writes an element's paint layers while a Motion play runs. The class pass (skew, shadow, gradient)
    // and the z-layer patch write them from the node's class array; a play holds them for its whole length
    // instead, so the pass records the array it was given and the play draws the layers from the pose it
    // started on to the one that array names. Ownership is a count, the play's handles: the last release
    // gives the layers back by running the pass over the array it recorded, which keeps a layer attached only
    // where that array still names it.
    internal sealed class FiberPaintLeases
    {
        [Flags]
        internal enum Layer
        {
            None = 0,
            Skew = 1,
            Shadow = 2,
            Gradient = 4,
            Z = 8,
        }

        private readonly ReconcilerContext _ctx;
        private readonly FiberSkewApplier _skew;
        private readonly FiberGradientApplier _gradient;
        private readonly FiberBoxShadowApplier _shadow;
        private readonly Dictionary<VisualElement, Hold> _holds = new();

        // Whether the play being opened is followed by the class pass that names the pose it ends on. A play with
        // no such pass behind it has its poses to hand when it opens, and draws its start pose then.
        internal bool PassFollows { get; set; }

        public FiberPaintLeases(ReconcilerContext ctx, FiberSkewApplier skew, FiberGradientApplier gradient,
            FiberBoxShadowApplier shadow)
        {
            _ctx = ctx;
            _skew = skew;
            _gradient = gradient;
            _shadow = shadow;
        }

        internal static Layer Named(string[] classNames)
        {
            var named = Layer.None;
            if (StyleSkewClass.TryGetWinningSkewClasses(classNames, out _, out _)) named |= Layer.Skew;
            if (StyleShadowClass.HasShadowClass(classNames)) named |= Layer.Shadow;
            if (StyleGradientClass.HasGradientClass(classNames)) named |= Layer.Gradient;
            if (StyleZIndexClass.HasZIndexClass(classNames)) named |= Layer.Z;
            return named;
        }

        // Null when neither pose of the play names a paint layer, which leaves the class pass in charge.
        internal IMotionPaintLease? Open(VisualElement element, string[] fromClasses, string[] toClasses, bool isExit)
        {
            var layers = Named(fromClasses) | Named(toClasses);
            if (layers == Layer.None)
            {
                return null;
            }
            if (!_holds.TryGetValue(element, out var hold))
            {
                hold = new Hold(this, element);
                _holds[element] = hold;
            }
            return hold.Open(layers, fromClasses, toClasses, isExit);
        }

        internal bool Holds(VisualElement element, Layer layer)
            => _holds.Count != 0 && _holds.TryGetValue(element, out var hold) && (hold.Layers & layer) != 0;

        // The class array a pass was given for an element whose layers are held, which is the pose the play ends on.
        internal void Note(VisualElement element, string[] classNames)
        {
            if (_holds.Count != 0 && _holds.TryGetValue(element, out var hold))
            {
                hold.Note(classNames);
            }
        }

        // The angles the element's transform shears by now.
        private static SkewSpec ShownSkew(VisualElement element)
            => AuthoredTransform.TryGetSkew(element, out var x, out var y) ? new SkewSpec(x, y, null, null) : default;

        // The z a patch resolved while the play held the stack position, applied when the play lets go.
        internal void NoteZ(VisualElement element, bool stacked, int resolvedZ)
        {
            if (_holds.TryGetValue(element, out var hold))
            {
                hold.NoteZ(stacked, resolvedZ);
            }
        }

        private sealed class Hold
        {
            private readonly FiberPaintLeases _owner;
            private readonly VisualElement _element;
            private int _handles;
            private Handle? _current;
            private string[] _fromClasses = Array.Empty<string>();
            private string[] _toClasses = Array.Empty<string>();
            private bool _isExit;
            private string[]? _target;
            private bool _resolved;
            private bool _hasZ;
            private bool _zStacked;
            private int _z;

            private SkewSpec _skewFrom;
            private SkewSpec _skewTo;
            private ShadowSpec[] _shadowFrom = Array.Empty<ShadowSpec>();
            private ShadowSpec[] _shadowTo = Array.Empty<ShadowSpec>();
            private GradientSpec? _gradientFrom;
            private GradientSpec? _gradientTo;

            // What the layers show, which a play that takes the hold over starts from.
            private SkewSpec _skewShown;
            private ShadowSpec[] _shadowShown = Array.Empty<ShadowSpec>();
            private GradientSpec? _gradientShown;
            private bool _drawn;

            public Layer Layers;

            public Hold(FiberPaintLeases owner, VisualElement element)
            {
                _owner = owner;
                _element = element;
            }

            public IMotionPaintLease Open(Layer layers, string[] fromClasses, string[] toClasses, bool isExit)
            {
                if (_handles == 0 || !_drawn)
                {
                    TakeLive();
                }
                else
                {
                    _skewFrom = _skewShown;
                    _shadowFrom = _shadowShown;
                    _gradientFrom = _gradientShown;
                }
                Layers |= layers;
                _fromClasses = fromClasses;
                _toClasses = toClasses;
                _isExit = isExit;
                _target = null;
                _resolved = false;
                _handles++;
                _current = new Handle(this);
                if (!_owner.PassFollows)
                {
                    _current.Started = true;
                    Write(0f);
                }
                return _current;
            }

            public void Note(string[] classNames)
            {
                _target = classNames;
                _resolved = false;
            }

            public void NoteZ(bool stacked, int resolvedZ)
            {
                _hasZ = true;
                _zStacked = stacked;
                _z = resolvedZ;
            }

            private void TakeLive()
            {
                _skewFrom = ShownSkew(_element);
                _shadowFrom = _owner._ctx.ShadowBindings.TryGetValue(_element, out var shadow)
                    ? shadow.Layers : Array.Empty<ShadowSpec>();
                _gradientFrom = LiveGradient();
                _skewShown = _skewFrom;
                _shadowShown = _shadowFrom;
                _gradientShown = _gradientFrom;
                _drawn = false;
                Layers = Layer.None;
                _hasZ = false;
            }

            private GradientSpec? LiveGradient()
            {
                return _owner._ctx.GradientBackgrounds.TryGetValue(_element, out var gradient) ? gradient.Spec : null;
            }

            // The poses are fixed by the first draw rather than at open, since the pass that names the pose a play
            // ends on runs after the play starts.
            private void Resolve()
            {
                _resolved = true;
                if (_target != null)
                {
                    _skewTo = SkewOf(_target) ?? default;
                    _shadowTo = ShadowOf(_target) ?? Array.Empty<ShadowSpec>();
                    _gradientTo = GradientOf(_target, _element);
                    Attach(_target);
                    return;
                }
                // No pass named the end pose, so the layers as found show the pose of the side the play started
                // on without a patch: an exit's start, an enter's end. The other side is the class list that
                // names it, for each layer it names.
                if (_isExit)
                {
                    ResolveFromClasses(_element, _toClasses, ref _skewTo, ref _shadowTo, ref _gradientTo);
                    return;
                }
                _skewTo = _skewFrom;
                _shadowTo = _shadowFrom;
                _gradientTo = _gradientFrom;
                ResolveFromClasses(_element, _fromClasses, ref _skewFrom, ref _shadowFrom, ref _gradientFrom);
            }

            private static void ResolveFromClasses(VisualElement element, string[] classNames, ref SkewSpec skew, ref ShadowSpec[] shadow,
                ref GradientSpec? gradient)
            {
                var names = Named(classNames);
                if ((names & Layer.Skew) != 0) skew = SkewOf(classNames) ?? default;
                if ((names & Layer.Shadow) != 0) shadow = ShadowOf(classNames) ?? Array.Empty<ShadowSpec>();
                if ((names & Layer.Gradient) != 0) gradient = GradientOf(classNames, element);
            }

            private static SkewSpec? SkewOf(string[] classNames)
                => StyleSkewClass.TryExtract(classNames, out var spec) ? spec : null;

            private static ShadowSpec[]? ShadowOf(string[] classNames)
                => StyleShadowClass.HasShadowClass(classNames) && StyleShadowClass.TryExtract(classNames, out var spec)
                    ? spec : null;

            private static GradientSpec? GradientOf(string[] classNames, VisualElement element)
                => StyleGradientClass.HasGradientClass(classNames)
                    && StyleGradientClass.TryExtract(classNames, element, out var spec, out _)
                    ? spec : null;

            // A layer the end pose names and the element has not yet gets attached through the reconciler's own
            // create path, on the end pose's classes, and is then drawn from the start pose.
            private void Attach(string[] classNames)
            {
                var ctx = _owner._ctx;
                if ((Layers & Layer.Skew) != 0 && !ctx.SkewTransformed.ContainsKey(_element) && _skewTo.Active)
                {
                    _owner._skew.ApplySkewOnCreate(_element, classNames);
                }
                if ((Layers & Layer.Gradient) != 0 && !ctx.GradientBackgrounds.ContainsKey(_element) && _gradientTo != null)
                {
                    _owner._gradient.ApplyGradientOnCreate(_element, classNames);
                }
                if ((Layers & Layer.Shadow) != 0 && !ctx.ShadowBindings.ContainsKey(_element) && _shadowTo.Length != 0)
                {
                    _owner._shadow.ApplyShadowOnCreate(_element, classNames);
                }
            }

            public void Draw(Handle handle, float progress)
            {
                if (handle.Released || !ReferenceEquals(handle, _current))
                {
                    return;
                }
                // The play's first draw is the one that starts it, ahead of the pass that names the pose it ends
                // on, so the layers stay as found until the next.
                if (!handle.Started)
                {
                    handle.Started = true;
                    return;
                }
                Write(progress);
            }

            private void Write(float progress)
            {
                if (!_resolved)
                {
                    Resolve();
                }
                var ctx = _owner._ctx;
                var skew = (Layers & Layer.Skew) != 0
                    ? MotionPaintInterpolation.Skew(_skewFrom, _skewTo, progress) : ShownSkew(_element);
                if ((Layers & Layer.Skew) != 0 && ctx.SkewTransformed.ContainsKey(_element))
                {
                    AuthoredTransform.Engage(_element, skew.XDeg, skew.YDeg);
                }
                _skewShown = skew;
                if ((Layers & Layer.Gradient) != 0)
                {
                    DrawGradient(progress);
                }
                if ((Layers & Layer.Shadow) != 0)
                {
                    DrawShadow(progress);
                }
                _drawn = true;
            }

            private void DrawGradient(float progress)
            {
                if (_gradientFrom is not { } from || _gradientTo is not { } to)
                {
                    var shown = progress < 0.5f ? _gradientFrom : _gradientTo;
                    SetGradient(shown);
                    _gradientShown = shown;
                    return;
                }
                var blended = MotionPaintInterpolation.Gradient(from, to, progress);
                SetGradient(blended);
                _gradientShown = blended;
            }

            private void SetGradient(GradientSpec? spec)
            {
                var ctx = _owner._ctx;
                var bound = ctx.GradientBackgrounds.TryGetValue(_element, out var current);
                if (spec is not { } value)
                {
                    if (bound)
                    {
                        GradientBackground.Clear(_element);
                        GradientBackground.Detach(_element, current!);
                        ctx.GradientBackgrounds.Remove(_element);
                    }
                }
                else if (!bound)
                {
                    ctx.GradientBackgrounds[_element] = GradientBackground.Bind(_element, value);
                }
                else if (!current!.Spec.Equals(value))
                {
                    GradientBackground.RebindOwned(_element, current, value);
                }
            }

            private void DrawShadow(float progress)
            {
                var ctx = _owner._ctx;
                if (!ctx.ShadowBindings.TryGetValue(_element, out var binding))
                {
                    return;
                }
                var layers = MotionPaintInterpolation.Shadow(_shadowFrom, _shadowTo, progress);
                _shadowShown = layers;
                BoxShadowSilhouette.Sync(_element, binding, new BoxShadowSyncRequest
                {
                    Layers = layers,
                    ClassNames = binding.ClassNames ?? Array.Empty<string>(),
                });
            }

            public void Release(Handle handle)
            {
                if (handle.Released)
                {
                    return;
                }
                handle.Released = true;
                if (--_handles > 0)
                {
                    return;
                }
                _owner._holds.Remove(_element);
                if (_owner._ctx.GradientBackgrounds.TryGetValue(_element, out var drawn))
                {
                    GradientBackground.EndOwned(_element, drawn);
                }
                if (_element.parent == null)
                {
                    return;
                }
                if (_target != null)
                {
                    _owner._gradient.ApplyGradientOnPatch(_element, _target);
                    _owner._skew.ApplySkewOnPatch(_element, _target, classesChanged: true);
                    _owner._shadow.ApplyShadowOnPatch(_element, _target, clipActive: false);
                }
                if (_hasZ)
                {
                    FiberZLayerCoordinator.Restack(_owner._ctx, _element, _zStacked ? _z : null);
                }
            }
        }

        private sealed class Handle : IMotionPaintLease
        {
            private readonly Hold _hold;

            public bool Released;
            public bool Started;

            public Handle(Hold hold) => _hold = hold;

            public void Apply(float progress) => _hold.Draw(this, progress);

            public void Release() => _hold.Release(this);
        }
    }
}
