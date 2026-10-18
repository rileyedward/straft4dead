"""Headless Blender: convert Source VTF textures to PNG through SourceIO's VTF importer.

usage: blender -b --factory-startup --addons SourceIO --python vtf_to_png.py -- <outdir> <file.vtf> [...]
"""
import os, sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
out, files = argv[0], argv[1:]
os.makedirs(out, exist_ok=True)
for f in files:
    f = os.path.abspath(f)
    before = set(bpy.data.images.keys())
    bpy.ops.sourceio.vtf(filepath=f, directory=os.path.dirname(f), files=[{"name": os.path.basename(f)}])
    for name in set(bpy.data.images.keys()) - before:
        img = bpy.data.images[name]
        dst = os.path.join(out, os.path.splitext(os.path.basename(f))[0] + ".png")
        img.filepath_raw = dst
        img.file_format = "PNG"
        img.save()
        print("S4D png", dst, img.size[0], img.size[1])
