### Changed

- `pointer-events-none` / `pointer-events-auto` already present in markup now change hit testing. Both
  classes were accepted and did nothing before, so markup that already carries them — Tailwind markup
  ported as written, such as `disabled:pointer-events-none` on a button — now lets the pointer through
  the element and its subtree, or takes a descendant back.
