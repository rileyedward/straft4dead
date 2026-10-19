"""Convert the L4D2 HUD textures the plugin draws ([HUD] TextureDir) from the player's own L4D2 install to PNG.

usage: python -I build_hud.py <L4D2 left4dead2 dir> <work dir> <blender.exe>
Output: <work>/hud/<name>.png
"""
import os, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SURVIVORS = ["mechanic", "gambler", "producer", "coach"]
TEXTURES = (
    ["materials/vgui/hud/" + n for n in [
        "healthbar_bg_player", "healthbar_ticks_withglow_green", "healthbar_ticks_withglow_orange",
        "healthbar_ticks_withglow_red", "healthbar_ticks_withglow_white", "healthbar_withglow_green",
        "healthbar_withglow_orange", "healthbar_withglow_red", "healthbar_withglow_white", "icon_medkit",
        "s_healthbar_outline", "scalablepanel_bgblack50_outlinegrey"]]
    + ["materials/vgui/terror/l4d360ui/hud/" + n for n in ["inventory_firstaidkit", "inventory_painpills"]]
    + ["materials/vgui/s_panel_" + n for n in ["background", "dead", "healthcross"]]
    + ["materials/vgui/s_panel_%s%s" % (s, v) for s in SURVIVORS for v in ("", "_incap")]
)

l4d2, work, blender = sys.argv[1], os.path.abspath(sys.argv[2]), sys.argv[3]
extracted = os.path.join(work, "extracted", "l4d2")
out = os.path.join(work, "hud")
subprocess.run([sys.executable, "-I", os.path.join(HERE, "vpk.py"), "extract", os.path.join(l4d2, "pak01_dir.vpk"), extracted]
               + [t + ".vtf" for t in TEXTURES], check=True)
vtfs = [os.path.join(extracted, *t.split("/")) + ".vtf" for t in TEXTURES]
log = subprocess.run([blender, "-b", "--factory-startup", "--addons", "SourceIO", "--python", os.path.join(HERE, "vtf_to_png.py"), "--", out]
                     + [v for v in vtfs if os.path.exists(v)], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
print(log.count("S4D png"), "of", len(TEXTURES), "HUD textures ->", out)
