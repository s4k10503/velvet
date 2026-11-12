### Changed

- Reading a keyword variant class with no relational name (`hover:`, `sm:`, `dark:`, `group-hover:` and the like)
  no longer cuts its keyword out as a string, and asking only whether a class is one cuts out no payload either.
  Outside a theme scope, asking it of a class behind a plain word that is no variant, such as `first:` or
  `max-3xl:`, cuts out nothing.
- The clip-path, filter, filter-transition, animate-hue and border-inset reads pass over a class that names none of
  their utilities before peeling its variants, so they allocate nothing to peel such a class.
- An arbitrary value with no type hint, such as `w-[120px]`, no longer has its prefix cut out as a string when it is
  tried as a colour.
- Mounting a `V.Div`, `V.Label` or `V.Button` no longer allocates for the corner-radius fit, which watches such an
  element for a radius its stylesheet or code gives it: the element's type hands its size changes to the fit, where
  a callback was registered on each one before.
