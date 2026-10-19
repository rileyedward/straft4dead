"""Set keys in a BepInEx .cfg file in place (adds the file, section or key if missing).

usage: python -I set_cfg.py <file.cfg> Section.Key=value [Section.Key=value ...]
"""
import os, re, sys

path = sys.argv[1]
text = open(path, encoding="utf-8-sig").read() if os.path.exists(path) else ""
if os.path.dirname(path): os.makedirs(os.path.dirname(path), exist_ok=True)
for arg in sys.argv[2:]:
    left, value = arg.split("=", 1)
    section, key = left.split(".", 1)
    m = re.search(r"^\[" + re.escape(section) + r"\]\s*$", text, re.M)
    if not m:
        text = text.rstrip("\n") + "\n\n[" + section + "]\n\n" + key + " = " + value + "\n"
        continue
    nxt = re.search(r"^\[", text[m.end():], re.M)
    end = m.end() + (nxt.start() if nxt else len(text) - m.end())
    body = text[m.end():end]
    line = re.compile(r"^" + re.escape(key) + r" = .*$", re.M)
    if line.search(body):
        body = line.sub(lambda _: key + " = " + value, body, count=1)
    else:
        body = body.rstrip("\n") + "\n" + key + " = " + value + "\n\n"
    text = text[:m.end()] + body + text[end:]
open(path, "w", encoding="utf-8").write(text)
print("updated", path)
