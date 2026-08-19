# Extracting and replacing Quasimorph textures

This guide describes the workflow used by `PlanetScalingTextures` to inspect Quasimorph's Unity
assets, export reference textures, and replace selected textures at runtime from a normal mod
package. It has been verified with Quasimorph 1.0 and UnityPy 1.25.3.

The recommended approach does **not** rewrite files under `Quasimorph_Data`. Game updates replace
edited asset files, direct changes are difficult to distribute through Workshop, and an incorrect
serialized-file write can prevent the game from loading. Export the originals for reference, then
let a mod assign replacement textures while the game is running.

## Keep extracted assets out of Git

Quasimorph's original textures and other extracted assets belong to the game. Do not redistribute
them in this repository or a Workshop package. Store them under the ignored `.work/` directory:

```text
.work/
  texture-export/
    originals/
    sources/
```

Only commit replacement images that you have the right to redistribute. Record their source,
license or usage policy, processing steps, and checksums in a third-party notice. Large source
TIFFs and intermediate images should remain reproducible build inputs outside Git.

## Requirements

- A local Steam installation of Quasimorph.
- Python 3.12. Python 3.8 or newer should also work with UnityPy.
- UnityPy 1.25.3. Pinning the version matters because its parsing API can change between minor
  releases.

Steam's **Manage > Browse local files** command is the easiest way to find the installation. This
guide uses the following example path:

```text
C:\Games\Steam\steamapps\common\Quasimorph
```

Create an isolated extraction environment from the repository root:

```powershell
$TextureWork = Join-Path (Resolve-Path .) ".work\texture-export"
python -m venv "$TextureWork\.venv"
& "$TextureWork\.venv\Scripts\python.exe" -m pip install "UnityPy==1.25.3"
```

## Where the planetary textures are stored

Unity's serialized asset files are under `Quasimorph_Data`. Keep each adjacent `.resS` file in
place: it contains the streamed pixel data and UnityPy resolves it automatically.

| Asset file | Relevant content |
| --- | --- |
| `sharedassets1.assets` | Earth (`terra`), Venus, their night maps, and Earth's cloud texture |
| `sharedassets2.assets` | Most planets, moons, asteroids, rings, and space materials |

Most normal planet maps are 1024x512 `Texture2D` objects. Their normal materials use these shader
properties:

| Property | Purpose |
| --- | --- |
| `_DiffuseTex` | Illuminated surface or atmosphere color |
| `_CloudAndNightTex` | Dark-side color and emissive settlement details |

Some bodies have additional renderers and materials:

- Earth, Venus, and Saturn have separate cloud textures.
- Saturn's rings use `rings_material`, `_MainTex`, and `rings_texture`.
- The square 512x512 textures and materials whose names contain `bramfatura` belong to the alternate
  Bramfatura presentation. They are not the normal 2:1 globe maps.
- Stations and irregular bodies can use non-2:1 UV layouts. Do not treat them as equirectangular
  maps merely because they are `Texture2D` objects.

The following normal-view pairs were confirmed in Quasimorph 1.0:

| Normal material | Diffuse texture | Night texture | Texture asset file |
| --- | --- | --- | --- |
| `earth_mat` | `terra` | `earth_nightmap` | `sharedassets1.assets` |
| `venus_mat` | `venus` | `venus_nightmap` | `sharedassets1.assets` |
| `mercury_mat` | `mercury` | `mercury_nightmap` | `sharedassets2.assets` |
| `moon_mat` | `moon` | `moon_nightmap` | `sharedassets2.assets` |
| `mars_mat` | `mars` | `mars_nightmap` | `sharedassets2.assets` |
| `jupiter_mat` | `jupiter` | `jupiter_Nightmap` | `sharedassets2.assets` |
| `saturn_mat` | `saturn` | `pluto_nightmap` | `sharedassets2.assets` |

`earth_mainMenu_mat` uses the same Earth texture pair as `earth_mat`.

Treat this table as a search map, not a permanent game contract. Repeat the inspection after a
substantial Quasimorph update.

## Export textures with UnityPy

Save the following as `.work/texture-export/export_textures.py`. Change `GAME_DATA` and `WANTED`
for the textures being investigated.

```python
from pathlib import Path

import UnityPy


GAME_DATA = Path(r"C:\Games\Steam\steamapps\common\Quasimorph\Quasimorph_Data")
OUTPUT_DIR = Path(__file__).parent / "originals"
ASSET_FILES = ("sharedassets1.assets", "sharedassets2.assets")
WANTED = {
    "earth_nightmap",
    "jupiter",
    "jupiter_Nightmap",
    "mars",
    "mars_nightmap",
    "mercury",
    "mercury_nightmap",
    "moon",
    "moon_nightmap",
    "terra",
    "venus",
    "venus_nightmap",
}


OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

found = set()
for asset_name in ASSET_FILES:
    environment = UnityPy.load(str(GAME_DATA / asset_name))
    for obj in environment.objects:
        if obj.type.name != "Texture2D":
            continue

        texture = obj.parse_as_object()
        if texture.m_Name not in WANTED:
            continue

        output_path = OUTPUT_DIR / f"{texture.m_Name}.png"
        texture.image.save(output_path)
        found.add(texture.m_Name)
        print(
            f"{asset_name}: {texture.m_Name} "
            f"{texture.m_Width}x{texture.m_Height} -> {output_path}"
        )

missing = WANTED - found
if missing:
    raise SystemExit(f"Textures not found: {', '.join(sorted(missing))}")
```

Run it with the environment created above:

```powershell
& "$TextureWork\.venv\Scripts\python.exe" "$TextureWork\export_textures.py"
```

If a texture appears vertically inverted in another extraction tool, use the UnityPy export as the
orientation reference. Do not flip a replacement until it has been compared with the actual
material in game.

## Inspect materials and texture slots

Texture names alone do not prove which renderer consumes an image. Enumerate `Material` objects
and their saved texture properties:

```python
from pathlib import Path

import UnityPy


GAME_DATA = Path(r"C:\Games\Steam\steamapps\common\Quasimorph\Quasimorph_Data")
PROPERTIES = {"_BaseMap", "_CloudAndNightTex", "_DiffuseTex", "_MainTex"}

for asset_name in ("sharedassets1.assets", "sharedassets2.assets"):
    environment = UnityPy.load(str(GAME_DATA / asset_name))
    texture_names = {
        obj.path_id: obj.peek_name()
        for obj in environment.objects
        if obj.type.name == "Texture2D"
    }

    print(f"\n[{asset_name}]")
    for obj in environment.objects:
        if obj.type.name != "Material":
            continue

        material = obj.parse_as_object()
        rows = []
        for property_name, texture_environment in material.m_SavedProperties.m_TexEnvs:
            if property_name not in PROPERTIES:
                continue

            pointer = texture_environment.m_Texture
            if not pointer.path_id:
                continue

            if pointer.file_id == 0:
                texture_name = texture_names.get(pointer.path_id, "<unknown>")
            else:
                texture_name = f"<external file {pointer.file_id}>"
            rows.append((property_name, texture_name, pointer.path_id))

        if not rows:
            continue

        print(material.m_Name)
        for property_name, texture_name, path_id in rows:
            print(f"  {property_name}: {texture_name} (path ID {path_id})")
```

An external-file pointer means that the material references another serialized asset. Inspect the
same path ID in the dependency asset instead of assuming a name. For example, some Earth materials
in `sharedassets2.assets` reference textures from `sharedassets1.assets`.

## Prepare a replacement

For a normal globe map:

1. Start from a 2:1 equirectangular source with known provenance.
2. Align longitude, latitude, and vertical orientation against the exported original. A source
   centered on a different longitude needs a circular horizontal roll, not a destructive crop.
3. Ensure the left and right edges form a continuous longitude seam. Clamp vertically at the poles.
4. Grade brightness, contrast, and color toward the original texture. High spatial resolution alone
   can look out of place beside Quasimorph's low-poly, point-filtered art.
5. Preserve real source features. If processing is intended as an upscale or scientific-data
   conversion, do not use a generative model to invent terrain.
6. Build a matching night texture from the original diffuse/night relationship. Preserve deliberate
   settlement or faction lights separately from the dark-side color transform.
7. Save ordinary color maps as RGB PNG. Use RGBA only when the shader actually consumes alpha.

`PlanetScalingTextures` currently uses 3600x1800 RGB PNGs. A single uncompressed RGB24 texture at
that size occupies 19,440,000 bytes (about 18.5 MiB) at runtime; a diffuse/night pair occupies about
37.1 MiB. PNG file size does not represent Unity runtime memory.

Before packaging, verify at least:

- exact dimensions and RGB/RGBA mode;
- no unintended vertical flip or longitude offset;
- horizontal seam continuity and sensible poles;
- correct placement of recognizable landmarks;
- night-side brightness and preserved emissive markings;
- source checksum and deterministic rebuild output.

## Replace textures from a mod

The implementation in
[`mods/PlanetScalingTextures/src/Plugin.cs`](../mods/PlanetScalingTextures/src/Plugin.cs) is the
working reference. Its runtime flow is:

1. On `ModHookType.SpaceStarted`, obtain `SpaceObjects` from game state.
2. Find the target by its `SpaceObject.ID`. This ID is not necessarily the same thing as a texture
   or material name.
3. Read replacement PNGs from the mod's `Textures` directory.
4. Create an sRGB `Texture2D` without mipmaps and load the PNG through
   `ImageConversion.LoadImage`.
5. Apply point filtering, horizontal repeat, vertical clamp, and the intended anisotropic level.
6. Walk renderers under that space object and require the expected shader properties and expected
   original diffuse texture name before assigning replacements.
7. Keep strong references to loaded textures for as long as space mode is active.
8. Before destroying replacements on `ModHookType.SpaceFinished` or partial-load failure, restore
   every changed material slot to its original texture. This also makes repeated space-mode entry
   safe.

The expected original texture-name check is important. A space object can contain multiple
renderers, including clouds, rings, effects, damaged views, or Bramfatura views. Matching only the
body ID risks assigning a 2:1 globe map to an unrelated material.

Add packaged images beneath the mod directory:

```text
mods/YourTextureMod/package/
  modmanifest.json
  YourTextureMod.dll
  Textures/
    body-source.png
    body-source-night.png
```

Do not load a replacement every frame. Load once when entering the relevant mode, reuse it, and
release it when leaving that mode. Apply bodies independently so one missing object, file, or
material does not disable all other replacements.

## Build and local installation

Build the relevant project from the repository root. The existing add-on copies its release DLL
into `package`:

```powershell
dotnet build .\mods\PlanetScalingTextures\src\PlanetScalingTextures.csproj -c Release
```

Quasimorph loads unpacked local mods from:

```text
%USERPROFILE%\AppData\LocalLow\Magnum Scriptum LTD\Quasimorph\LocalUserPresets\<UniqueModName>
```

Copy the **contents of the package directory** there, preserving the `Textures` subdirectory. The
folder directly under `LocalUserPresets` must contain `modmanifest.json`; an extra package nesting
level prevents discovery. Restart Quasimorph completely after changing a DLL because mod
assemblies are loaded during bootstrap.

## Validation and troubleshooting

Automated work in this repository does not launch or control Quasimorph. The person testing the mod
should compare the same body with and without the texture mod and inspect:

- illuminated terrain or atmosphere;
- the terminator and dark side;
- north and south poles;
- the longitude seam;
- clouds, rings, and other separate renderers;
- the normal orbital view rather than only the Bramfatura presentation.

For loading or material-selection failures, inspect:

```text
%USERPROFILE%\AppData\LocalLow\Magnum Scriptum LTD\Quasimorph\Player.log
```

Useful diagnostics should log the body ID, replacement filename, matched material count, and a full
exception. Typical symptoms are:

| Symptom | Likely cause |
| --- | --- |
| Mod is absent | Wrong `LocalUserPresets` path, extra folder nesting, invalid manifest, or missing dependency |
| DLL update has no effect | Game was not fully restarted |
| `0` materials matched | Wrong body ID, shader property, expected original texture name, or view |
| White or glowing body | Wrong color space, material slot, alpha interpretation, or shader/material replacement |
| Mirrored landmarks | Longitude direction, horizontal roll, or vertical orientation is wrong |
| Visible vertical line | Source edges are not a continuous longitude seam or horizontal wrapping is wrong |
| Clouds or rings remain blurry | They are separate materials and textures, not part of the diffuse/night pair |
| Memory use is unexpectedly high | Runtime textures are uncompressed and may have mipmaps or alpha channels |

## Updating after a game patch

After a substantial Quasimorph update:

1. Re-run the texture and material inventory against the updated `sharedassets` files.
2. Confirm body IDs and renderer structure by static inspection or guarded runtime logging.
3. Re-export the exact originals used for matching and compare their hashes and dimensions.
4. Rebuild the mod against the updated managed assemblies.
5. Repeat the user-operated visual checklist and check `Player.log` for texture-loading warnings.
