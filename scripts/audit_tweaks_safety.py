import json

def audit(file_path):
    print(f"\n================ AUDITING {file_path} ================")
    with open(file_path, "r", encoding="utf-8") as f:
        tweaks = json.load(f)
        
    print(f"Total tweaks: {len(tweaks)}")
    missing_undo = []
    missing_orig = []
    
    for t in tweaks:
        inv = t.get("invokeScript", [])
        undo = t.get("undoScript", [])
        if inv and len(inv) > 0 and (not undo or len(undo) == 0):
            missing_undo.append((t["id"], t["name"], inv))
            
        for r in t.get("registry", []):
            if "OriginalValue" not in r or r["OriginalValue"] is None or r["OriginalValue"] == "":
                missing_orig.append((t["id"], r.get("Path"), r.get("Name")))
                
    print(f"Missing undoScript: {len(missing_undo)}")
    for tid, name, inv in missing_undo:
        print(f"  [NO UNDO SCRIPT] {tid}: {name}")
        print(f"     Invoke: {inv}")
        
    print(f"Missing OriginalValue: {len(missing_orig)}")
    for tid, path, name in missing_orig:
        print(f"  [NO ORIGINAL VALUE] {tid} -> {path} \\ {name}")

audit("WinSetupHub.App/Configuration/tweaks.default.json")
audit("WinSetupHub.App/Configuration/registry_tweaks.default.json")
