### Fixed

- Structural variants (`first:`, `last:`, `only:`, `odd:`, `even:`, `nth-N:`, `nth-last-N:`) take effect on children
  rendered directly into the element a tree is mounted into, and into a portal's target. They were not evaluated
  there, so `V.Mount(host, V.Div(className: "first:bg-red-500"))` left the div unstyled. They are also re-derived
  after a sibling is added or removed there, after a Portal unmounts and leaves other children on its target, for
  the children of a context-provider wrapper element, and when a time-sliced reconcile finishes. A component that
  re-renders alone and adds a row beside siblings it shares a parent with moves `last:` to the new row. The host's
  or target's other children count as siblings, as in CSS, including ones this tree did not render.
