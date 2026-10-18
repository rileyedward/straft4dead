"""Print one method's IL anchors in order (calls, field reads/writes, string literals) from an REA bundle.

usage: python -I rea_method.py <bundle.json> <Type.Method> [more...]
"""
import json, sys

b = json.load(open(sys.argv[1], encoding="utf-8"))
m = [r for r in b["records"] if r["operation"] == "inspect_managed_members"][0]["normalized_result"]
strs = {s["token"]: s["value"] for s in m["user_strings"]["literals"]}
mref = {}
for r in m["member_refs"]:
    mref[r["token"]] = "%s.%s" % (r.get("parent_name") or "?", r.get("name"))
mdef = {x["token"]: "%s.%s" % (x["declaring_type"], x["name"]) for x in m["methods"]}
fdef = {f["token"]: "%s.%s" % (f["declaring_type"], f["name"]) for f in m["fields"]}

def name(tok):
    return mdef.get(tok) or fdef.get(tok) or mref.get(tok) or tok

for want in sys.argv[2:]:
    for x in m["methods"]:
        if "%s.%s" % (x["declaring_type"], x["name"]) != want:
            continue
        print("=== %s" % want)
        for a in (x.get("body") or {}).get("anchors", []):
            op, operand = a["opcode"], a.get("operand")
            if a.get("operand_kind") == "string" or op == "ldstr":
                print("  %4d ldstr %r" % (a["il_offset"], strs.get(operand, operand)))
            elif op.startswith(("call", "newobj", "ldfld", "stfld", "ldsfld", "stsfld", "ldflda")):
                print("  %4d %s %s" % (a["il_offset"], op, name(operand)))
            elif op.startswith(("ldc.r4", "ldc.i4")) and operand is not None:
                print("  %4d %s %s" % (a["il_offset"], op, operand))
