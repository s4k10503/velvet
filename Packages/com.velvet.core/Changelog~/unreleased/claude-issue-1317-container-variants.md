### Added

- Tailwind v4's container query variants: `@container` (or `@container/name`) marks a query container, and
  `@3xs:` … `@7xl:`, `@min-*:`, `@max-*:` and `@min-[475px]:` / `@[475px]:` / `@max-[600px]:` apply by the width
  of the nearest one (its content width, padding and border excluded), `@md/name:` by the nearest one of that name. A query with no container above it
  matches nothing. They stack (`dark:@md:`, `@md:hover:`), rank after every viewport breakpoint
  and before `dark:`, and re-resolve when a render adds, removes or renames the marker on an ancestor.
  `StyleVariantKind.Container` is the kind the parser reports for them.
- `VelvetResponsive.ViewportClass`, the marker that makes `sm:`…`2xl:` measure an element's width in place of the
  panel root's, for tooling that simulates a viewport.

### Fixed

- Markup copied from Tailwind that writes `@md:` and the like resolves, where it used to leave an inert class on
  the element.
