### Changed

- `@container` no longer re-points `sm:`…`2xl:`. Those prefixes measure the viewport (the panel root, or for a
  portal's children the panel the portal was declared on) whether or not an `@container` ancestor is around them, as
  in Tailwind v4. A component that answered the width of its `@container` through `md:` writes `@md:`-style container
  variants instead; the container scale starts lower than the viewport's, so `@3xl:` (768 px), `@5xl:` (1024 px)
  and `@7xl:` (1280 px) are the names that sit where `md:`, `lg:` and `xl:` did, and `@min-[640px]:` or
  `@min-[1536px]:` stands in for `sm:` and `2xl:`. The marker does nothing without a container variant beneath it.
- Tooling that applied `VelvetResponsive.ContainerClass` to simulate a viewport, as the preview window's Viewport
  addon did, applies the new `VelvetResponsive.ViewportClass` instead, which makes `sm:`…`2xl:` follow an element's
  width the way `@container` used to.
