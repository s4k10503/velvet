### Fixed

- A named uniform scale such as `scale-50` now supplies the untouched axis beside `scale-x-*` or
  `scale-y-*`, and changing or removing that preset updates the composition. Uniform presets retain
  their stylesheet order and existing inline and important priority rules.
