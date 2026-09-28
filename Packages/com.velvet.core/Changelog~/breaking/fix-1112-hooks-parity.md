### Changed

- `Hooks.UseAnimationSequence` takes its dependency list as a required second parameter, `deps`, ahead of
  `autoplay` and `loop`, and reads it as the React migration guide's dependency-list section describes:
  null restarts the sequence on every render and an empty array plays it once per mount. The list used to
  be an optional last parameter whose omission, or null, played the sequence once per mount. A caller that
  left it out, or passed null, passes `Array.Empty<object>()` to keep that behaviour.
