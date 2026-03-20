# Ecosystem Boundaries

This document defines ownership boundaries for the LanMountainDesktop plugin ecosystem.

## Source of Truth

- Host runtime and plugin loading: `LanMountainDesktop`
- Plugin SDK API baseline: `LanMountainDesktop`
- Shared contracts used by host and plugins: `LanMountainDesktop`
- Plugin market index and ecosystem metadata: `LanAirApp`
- Official sample plugin implementation and release artifacts: `LanMountainDesktop.SamplePlugin`

## What Stays in This Repository

- Host runtime code and desktop shell behavior
- Plugin runtime, loader, install coordination, and host integration
- Plugin SDK public interfaces, contracts, and registration helpers
- Host appearance and settings infrastructure
- Tests that validate host + SDK behavior

## What Should Not Be Maintained Here as Authoritative

- Market documentation as a canonical developer portal
- Market publishing metadata as canonical source
- Official sample plugin source and release pipeline
- External reference projects (for example ClassIsland) as dependencies

## Local Debugging Rule

When running a workspace build, plugin market index and related market assets must be resolved from the sibling repository path:

- `..\\LanAirApp\\airappmarket\\index.json`

The host should not depend on an embedded `LanAirApp` mirror inside this repository for workspace market resolution.
