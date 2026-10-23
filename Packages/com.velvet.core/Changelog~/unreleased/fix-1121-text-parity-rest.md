### Added

- `antialiased` and `subpixel-antialiased`, which write `-unity-editor-text-rendering-mode` (`sdf` and
  `bitmap`).
- `leading-[…]` reads a viewport unit (`leading-[10vw]`) and a math function mixing an em or a percentage
  with another length (`leading-[calc(1em+4px)]`), measured on the element that declares it and measured
  again when its font size or the panel changes. These used to leave the line height unset.

### Fixed

- Turning `enableRichText` on or off on a text leaf builds the string it shows again at its next repaint,
  where the change was read only at the next time the leaf's text effects resolved.
