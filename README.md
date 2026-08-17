# Quasimorph Mods

Source repository for Quasimorph mods and their supporting documentation.

Automated work on this repository does not launch or drive the game. In-game validation is performed
by the user from a concise test checklist; builds, static checks, and supplied log/report analysis can
be handled automatically. See [AGENTS.md](AGENTS.md).

## Mods

- [Planet Scaling](mods/PlanetScaling/README.md) — makes the Magnum feel appropriately small
  beside planets in the 3D orbital view.
- [Planet Scaling: Jupiter HD](mods/PlanetScalingTextures/README.md) — optional NASA/JPL Cassini
  textures for Jupiter in Planet Scaling's closer orbital view.
- [CustomStart](mods/CustomStart/README.md) — creates configurable Early, EarlyMid, Mid, and Late campaign
  starts with correlated faction progress, world history, learned recipes, practical arsenal and
  supply stockpiles, Magnum upgrades, and optional in-game MCM controls.

Each directory under `mods/` is independently buildable and contains its own source, package
manifest, configuration or assets as needed, and documentation. Compiled assemblies are release
artifacts and are not stored in Git.

## Modding reference

- [Official Quasimorph modding guide](https://steamcommunity.com/sharedfiles/filedetails/?id=3281671312)
- [Quasimorph Steam Workshop](https://steamcommunity.com/workshop/about/?appid=2059170)
