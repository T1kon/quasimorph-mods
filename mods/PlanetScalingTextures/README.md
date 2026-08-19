# Planet Scaling: HD Textures

HD Textures is an optional add-on for Planet Scaling. It replaces the diffuse and dark-side maps
for the following bodies with 3600x1800 maps derived from real spacecraft imagery:

| Body | Source |
| --- | --- |
| Jupiter | NASA/JPL Cassini cylindrical mosaic PIA07782 |
| Moon | NASA Scientific Visualization Studio 2025 LRO color map |

The source maps are deterministic, non-generative conversions. They are aligned and color-graded
against Quasimorph's original maps so that the added surface detail retains the game's palette. The
Moon dark-side map also preserves the cyan settlement lights from the original game texture.

The add-on preserves the original meshes, materials, atmosphere shader, rotation, dimensions, and
point filtering. Only the diffuse and dark-side texture inputs are replaced.

## Requirements

- Planet Scaling
- Quasimorph 1.0

The manifest declares `PlanetScaling` as a required dependency. Quasimorph disables this add-on if
Planet Scaling is unavailable.

## Performance

The four textures are loaded once when space mode starts and released when it ends. Each body uses
about 39 MB of uncompressed texture memory, or about 78 MB total. The add-on does not run per-frame
code.

## Image sources and credit

- PIA07782, Cassini's Best Maps of Jupiter (Cylindrical Map)
  - Credit: NASA/JPL/Space Science Institute
  - https://www.jpl.nasa.gov/images/pia07782-cassinis-best-maps-of-jupiter-cylindrical-map/
- CGI Moon Kit, 2025 LRO color map
  - Credit: NASA's Scientific Visualization Studio; Ernie Wright (USRA); Noah Petro (NASA/GSFC)
  - https://svs.gsfc.nasa.gov/4720/

See `THIRD_PARTY_NOTICES.md` for source and processing details.

## Rebuilding the Moon textures

The conversion tool requires Python 3.12 and the packages pinned in `tools/requirements.txt`. It
expects the NASA 4096x2048 16-bit sRGB TIFF plus extracted copies of Quasimorph's original `moon`
and `moon_nightmap` textures:

```powershell
python -m pip install -r .\tools\requirements.txt
python .\tools\build_moon_textures.py `
  --source .\lroc_color_16bit_srgb_4k.tif `
  --game-diffuse .\moon.png `
  --game-night .\moon_nightmap.png `
  --output-dir .\package\Textures
```

The original Quasimorph textures and the 59 MB NASA source TIFF are build inputs and are not
redistributed in this repository.

## Repository assets

The four game-ready PNGs under `package/Textures` are intentionally versioned as ordinary Git
blobs because they are the runtime payload copied directly into local and Workshop installations.
They total about 21.5 MB, each file is at most 10.8 MB, and they are expected to change only when a
source or processing pipeline is deliberately refreshed. The much larger source datasets,
extracted Quasimorph textures, and intermediate files remain ignored under `.work`. Refreshes are
owned by this mod's maintainers and must update provenance, output checksums, and visual validation;
rollback is a normal source revert. Git LFS is not used for this small, directly consumed package.

## Building the mod

The project expects Quasimorph at `C:\Games\Steam\steamapps\common\Quasimorph`. Override
`GameManagedDir` when building if needed:

```powershell
dotnet build .\src\PlanetScalingTextures.csproj -c Release `
  -p:GameManagedDir="D:\SteamLibrary\steamapps\common\Quasimorph\Quasimorph_Data\Managed"
```

The resulting DLL is copied into `package`.
