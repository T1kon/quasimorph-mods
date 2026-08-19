# Third-party notices

## Cassini Jupiter cylindrical map (PIA07782)

The Jupiter diffuse replacement is adapted from **Cassini's Best Maps of Jupiter (Cylindrical
Map)**, image PIA07782.

- Source: https://www.jpl.nasa.gov/images/pia07782-cassinis-best-maps-of-jupiter-cylindrical-map/
- Source file: `nasa-pia07782.jpg` (3601x1801)
- Source SHA-256: `6B835DDD8036EA9EFD25DEF04F75AA60B69B096025A37EEAE5E9B6978E0A2281`
- Quasimorph `jupiter` RGB pixel SHA-256: `ABAF1941F3FD200F5754E54FDA6B8F88E556C9CE1BEC1548BC0085DAA7D40694`
- Quasimorph `jupiter_nightmap` RGB pixel SHA-256: `EBF25A1ED32B5DF4015E191FFC90E0325B6BE2030838C8F31CCE9DE9EEEE2E98`
- Credit: NASA/JPL/Space Science Institute
- JPL image-use policy: https://www.jpl.nasa.gov/jpl-image-use-policy/

The source map was cropped from 3601x1801 to 3600x1800 to remove duplicate boundary pixels,
deterministically color-graded to match Quasimorph's Jupiter palette, and sharpened without
generative processing. The dark-side texture was derived from the adjusted map using the color
transform measured from Quasimorph's original Jupiter diffuse and dark-side textures.

The committed outputs are:

- `jupiter-cassini.png`: `F6714A4BEB3D8653A00F2D2C3FEA1791C4B5BE092F25F9FBEB9FEAA4FBCF9C3F`
- `jupiter-cassini-night.png`: `B48C478493ECE27E589471EBF55A0D89975160FA8A05DB25751104CAED742DB5`

## Lunar Reconnaissance Orbiter color map

The Moon diffuse replacement is adapted from the 2025 color map in NASA's **CGI Moon Kit**.

- Source: https://svs.gsfc.nasa.gov/4720/
- Source file: `lroc_color_16bit_srgb_4k.tif` (4096x2048, 16-bit sRGB)
- Source SHA-256: `9731FA8AF425B6C2F88F277ECCA82BF8C603F3743894F64ED7B25C5BFEFA22FF`
- Quasimorph `moon` RGB pixel SHA-256: `709A2A6D5CADD863E65F4607ED4771A0D557BBA5F8CD8B26C74843A0BF569477`
- Quasimorph `moon_nightmap` RGB pixel SHA-256: `2CAE25AE8E70AA130F1ECAB3371AA7C5624C850C1E0CFD8051F85C6D91E6332A`
- Credit: NASA's Scientific Visualization Studio
- Visualizer: Ernie Wright (USRA)
- Scientist: Noah Petro (NASA/GSFC)

The source map was resampled to 3600x1800 and deterministically histogram-matched to Quasimorph's
original Moon palette. The dark-side texture uses the color transform measured from the original
diffuse/night pair. Positive residuals above the fitted transform preserve the original cyan
settlement lights. No generative processing was used.

The committed outputs are:

- `moon-lroc.png`: file `4A8F9D7367796153B2D0C4624E52F21ECF14FD2139FE88BAAF5C29420185807E`,
  RGB pixels `B94BF6095AAC5B9AC79061B44CD0B378DAD1A4CEC9E78119B74C64C2C2F977F9`
- `moon-lroc-night.png`: file `0C996F7BD4612D0657B960B5F0DC6E5465D6D8556DAB0492B3ABF60F104644BE`,
  RGB pixels `2478CFB29C658815587EBC55A3DA48FD95EF3C45B0636784979E0FDB2EABC52B`
