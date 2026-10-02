### Fixed

- A colour opacity modifier on `bg-`, `text-` and `border-` scales the colour's own alpha, as Tailwind mixes the
  colour with transparent: `bg-[#ff000080]/50` is a quarter opaque. The modifier replaced the alpha, so it was
  half opaque. An opaque colour, such as every palette colour, is unaffected.
