import os

guids = {
    '1850b919eb1f32a4b93dae83762d3eed': None,
    '1b1f70b226f40814a8af826b40b27a4c': None,
    '846d8a1f3901f4e4aad991782e487211': None,
    '93ae62d6ac4e85042b4352cac42a53df': None,
    'fc9e3c9276b3dd440ad2b7ec84c718bf': None,
    # Also check the plan's GUIDs
    '96e8853f9369f2f4299ebc49dc52fb27': None,
    '0264c97eb8fd2dd4581fb3aa6ebdf7e8': None,
    'c1fc25a9a2356134b8fd8dc2ab6abe3c': None,
    'f189f482bb9fa5e4a8d2dfe1ce8a96fe': None
}

game_root = r"C:\Users\liand\Downloads\Cities-Skylines-2-SteamRIP.com\Cities Skylines II\Cities2_Data"

for root, dirs, files in os.walk(game_root):
    for f in files:
        if f.endswith(('.cid', '.cok', '.json', '.txt', '.csv', '.meta', '.index')):
            p = os.path.join(root, f)
            try:
                data = open(p, 'rb').read()
                for g in guids:
                    gb = g.encode('ascii')
                    if gb in data:
                        print(f"Found {g} in {p}")
            except Exception:
                pass
