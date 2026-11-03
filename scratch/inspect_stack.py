import zipfile, json, os, re

mod_dir = r"C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1"
p = os.path.join(mod_dir, "Admiral Electron's System Interchanges.cok")

with zipfile.ZipFile(p, 'r') as z:
    raw = z.open("AE-Stack Interchange.Prefab").read().decode('utf-8', errors='replace')
    clean = re.sub(r'\$fstrref:', '', raw)
    data = json.loads(clean)
    comps = data.get("components", {}).get("$rcontent", [])
    for c in comps:
        if c.get("name") == "ObjectSubNets":
            items = c.get("m_SubNets", {}).get("$rcontent", [])
            for i, it in enumerate(items):
                g = it.get("m_NetPrefab")
                if "1b1f70" in g:
                    a = it["m_BezierCurve"]["a"]
                    d = it["m_BezierCurve"]["d"]
                    nodes = it["m_NodeIndex"]
                    upg = it.get("m_Upgrades", {}).get("$rcontent", [])
                    print(f"Stack [1b1f70] Nodes:({nodes['x']:2d},{nodes['y']:2d}) A:({a['x']:6.1f},{a['y']:4.1f},{a['z']:6.1f}) -> D:({d['x']:6.1f},{d['y']:4.1f},{d['z']:6.1f}) Upg:{upg}")
