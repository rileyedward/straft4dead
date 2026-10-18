"""Minimal Valve VPK (v1/v2) reader: list and extract files from a *_dir.vpk set. Python stdlib only.

usage:
  python -I vpk.py list    <pak01_dir.vpk> [substring ...]
  python -I vpk.py extract <pak01_dir.vpk> <outdir> <path-or-prefix> [...]

Reads the player's own install and writes to <outdir>; nothing it produces belongs in the repo.
"""
import os, struct, sys


def read_cstr(f):
    out = bytearray()
    while True:
        c = f.read(1)
        if not c or c == b"\0":
            return out.decode("utf-8", "replace")
        out += c


def read_index(dir_path):
    entries = {}
    with open(dir_path, "rb") as f:
        sig, ver = struct.unpack("<II", f.read(8))
        if sig != 0x55AA1234:
            raise ValueError("not a VPK directory file")
        (tree_size,) = struct.unpack("<I", f.read(4))
        if ver == 2:
            f.read(16)
        header = f.tell()
        while True:
            ext = read_cstr(f)
            if not ext:
                break
            while True:
                folder = read_cstr(f)
                if not folder:
                    break
                while True:
                    name = read_cstr(f)
                    if not name:
                        break
                    crc, preload, arch, off, length, term = struct.unpack("<IHHIIH", f.read(18))
                    pre = f.read(preload)
                    folder_part = "" if folder == " " else folder + "/"
                    path = "%s%s.%s" % (folder_part, name, ext) if ext != " " else folder_part + name
                    entries[path.lower()] = (arch, off, length, pre)
        data_base = header + tree_size
    return entries, data_base


def read_file(dir_path, entry, data_base):
    arch, off, length, pre = entry
    if length == 0:
        return pre
    if arch == 0x7FFF:
        src, pos = dir_path, data_base + off
    else:
        src, pos = dir_path.replace("_dir.vpk", "_%03d.vpk" % arch), off
    with open(src, "rb") as f:
        f.seek(pos)
        return pre + f.read(length)


def main(argv):
    cmd, dir_path = argv[1], argv[2]
    entries, base = read_index(dir_path)
    if cmd == "list":
        subs = [s.lower() for s in argv[3:]]
        for p in sorted(entries):
            if all(s in p for s in subs):
                print(p)
    elif cmd == "extract":
        out = argv[3]
        n = 0
        for want in argv[4:]:
            want = want.lower().replace("\\", "/")
            for p, e in entries.items():
                if p == want or p.startswith(want):
                    dst = os.path.join(out, *p.split("/"))
                    os.makedirs(os.path.dirname(dst), exist_ok=True)
                    with open(dst, "wb") as fh:
                        fh.write(read_file(dir_path, e, base))
                    n += 1
        print("extracted %d files" % n)
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main(sys.argv)
