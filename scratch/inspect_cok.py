import zipfile, os

mod_dir = r"C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1"
cok_name = [f for f in os.listdir(mod_dir) if f.endswith('.cok')][0]
p = os.path.join(mod_dir, cok_name)
print("Path:", p)
print("Is zip:", zipfile.is_zipfile(p))
if zipfile.is_zipfile(p):
    with zipfile.ZipFile(p, 'r') as z:
        names = z.namelist()
        print("Files in zip:", len(names))
        for n in names:
            if n.endswith('.Prefab'):
                print("Prefab:", n)
else:
    with open(p, 'rb') as f:
        print("Header bytes:", f.read(64))
