import zipfile, json, os, re, math

mod_dir = r"C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1"
p = os.path.join(mod_dir, "Admiral Electron's System Interchanges.cok")

ARCHETYPES = [
    ("Trumpet", "AE-Trumpet 2.Prefab", 3),
    ("DirectionalT", "AE-T.Prefab", 3),
    ("YStack", "AE-Trumpet 1.Prefab", 3),
    ("RoundaboutInterchange", "AE-Trumpet Roundabout.Prefab", 3),
    ("Diamond", "AE-Full Diamond.Prefab", 4),
    ("ParcloA4", "AE-Twisted Diamond.Prefab", 4),
    ("Cloverleaf", "AE-Twisted Cloverleaf.Prefab", 4),
    ("FourLevelStack", "AE-Stack Interchange.Prefab", 4),
    ("Turbine", "AE-Turbine.Prefab", 4)
]

with zipfile.ZipFile(p, 'r') as z:
    for arch, pf, req_legs in ARCHETYPES:
        raw = z.open(pf).read().decode('utf-8', errors='replace')
        clean = re.sub(r'\$fstrref:', '', raw)
        data = json.loads(clean)
        comps = data.get("components", {}).get("$rcontent", [])
        subnets = None
        for c in comps:
            if c.get("name") == "ObjectSubNets":
                subnets = c.get("m_SubNets", {}).get("$rcontent", [])
                break
        
        # Build node degrees and find unique node positions
        node_degrees = {} # node_idx -> count of connections
        node_positions = {}
        for seg_idx, it in enumerate(subnets):
            nx = it["m_NodeIndex"]["x"]
            ny = it["m_NodeIndex"]["y"]
            node_degrees[nx] = node_degrees.get(nx, 0) + 1
            node_degrees[ny] = node_degrees.get(ny, 0) + 1
            node_positions[nx] = it["m_BezierCurve"]["a"]
            node_positions[ny] = it["m_BezierCurve"]["d"]
            
        terminals = [n for n, deg in node_degrees.items() if deg == 1]
        print(f"=== {arch} ({pf}) ===")
        print(f"  Nodes: {len(node_positions)}, Segments: {len(subnets)}, Degree-1 Nodes: {len(terminals)} (expected {req_legs*2})")
        for t in terminals:
            pos = node_positions[t]
            az = math.atan2(pos['z'], pos['x'])
            r = math.hypot(pos['x'], pos['z'])
            print(f"    Node {t:2d}: ({pos['x']:6.1f}, {pos['y']:4.1f}, {pos['z']:6.1f}) -> r={r:6.1f}, az={math.degrees(az):6.1f} deg")
