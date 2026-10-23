using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Notices a text leaf's enableRichText changing, which raises no event of its own but marks the leaf for
    // repaint, so the generateVisualContent callback registered here runs. The shown string the resolver built
    // carries tags only while rich text is on, so a change has to build it again; the callback only asks for
    // that on a scheduler tick, since a leaf is not rewritten from inside its own mesh generation.
    //
    // One per leaf whose shown string depends on the flag, keyed in ReconcilerContext.TextRichTextWatches. Not
    // a pure side-table for the reason TextOverlineBindings is not: an entry owns a live delegate.
    internal sealed class TextRichTextWatch
    {
        private readonly ReconcilerContext _ctx;
        private readonly TextElement _element;
        private readonly Action<MeshGenerationContext> _onGenerate;
        private bool _pending;

        // The flag the shown string was last built under.
        public bool Applied { get; set; }

        private TextRichTextWatch(ReconcilerContext ctx, TextElement element)
        {
            _ctx = ctx;
            _element = element;
            Applied = element.enableRichText;
            _onGenerate = _ => OnGenerate();
        }

        public static void Sync(ReconcilerContext ctx, TextElement element, bool wanted)
        {
            ctx.TextRichTextWatches.TryGetValue(element, out var watch);
            if (wanted && watch == null)
            {
                watch = new TextRichTextWatch(ctx, element);
                ctx.TextRichTextWatches[element] = watch;
                element.generateVisualContent += watch._onGenerate;
            }
            else if (!wanted && watch != null)
            {
                Detach(ctx, element);
            }
            if (watch != null && wanted)
            {
                watch.Applied = element.enableRichText;
            }
        }

        public static void Detach(ReconcilerContext ctx, VisualElement element)
        {
            if (ctx.TextRichTextWatches.Remove(element, out var watch))
            {
                element.generateVisualContent -= watch._onGenerate;
            }
        }

        private void OnGenerate()
        {
            if (_pending || _element.enableRichText == Applied)
            {
                return;
            }
            _pending = true;
            _element.schedule.Execute(Rebuild);
        }

        private void Rebuild()
        {
            _pending = false;
            if (_ctx.TextRichTextWatches.ContainsKey(_element))
            {
                StyleTextEffectResolver.ReapplyElement(_ctx, _element);
            }
        }
    }
}
