### Changed

- The migration guide now states where an `absolute` element with no offset on an axis sits in a `flex` or
  `flex-col` container: at the static position css-flexbox gives a flex container's child, across the content
  edges and along the main axis as its sole flex item, so `justify-*` and `items-*` place it. A `grid`
  container is not claimed. Layout is unchanged.
