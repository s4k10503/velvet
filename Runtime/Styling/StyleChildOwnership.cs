using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The claim a per-child style manipulator writes before it may take a value back off a child. Each of
    // these manipulators tracks the children it wrote to by raw reference and resets the value on one that
    // is no longer in its container. The element pool hands a child straight from one container to
    // another, and a container re-applies on the panel's schedule as well as the reconciler's, so the
    // container a child left can still be tracking it after the container it joined has written to it —
    // and would then reset that write.
    //
    // One table per property DOMAIN rather than one per manipulator: gap and grid both write a child's
    // margins, so a child moving between a gap container and a grid one has to be one owner's or the other's
    // and a per-manipulator table would let each still answer "mine". The tables live on ReconcilerContext
    // and are enrolled in its pure element side-tables, so a claim's teardown is the plain Remove.
    internal static class StyleChildOwnership
    {
        public static void Claim(Dictionary<VisualElement, Manipulator> owners, VisualElement child,
            Manipulator owner)
            => owners[child] = owner;

        // True once the claim was this owner's and has been dropped — the caller may then reset what it
        // wrote. False leaves the child untouched: the value on it now belongs to somebody else, or to
        // nobody (the reconciler drops a removed element's claim with the rest of its side tables).
        public static bool TryRelease(Dictionary<VisualElement, Manipulator> owners, VisualElement child,
            Manipulator owner)
        {
            if (!owners.TryGetValue(child, out var held) || !ReferenceEquals(held, owner))
            {
                return false;
            }
            owners.Remove(child);
            return true;
        }
    }
}
