# Material Symbols pause_circle

Source: Google Material Design Icons / Material Symbols Outlined.

- Icon: `pause_circle`, optical size 48, weight 600, fill 0, grade 0.
- Original SVG: https://raw.githubusercontent.com/google/material-design-icons/master/symbols/web/pause_circle/materialsymbolsoutlined/pause_circle_wght600_48px.svg
- License: Apache-2.0, full text in `LICENSE.txt`.
- Local Unity asset: `Assets/OurTaiko/Art/ui/material_symbols/pause_circle.png`.
- Conversion: rasterized the original SVG at 192 × 192 with transparency, with white foreground for Unity tinting. No font or network request is used at runtime.

Reproduce using ImageMagick:

```sh
magick -background none -density 384 Documentation/ThirdParty/MaterialSymbols/pause_circle_wght600_48px.svg -resize 192x192 -channel RGB -negate +channel Assets/OurTaiko/Art/ui/material_symbols/pause_circle.png
```
