### Fixed

- Changing `maxLength:` on a `V.TextField` with `isDelayed: true` no longer discards the text the user has
  typed but not yet committed. The edit stays on screen, cut to the new limit, is not reported early, and
  still commits on Enter or blur.
