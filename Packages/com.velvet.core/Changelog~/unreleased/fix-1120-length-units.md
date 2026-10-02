### Fixed

- An arbitrary length takes CSS's absolute units, `pt`, `pc`, `in`, `cm`, `mm` and `Q`, at 96px to the inch, and a
  unit in either case, as CSS reads `10PX`: `w-[1in]` is 96px wide and `blur-[3pt]` blurs by 4px. They were not
  recognized, and the class stayed inert.
