### Changed

- A `TransitionType.Spring` play now ends as Framer Motion's spring animation does. It samples each channel's
  spring by the time since the play started and ends when its slowest channel has rested, measured on
  Framer's 50 ms samples and rest thresholds: within 0.005 of the target and at no more than 0.01 per second
  for a travel under 5, within 0.5 and at 2 per second for a longer one, a color measured over a travel of
  100. It ended before once every channel came within Velvet's own epsilons — 0.001 on opacity, scale and
  color, 0.1 on translate, rotate and lengths — checked on every tick. An opacity on the default spring now
  ends at 1.1 s, and a spring that does not rest within 20 seconds now never ends. A frame that arrives late now moves a spring as far as the time it
  covers, where the integrator clamped such a frame to 1/30 s; a play interrupted by an exit-cancel still
  reverses on the integrator, resting by Framer's thresholds over the travel left.
