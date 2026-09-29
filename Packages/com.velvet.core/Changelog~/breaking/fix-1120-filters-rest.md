### Changed

- Under `transition-filter`, a filter change interpolates by the CSS filter-list rule: functions pair by
  position, and a filter added or removed at the end of the list fades. A filter added or removed
  anywhere else, such as a `blur-*` added to an element carrying `grayscale` or a `filter-[name]` custom, now
  applies at once, as it does in CSS and under `transition-all`. It faded in or out.
