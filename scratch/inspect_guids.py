import zipfile, json, os, re

mod_dir = r"C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1"
p = os.path.join(mod_dir, "Admiral Electron's System Interchanges.cok")

prefabs = [
    "AE-Trumpet 2.Prefab",
    "AE-T.Prefab",
    "AE-Trumpet 1.Prefab",
    "AE-Trumpet Roundabout.Prefab",
    "AE-Full Diamond.Prefab",
    "AE-Twisted Diamond.Prefab",
    "AE-Twisted Cloverleaf.Prefab",
    "AE-Stack Interchange.Prefab",
    "AE-Turbine.Prefab"
]

with zipfile.ZipFile(p, 'r') as z:
    all_guids = set()
    for pf in prefabs:
        raw = z.open(pf).read().decode('utf-8', errors='replace')
        clean = re.sub(r'\$fstrref:', '', raw)
        data = json.loads(clean)
        comps = data.get("components", {}).get("$rcontent", [])
        for c in comps:
            if c.get("name") == "ObjectSubNets":
                items = c.get("m_SubNets", {}).get("$rcontent", [])
                for item in items:
                    all_guids.add(item.get("m_NetPrefab"))
    print("All GUIDs found:")
    for g in sorted(all_guids):
        print(" ", g)
