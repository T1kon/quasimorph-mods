# Planet Scaling

Planet Scaling changes the normal 3D orbital view so that the Magnum feels small beside planets. It does not modify the 2D destination-selection starmap.

The mod is deliberately visual-only:

- the Magnum's rendered hierarchy is reduced without changing its logical transform;
- the normal orbit camera is moved closer to the Magnum so the smaller ship remains readable;
- local light range and intensity are reduced with the model, preventing overexposure;
- attached particle effects, including the travelling exhaust, follow the reduced ship hierarchy
  without increasing their emission density;
- the ship-upgrade overview camera follows the reduced model while individual module cameras remain unchanged;
- planet sizes, orbit paths, travel time, click targets, UI anchors, and save data are unchanged;
- individual ship-module cameras retain their vanilla distances.

## Configuration

Edit `config.json` in the installed mod directory and restart the game.

| Setting | Default | Range | Effect |
| --- | ---: | ---: | --- |
| `ShipVisualScale` | `0.12` | `0.02`–`1.0` | Scale of the Magnum model and its attached effects. |
| `CameraDistanceScale` | `0.25` | `0.05`–`1.0` | Multiplier for the normal orbital camera distance. |
| `ShipScreenCameraDistanceScale` | `0.12` | `0.02`–`1.0` | Multiplier for the ship-upgrade overview camera. |

Suggested alternatives:

- Readable: ship `0.20`, camera `0.35`
- Immersive: ship `0.12`, camera `0.25`
- Monumental: ship `0.07`, camera `0.16`

## Planet proportions

This version does not scale any planet. Quasimorph's primary planet models already use roughly astronomical relative radii:

| Body | Vanilla model radius | Relative to Earth |
| --- | ---: | ---: |
| Mercury | 14 | 0.38 |
| Venus | 35 | 0.95 |
| Earth | 37 | 1.00 |
| Mars | 19 | 0.51 |
| Jupiter | 455 | 12.30 |
| Saturn | 430 | 11.62 |

Jupiter and Saturn are somewhat exaggerated relative to their astronomical ratios, but the larger visual difference comes from the camera. Vanilla ship-orbit radii are approximately 28 around Mercury, 50 around Venus, 60 around Earth, and 800 around Jupiter and Saturn. Reducing the fixed camera-to-ship distance therefore enlarges Mercury and Venus much more on screen than the gas giants. This is perspective, not additional planet scaling.

## Build

The project expects Quasimorph at `C:\Games\Steam\steamapps\common\Quasimorph`. Override `GameManagedDir` when building if needed:

```powershell
dotnet build .\src\PlanetScaling.csproj -c Release -p:GameManagedDir="D:\SteamLibrary\steamapps\common\Quasimorph\Quasimorph_Data\Managed"
```

The resulting DLL is copied into `package`.

## Local installation

Copy the contents of `package` into:

```text
%USERPROFILE%\AppData\LocalLow\Magnum Scriptum LTD\Quasimorph\LocalUserPresets\PlanetScaling
```

The game loads assemblies from `LocalUserPresets` during startup. A restart is required after replacing the DLL.
