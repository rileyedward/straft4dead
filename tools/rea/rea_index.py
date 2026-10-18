"""Turn the REA managed-members Evidence bundle into compact text indexes.

usage: python -I rea_index.py <bundle.json> <outdir>
writes types.txt (type : base, field/method counts) and members/<Type>.txt (fields, methods, callees).
"""
import json, os, sys, collections

bundle, out = sys.argv[1], sys.argv[2]
b = json.load(open(bundle, encoding="utf-8"))
m = [r for r in b["records"] if r["operation"] == "inspect_managed_members"][0]["normalized_result"]
os.makedirs(os.path.join(out, "members"), exist_ok=True)

tok2type = {t["token"]: t["full_name"] for t in m["types"]}
ref_names = {}
for r in m.get("member_refs", []):
    ref_names[r.get("token")] = r

def tname(tok):
    return tok2type.get(tok, tok)

def sig(s):
    if not s:
        return ""
    if s.get("kind") == "field":
        return str(s.get("field_type"))
    return "(%s) -> %s" % (", ".join(map(str, s.get("parameter_types") or [])), s.get("return_type"))

fields = collections.defaultdict(list)
for f in m["fields"]:
    fields[f["declaring_type"]].append(f)
methods = collections.defaultdict(list)
for x in m["methods"]:
    methods[x["declaring_type"]].append(x)
calls = collections.defaultdict(list)
for e in m["call_edges"]:
    calls[e["caller"]].append("%s.%s" % (e.get("target_declaring_type"), e.get("target_name")))


with open(os.path.join(out, "types.txt"), "w", encoding="utf-8") as fh:
    for t in m["types"]:
        fh.write("%s : %s  [f%d m%d]\n" % (t["full_name"], tname(t.get("extends_token")),
                                          len(fields[t["full_name"]]), len(methods[t["full_name"]])))

for t in m["types"]:
    n = t["full_name"]
    safe = n.replace("/", "+").replace("<", "_").replace(">", "_").replace("`", "_")
    with open(os.path.join(out, "members", safe + ".txt"), "w", encoding="utf-8") as fh:
        fh.write("type %s : %s\n\n" % (n, tname(t.get("extends_token"))))
        for f in fields[n]:
            fh.write("  field %s : %s\n" % (f["name"], sig(f.get("signature"))))
        fh.write("\n")
        for x in methods[n]:
            q = "%s.%s" % (n, x["name"])
            fh.write("  method %s %s\n" % (x["name"], sig(x.get("signature"))))
            seen = []
            for c in calls[q]:
                if c not in seen:
                    seen.append(c)
            for c in seen:
                fh.write("      -> %s\n" % c)

print("types", len(m["types"]))
