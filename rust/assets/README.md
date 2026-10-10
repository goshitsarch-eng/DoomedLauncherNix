# Palette provenance

These are the original, byte-for-byte palette resources from
`src/resources/` in DoomedLauncherNix revision
`f48510d8b9eea4051e09d42e6393517485b5bd06` (4.0.3):

* `PLAYPAL.LMP`: Doom fallback palette.
* `HereticPalette.pal`: Heretic fallback palette.
* `HexenPalette.pal`: Hexen fallback palette.

The Rust artwork decoder embeds the resources and uses their first 768 bytes
(256 RGB colors), matching the original `DoomImage::defaultPalette` behavior.
All original palette layers remain in the files. A resource's own `PLAYPAL`
overrides the fallback. No palettes were regenerated or recolored.

The repository's existing [GPL-3.0 license](../../LICENSE) and original project
history are retained; the migration introduces no new license claim for these
previously distributed resources.
