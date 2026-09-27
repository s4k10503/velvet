### Fixed

- The package no longer uses two APIs that fail to compile from the Unity 6.5 beta on. On 6.5 and later
  it names `UnityEngine.UIElements.WorldSpaceSizeMode` where it named `UIDocument.WorldSpaceSizeMode`,
  and on every version it tells a swapped particle effect from the same one by `GetEntityId()` rather
  than by the instance ID 6.5 withdrew. CI now compiles the package, its editor and test assemblies
  included, on a later editor as well as on the declared floor.
