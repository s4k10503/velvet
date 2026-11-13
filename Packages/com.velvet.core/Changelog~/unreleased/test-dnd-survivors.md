### Fixed

- A render that changes an element's `bg-[…]` or `border-[…]` colour and removes its box shadow no longer puts the
  colour it had before back over the new one. The same holds for a render that changes a `border-[…]` colour and
  removes a dashed or dotted border.
- A colour written through a ref over the background or border of an element with a box shadow, or over the border
  of an element with a dashed or dotted border, now stays when a later render removes the shadow or the border
  style, or when the shadowed element, its border solid, starts clipping its overflow. It used to revert to the
  colour from before the write.
- An inline `border-[…]` colour on an element with a box shadow stays the element's own after a variant such as
  `dark:` changes its background. A border colour from a class rule no longer paints in its place, and the colour
  is no longer dropped when the shadow is removed.
- A dashed or dotted border whose `border-[…]` colour was removed by the render that added a box shadow no longer
  takes that colour back when a later render removes the shadow.
