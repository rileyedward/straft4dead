"""Print a Source engine MDL's skeleton (bone names, parents) and sequence names. Python stdlib only.

usage: python -I mdl_info.py <file.mdl> [--seq]
Header layout follows studiohdr_t for MDL v44-v49 (L4D2 uses v49).
"""
import struct, sys

BONE_SIZE = 216     # mstudiobone_t, v44+
SEQ_SIZE = 212      # mstudioseqdesc_t, v44+


def cstr(buf, off):
    end = buf.index(b"\0", off)
    return buf[off:end].decode("ascii", "replace")


def main(path, show_seq):
    b = open(path, "rb").read()
    ident, version = struct.unpack_from("<4si", b, 0)
    name = cstr(b, 12)
    bone_count, bone_off = struct.unpack_from("<ii", b, 156)
    seq_count, seq_off = struct.unpack_from("<ii", b, 188)
    include_count, include_off = struct.unpack_from("<ii", b, 0x150) if len(b) > 0x158 else (0, 0)
    print("%s  %s v%d  bones=%d  seqs=%d" % (path, name, version, bone_count, seq_count))
    names = []
    for i in range(bone_count):
        base = bone_off + i * BONE_SIZE
        name_idx, parent = struct.unpack_from("<ii", b, base)
        names.append((cstr(b, base + name_idx), parent))
    for i, (n, p) in enumerate(names):
        print("  bone %3d  %-40s parent=%s" % (i, n, names[p][0] if p >= 0 else "-"))
    if show_seq:
        for i in range(seq_count):
            base = seq_off + i * SEQ_SIZE
            (label_idx,) = struct.unpack_from("<i", b, base + 4)
            print("  seq %4d  %s" % (i, cstr(b, base + label_idx)))


if __name__ == "__main__":
    main(sys.argv[1], "--seq" in sys.argv)
