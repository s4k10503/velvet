using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Notices what the string a text leaf shows depends on changing without an event of the leaf's own: its
    // enableRichText, which the tags are written under, and whether the engine elides its text from the start
    // or the middle, which it does to the text rather than to the string shown (StyleTextEffectResolver.ShowText).
    // Both mark the leaf for repaint, so the generateVisualContent callback registered here runs; it only asks
    // for the string to be built again, on a scheduler tick, since a leaf is not rewritten from inside its own
    // mesh generation.
    //
    // One per leaf whose shown string differs from its text or carries a tag, keyed in
    // ReconcilerContext.TextShownWatches. Not a pure side-table for the reason TextOverlineBindings is not: an
    // entry owns a live delegate.
    internal sealed class TextShownWatch
    {
        private readonly ReconcilerContext _ctx;
        private readonly TextElement _element;
        private readonly Action<MeshGenerationContext> _onGenerate;
        private bool _pending;

        private bool _richText;
        private bool _elidesFromStart;

        private TextShownWatch(ReconcilerContext ctx, TextElement element)
        {
            _ctx = ctx;
            _element = element;
            _onGenerate = _ => OnGenerate();
        }

        public static void Sync(ReconcilerContext ctx, TextElement element, bool wanted)
        {
            ctx.TextShownWatches.TryGetValue(element, out var watch);
            if (!wanted)
            {
                if (watch != null)
                {
                    Detach(ctx, element);
                }
                return;
            }
            if (watch == null)
            {
                watch = new TextShownWatch(ctx, element);
                ctx.TextShownWatches[element] = watch;
                element.generateVisualContent += watch._onGenerate;
            }
            watch.Record();
        }

        public static void Detach(ReconcilerContext ctx, VisualElement element)
        {
            if (ctx.TextShownWatches.Remove(element, out var watch))
            {
                element.generateVisualContent -= watch._onGenerate;
            }
        }

        // What the shown string was last built under.
        private void Record()
        {
            _richText = _element.enableRichText;
            _elidesFromStart = StyleTextEffectResolver.ElidesFromStart(_element);
        }

        private void OnGenerate()
        {
            if (_pending
                || (_element.enableRichText == _richText
                    && StyleTextEffectResolver.ElidesFromStart(_element) == _elidesFromStart))
            {
                return;
            }
            _pending = true;
            _element.schedule.Execute(Rebuild);
        }

        private void Rebuild()
        {
            _pending = false;
            if (_ctx.TextShownWatches.ContainsKey(_element))
            {
                StyleTextEffectResolver.ReapplyElement(_ctx, _element);
            }
        }
    }
}
