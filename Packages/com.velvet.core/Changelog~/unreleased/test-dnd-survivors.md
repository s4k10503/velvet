### Fixed

- A background or border colour written through a ref on an element with a box shadow, or with a dashed or dotted
  border, is no longer replaced by the colour captured before it when the element unmounts.
- An inline border colour (`border-[…]`) on an element with a box shadow is no longer dropped when the element
  unmounts after a variant such as `dark:` changed its background.
