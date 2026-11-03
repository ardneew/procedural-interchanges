import zipfile, json, os, re

mod_dir = r"C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1"
p = os.path.join(mod_dir, "Admiral Electron's System Interchanges.cok")

with zipfile.ZipFile(p, 'r') as z:
    raw = z.open("AE-Trumpet 2.Prefab").read().decode('utf-8', errors='replace')
    clean = re.sub(r'\$fstrref:', '', raw)
    data = json.loads(clean)
    comps = data.get("components", {}).get("$rcontent", [])
    for c in comps:
        if c.get("name") == "ObjectSubNets":
            subnets = c.get("m_SubNets", {})
            items = subnets.get("$rcontent", [])
            print("SubNets items count:", len(items))
            if items:
                print("First subnet item:", json.dumps(items[0], indent=2))
                print("Second subnet item:", json.dumps(items[1], indent=2))
