# Changelog

Versions follow [semver](https://semver.org).

## [Unreleased]

### Added
- The axe takes a couple of hits to get through a plane (`HitsPerPlane`, default 2). Vertical cuts split from the top down, horizontal ones notch in from the side
- Heavy Hit axe mode: takes three planes in one blow for double durability
- Chop sounds made from layered vanilla sounds, different for a partial hit, a finished plane and a heavy hit
- Right click with an axe, saw, knife or chisel to turn the log a quarter turn
- Server settings for the axe get sent to players when they join
- Chopping blocks get made in the world: sneak + hold right click a standing log with an axe. One for every wood
- Freeform in the recipe list: carve whatever, turn and tip it any time, and switch to a real recipe later if there's wood for it
- Partly hewn logs can be put down in the world
- Bits cut off from the log fall away like on the anvil. Big ones drop as a partly hewn log, smaller ones as firewood. In freeform the axe splits a log into two halves and each half into two quarters of firewood

### Changed
- Firewood recipe cross is three planes wide
- The chopping block is round like the log on it

### Removed
- The grid recipe for the chopping block

## [0.1.0] - 2026-10-12

### Added
- Hewing in the handbook, wood chips, tree ring grain on cut faces
- Config file, tool durability, interaction hints, hold to chop
- Ctrl + right click to lay the log on its side, planks recipe

### Changed
- Updated to Vintage Story 1.22 and .NET 10
- Chopping block uses the vanilla multiblock system
- Recipe outline works like the anvil

### Fixed
- Logs couldn't be placed on the chopping block in multiplayer
- Breaking the chopping block ate the log
