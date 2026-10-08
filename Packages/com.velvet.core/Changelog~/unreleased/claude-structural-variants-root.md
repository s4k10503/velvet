### Fixed

- Structural variants (`first:`, `last:`, `only:`, `odd:`, `even:`, `nth-N:`, `nth-last-N:`) take effect on children
  rendered directly into the element a tree is mounted into, and into a portal's target. They were not evaluated
  there, so `V.Mount(host, V.Div(className: "first:bg-red-500"))` left the div unstyled. A component that renders
  siblings into a parent it shares with others and re-renders alone now moves `last:` to the new final child as
  well. The host's or target's other children count as siblings, as in CSS, including ones this tree did not render.
