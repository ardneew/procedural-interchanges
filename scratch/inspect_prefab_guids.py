import zipfile, json, os, re

mod_dir = r"C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1"
p = os.path.join(mod_dir, "Admiral Electron's System Interchanges.cok")

with zipfile.ZipFile(p, 'r') as z:
    for pf in ["AE-Trumpet 2.Prefab", "AE-T.Prefab", "AE-Full Diamond.Prefab"]:
        raw = z.open(pf).read().decode('utf-8', errors='replace')
        clean = re.sub(r'\$fstrref:', '', raw)
        data = json.loads(clean)
        comps = data.get("components", {}).get("$rcontent", [])
        for c in comps:
            if c.get("name") == "ObjectSubNets":
                items = c.get("m_SubNets", {}).get("$rcontent", [])
                guids_in_pf = {}
                for it in items:
                    g = it.get("m_NetPrefab")
                    guids_in_pf[g] = guids_in_pf.get(g, 0) + 1
                print(f"=== {pf} ===")
                for g, cnt in guids_in_pf.items():
                    print(f"  {g}: {cnt} segments")
