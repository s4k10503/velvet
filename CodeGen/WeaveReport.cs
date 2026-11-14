using System.Collections.Generic;
using System.Text;

namespace Velvet.CodeGen
{
    // What the weaver did with one [Component] body and what it left alone, so that a component cached in part is
    // as inspectable as one cached whole. Every entry names a thing that is not cached and the reason.
    internal sealed class WeaveReport
    {
        private readonly List<string> _notes = new();

        public IReadOnlyList<string> Notes => _notes;

        // Whether the body as a whole is keyed on its props and hook values.
        public bool BodyGate { get; private set; }

        // How many parts of the body are cached on their own, apart from the body as a whole.
        public int Scopes { get; private set; }

        public void Add(string note) => _notes.Add(note);

        public void AddRange(IEnumerable<string> notes) => _notes.AddRange(notes);

        public void Woven(bool bodyGate, int scopes)
        {
            BodyGate = bodyGate;
            Scopes = scopes;
        }

        // The notes as the single string the attribute carries.
        public string Joined()
        {
            var joined = new StringBuilder();
            foreach (var note in _notes)
            {
                if (joined.Length > 0) joined.Append('\n');
                joined.Append(note);
            }
            return joined.ToString();
        }
    }
}
