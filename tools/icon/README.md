# Plugin icon generator

`make_icon.py` draws the plugin icon: a stack of three matte unit slabs, each with a status light
(two running units in the accent colour, one failed unit in red), on a deep amber tile. It shares
the shading and accent of the other plugin icons; the amber tile keeps it apart from them. The
icon is original artwork, no third-party source; the systemd logo is not used or imitated.

```bash
pip install pillow numpy
python tools/icon/make_icon.py tools/icon/out
cp tools/icon/out/icon_256.png icon.png
```

`BG_HUE` at the top of the script sets the background tint; `UNITS` sets the status of each slab.

The script writes `icon_{256,128,64,32,16}.png` into the given folder; only the 256 px file is
used, as `icon.png` in the repo root. The `out/` folder is not committed.
