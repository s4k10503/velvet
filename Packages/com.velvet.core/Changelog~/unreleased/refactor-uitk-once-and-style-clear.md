### Fixed

- An element that was given a class of the application's own before it was in a panel, and was recycled before it
  attached, took that class up on the attach its next use gave it and read the class's rules for a probe nobody still
  needed. A recycled element now forgets what it deferred.
