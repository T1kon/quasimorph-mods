# Planet Scaling: Jupiter HD

Jupiter HD is an optional add-on for Planet Scaling. It replaces Jupiter's 1024x512 diffuse and
dark-side maps with 3600x1800 maps derived from NASA/JPL's Cassini cylindrical mosaic.

The source image is real spacecraft imagery, not generative artwork. Its longitude and latitude
layout already matches Quasimorph's original Jupiter map. The replacement receives deterministic
color grading based on the original game texture, and its dark-side map uses the color transform
recovered from Quasimorph's original diffuse and night-map pair.

The add-on preserves Jupiter's original mesh, material, atmosphere shader, rotation, dimensions,
and point filtering. Only the two texture inputs are replaced.

## Requirements

- Planet Scaling
- Quasimorph 1.0

The manifest declares `PlanetScaling` as a required dependency. Quasimorph disables this add-on if
Planet Scaling is unavailable.

## Performance

The textures are loaded once when space mode starts and released when it ends. They use about 39 MB
of uncompressed texture memory in total. The add-on does not run per-frame code.

## Image source and credit

- PIA07782, Cassini's Best Maps of Jupiter (Cylindrical Map)
- Credit: NASA/JPL/Space Science Institute
- https://www.jpl.nasa.gov/images/pia07782-cassinis-best-maps-of-jupiter-cylindrical-map/

See `THIRD_PARTY_NOTICES.md` for the source and usage-policy links.

## Build

The project expects Quasimorph at `C:\Games\Steam\steamapps\common\Quasimorph`. Override
`GameManagedDir` when building if needed:

```powershell
dotnet build .\src\PlanetScalingTextures.csproj -c Release -p:GameManagedDir="D:\SteamLibrary\steamapps\common\Quasimorph\Quasimorph_Data\Managed"
```

The resulting DLL is copied into `package`.
