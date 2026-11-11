### Fixed

- A transition UI Toolkit runs on a function the element's classes give it now runs on that function from its first
  frame: Tailwind's curve for an `ease-in`, `ease-out` or `ease-in-out` class or the `ease-in-out` a `transition-*`
  utility bundles, or the function an `ease-[…]` class or a theme's `ease-<name>` names. The first frame was eased by
  UI Toolkit's own curve for the keyword and the function took over from the second: 0.15 s into a 0.3 s
  `transition-all`, a blur stood half way where Tailwind's curve puts it at about 78%, and a `steps(…, jump-start)`
  had not yet taken its first step.
