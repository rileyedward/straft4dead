"""List the methods that load a string literal matching a regex, from an REA bundle.

usage: python -I rea_strings.py <bundle.json> <regex> [more regexes...]
"""
import json, re, sys

b = json.load(open(sys.argv[1], encoding="utf-8"))
m = [r for r in b["records"] if r["operation"] == "inspect_managed_members"][0]["normalized_result"]
strs = {s["token"]: s["value"] for s in m["user_strings"]["literals"]}
pats = [re.compile(p, re.I) for p in sys.argv[2:]]
for x in m["methods"]:
    hits = []
    for a in (x.get("body") or {}).get("anchors", []):
        if a["opcode"] == "ldstr":
            s = strs.get(a.get("operand"), "")
            if any(p.search(s) for p in pats):
                hits.append(s)
    if hits:
        print("%s.%s" % (x["declaring_type"], x["name"]))
        for s in hits:
            print("    %r" % s[:160])
