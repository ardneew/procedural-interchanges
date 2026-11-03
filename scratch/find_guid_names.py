import zipfile, json, os

mod_dir = r"C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1"
p = os.path.join(mod_dir, "Admiral Electron's System Interchanges.cok")

with zipfile.ZipFile(p, 'r') as z:
    for name in z.namelist():
        if name.endswith('.Prefab') or name.endswith('.json') or 'manifest' in name.lower():
            raw = z.open(name).read().decode('utf-8', errors='replace')
            for g in ["1850b9", "1b1f70", "846d8a", "93ae62", "fc9e3c"]:
                if g in raw:
                    print(f"Found {g} in {name}")
