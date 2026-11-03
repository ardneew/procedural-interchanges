import zipfile, json, os, re, math

mod_dir = r"C:\Users\liand\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\pdx_mods\126919_1"
cok_path = os.path.join(mod_dir, "Admiral Electron's System Interchanges.cok")

ARCHETYPES = [
    ("Trumpet", "AE-Trumpet 2.Prefab", 3, [-1.5707963, 0.0, 3.1415927]),
    ("DirectionalT", "AE-T.Prefab", 3, [-1.5707963, 0.0, 3.1415927]),
    ("YStack", "AE-Trumpet 1.Prefab", 3, [-1.5707963, 0.0, 3.1415927]),
    ("RoundaboutInterchange", "AE-Trumpet Roundabout.Prefab", 3, [-1.5707963, 0.0, 1.5707963]),
    ("Diamond", "AE-Full Diamond.Prefab", 4, [-3.1415927, -1.5707963, 0.0, 1.5707963]),
    ("ParcloA4", "AE-Twisted Diamond.Prefab", 4, [-3.1415927, -1.5707963, 0.0, 1.5707963]),
    ("Cloverleaf", "AE-Twisted Cloverleaf.Prefab", 4, [-3.1415927, -1.5707963, 0.0, 1.5707963]),
    ("FourLevelStack", "AE-Stack Interchange.Prefab", 4, [-3.1415927, -1.5707963, 0.0, 1.5707963]),
    ("Turbine", "AE-Turbine.Prefab", 4, [-3.1415927, -1.5707963, 0.0, 1.5707963])
]

GUID_MAP = {
    # Plan's specified GUIDs
    "96e8853f9369f2f4299ebc49dc52fb27": "TemplateRoadWidthClass.OneWay1Lane",
    "0264c97eb8fd2dd4581fb3aa6ebdf7e8": "TemplateRoadWidthClass.OneWay2Lane",
    "c1fc25a9a2356134b8fd8dc2ab6abe3c": "TemplateRoadWidthClass.OneWay3Lane",
    "f189f482bb9fa5e4a8d2dfe1ce8a96fe": "TemplateRoadWidthClass.OneWay4Lane",
    # Actual GUIDs in Admiral Electron
    "1850b919eb1f32a4b93dae83762d3eed": "TemplateRoadWidthClass.OneWay1Lane",
    "93ae62d6ac4e85042b4352cac42a53df": "TemplateRoadWidthClass.OneWay1Lane",
    "846d8a1f3901f4e4aad991782e487211": "TemplateRoadWidthClass.OneWay2Lane",
    "fc9e3c9276b3dd440ad2b7ec84c718bf": "TemplateRoadWidthClass.OneWay3Lane",
    "1b1f70b226f40814a8af826b40b27a4c": "TemplateRoadWidthClass.OneWay4Lane",
}

def clean_guid(g_str):
    if not g_str:
        return ""
    m = re.search(r'([0-9a-fA-F]{32})', g_str)
    return m.group(1).lower() if m else g_str.lower()

def wrap_ccw(angle):
    while angle <= -math.pi:
        angle += 2 * math.pi
    while angle > math.pi:
        angle -= 2 * math.pi
    return angle

def process_prefab(z, arch_name, prefab_file, req_legs, canon_azimuths):
    raw = z.open(prefab_file).read().decode('utf-8', errors='replace')
    clean = re.sub(r'\$fstrref:', '', raw)
    data = json.loads(clean)
    comps = data.get("components", {}).get("$rcontent", [])
    subnets = []
    for c in comps:
        if c.get("name") == "ObjectSubNets":
            subnets = c.get("m_SubNets", {}).get("$rcontent", [])
            break

    # 1. Collect all node positions and degrees
    node_degrees = {}
    node_positions = {}
    for seg_idx, it in enumerate(subnets):
        nx = it["m_NodeIndex"]["x"]
        ny = it["m_NodeIndex"]["y"]
        node_degrees[nx] = node_degrees.get(nx, 0) + 1
        node_degrees[ny] = node_degrees.get(ny, 0) + 1
        a = it["m_BezierCurve"]["a"]
        d = it["m_BezierCurve"]["d"]
        node_positions[nx] = [float(a["x"]), float(a["y"]), float(a["z"])]
        node_positions[ny] = [float(d["x"]), float(d["y"]), float(d["z"])]

    orig_indices = sorted(node_positions.keys())
    orig_to_compact = {orig: i for i, orig in enumerate(orig_indices)}

    # Find terminal ports (degree == 1)
    terminal_orig = [orig for orig in orig_indices if node_degrees.get(orig, 0) == 1]
    
    # 2. Group terminal ports into legs by angular proximity (within 0.35 rad)
    term_info = []
    for torig in terminal_orig:
        pos = node_positions[torig]
        az = math.atan2(pos[2], pos[0])
        r = math.hypot(pos[0], pos[2])
        term_info.append({
            "orig_node": torig,
            "compact_node": orig_to_compact[torig],
            "pos": pos,
            "az": az,
            "r": r
        })

    unassigned = list(term_info)
    legs = []
    while unassigned:
        base = unassigned.pop(0)
        group = [base]
        partner_idx = -1
        best_diff = 999.0
        for i, cand in enumerate(unassigned):
            diff = abs(wrap_ccw(cand["az"] - base["az"]))
            if diff < 0.35 and diff < best_diff:
                best_diff = diff
                partner_idx = i
        if partner_idx >= 0:
            group.append(unassigned.pop(partner_idx))
        legs.append(group)

    # Compute each leg's midpoint azimuth
    leg_data = []
    all_term_radii = []
    for g in legs:
        mx = sum(t["pos"][0] for t in g) / len(g)
        my = sum(t["pos"][1] for t in g) / len(g)
        mz = sum(t["pos"][2] for t in g) / len(g)
        az = math.atan2(mz, mx)
        for t in g:
            all_term_radii.append(t["r"])
        leg_data.append({
            "midpoint": [mx, my, mz],
            "azimuth": az,
            "terminals": g
        })

    # Match each leg to its closest canonical azimuth from blueprint table
    # Sort legs to match canonical_azimuths order
    matched_legs = [None] * len(canon_azimuths)
    for l in leg_data:
        best_c_idx = -1
        best_diff = 999.0
        for c_idx, c_az in enumerate(canon_azimuths):
            diff = abs(wrap_ccw(l["azimuth"] - c_az))
            if diff < best_diff and matched_legs[c_idx] is None:
                best_diff = diff
                best_c_idx = c_idx
        if best_c_idx >= 0:
            matched_legs[best_c_idx] = l
        else:
            # fallback find any empty slot
            for c_idx in range(len(canon_azimuths)):
                if matched_legs[c_idx] is None:
                    matched_legs[c_idx] = l
                    break

    leg_data = matched_legs

    # Shift Y so minimum node elevation is 0.0m
    min_y = min(p[1] for p in node_positions.values())

    # Build compact node array
    compact_nodes = []
    for orig in orig_indices:
        p = node_positions[orig]
        nx = round(p[0], 4)
        ny = round(p[1] - min_y, 4)
        nz = round(p[2], 4)
        compact_nodes.append([nx, ny, nz])

    canonical_radius = round(sum(all_term_radii) / len(all_term_radii), 2)

    # Process segments
    segments = []
    terminal_ports = []
    
    for seg_idx, it in enumerate(subnets):
        nx_orig = it["m_NodeIndex"]["x"]
        ny_orig = it["m_NodeIndex"]["y"]
        sx = orig_to_compact[nx_orig]
        sy = orig_to_compact[ny_orig]

        b = it["m_BezierCurve"]
        pa = [round(float(b["a"]["x"]), 4), round(float(b["a"]["y"]) - min_y, 4), round(float(b["a"]["z"]), 4)]
        pb = [round(float(b["b"]["x"]), 4), round(float(b["b"]["y"]) - min_y, 4), round(float(b["b"]["z"]), 4)]
        pc = [round(float(b["c"]["x"]), 4), round(float(b["c"]["y"]) - min_y, 4), round(float(b["c"]["z"]), 4)]
        pd = [round(float(b["d"]["x"]), 4), round(float(b["d"]["y"]) - min_y, 4), round(float(b["d"]["z"]), 4)]

        elev = [pa[1], pd[1]]

        upg = it.get("m_Upgrades")
        upg_content = upg.get("$rcontent", []) if isinstance(upg, dict) else []
        is_elevated = (11 in upg_content)

        guid_clean = clean_guid(it.get("m_NetPrefab"))
        width_cls = GUID_MAP.get(guid_clean, "TemplateRoadWidthClass.OneWay2Lane")

        segments.append({
            "start": sx,
            "end": sy,
            "a": pa,
            "b": pb,
            "c": pc,
            "d": pd,
            "elev": elev,
            "width": width_cls,
            "is_elevated": is_elevated
        })

        for canon_leg_idx, l in enumerate(leg_data):
            if l is None: continue
            for t in l["terminals"]:
                c_node = t["compact_node"]
                if sx == c_node:
                    terminal_ports.append({
                        "node": c_node,
                        "segment": seg_idx,
                        "is_inbound": True,
                        "canonical_leg": canon_leg_idx
                    })
                elif sy == c_node:
                    terminal_ports.append({
                        "node": c_node,
                        "segment": seg_idx,
                        "is_inbound": False,
                        "canonical_leg": canon_leg_idx
                    })

    terminal_ports.sort(key=lambda tp: (tp["canonical_leg"], not tp["is_inbound"]))

    return {
        "name": arch_name,
        "req_legs": req_legs,
        "canon_radius": canonical_radius,
        "canon_azimuths": canon_azimuths,
        "nodes": compact_nodes,
        "segments": segments,
        "terminal_ports": terminal_ports
    }

def main():
    with zipfile.ZipFile(cok_path, 'r') as z:
        results = []
        for arch, pf, req_legs, c_az in ARCHETYPES:
            print(f"Processing {arch}...")
            res = process_prefab(z, arch, pf, req_legs, c_az)
            results.append(res)

    print("Generating C# code...")
    out_path = r"c:\Users\liand\Documents\antigravity\procedural-interchanges\Core\Templates\WorkshopTemplateLibrary.cs"
    
    lines = []
    lines.append("// <auto-generated />")
    lines.append("// Generated by scratch/generate_templates_cs.py from Admiral Electron's System Interchanges")
    lines.append("using System;")
    lines.append("using System.Collections.Generic;")
    lines.append("using Unity.Mathematics;")
    lines.append("using ProceduralInterchanges.Core.Models;")
    lines.append("")
    lines.append("namespace ProceduralInterchanges.Core.Templates")
    lines.append("{")
    lines.append("    public static class WorkshopTemplateLibrary")
    lines.append("    {")
    lines.append("        private static readonly Dictionary<InterchangeArchetype, WorkshopTemplateDef> s_Templates = new Dictionary<InterchangeArchetype, WorkshopTemplateDef>();")
    lines.append("")
    lines.append("        static WorkshopTemplateLibrary()")
    lines.append("        {")
    for r in results:
        lines.append(f"            s_Templates[InterchangeArchetype.{r['name']}] = Create_{r['name']}();")
    lines.append("        }")
    lines.append("")
    lines.append("        public static WorkshopTemplateDef GetTemplate(InterchangeArchetype archetype)")
    lines.append("        {")
    lines.append("            if (s_Templates.TryGetValue(archetype, out var def)) return def;")
    lines.append("            if (s_Templates.TryGetValue(InterchangeArchetype.Diamond, out var fallback)) return fallback;")
    lines.append("            throw new InvalidOperationException($\"No template found for {archetype}\");")
    lines.append("        }")
    lines.append("")

    for r in results:
        lines.append(f"        private static WorkshopTemplateDef Create_{r['name']}()")
        lines.append("        {")
        lines.append("            return new WorkshopTemplateDef")
        lines.append("            {")
        lines.append(f"                Name = \"{r['name']}\",")
        lines.append(f"                Archetype = InterchangeArchetype.{r['name']},")
        lines.append(f"                RequiredLegs = {r['req_legs']},")
        lines.append(f"                CanonicalRadius = {r['canon_radius']}f,")
        az_strs = [f"{az}f" for az in r['canon_azimuths']]
        lines.append(f"                CanonicalLegAzimuthsRad = new float[] {{ {', '.join(az_strs)} }},")
        
        # Nodes
        lines.append(f"                Nodes = new float3[{len(r['nodes'])}]")
        lines.append("                {")
        for i, n in enumerate(r['nodes']):
            comma = "," if i < len(r['nodes']) - 1 else ""
            lines.append(f"                    new float3({n[0]}f, {n[1]}f, {n[2]}f){comma}")
        lines.append("                },")

        # Segments
        lines.append(f"                Segments = new TemplateSegmentDef[{len(r['segments'])}]")
        lines.append("                {")
        for i, s in enumerate(r['segments']):
            comma = "," if i < len(r['segments']) - 1 else ""
            is_elev = "true" if s['is_elevated'] else "false"
            lines.append("                    new TemplateSegmentDef")
            lines.append("                    {")
            lines.append(f"                        StartNodeIndex = {s['start']},")
            lines.append(f"                        EndNodeIndex = {s['end']},")
            lines.append(f"                        BezierA = new float3({s['a'][0]}f, {s['a'][1]}f, {s['a'][2]}f),")
            lines.append(f"                        BezierB = new float3({s['b'][0]}f, {s['b'][1]}f, {s['b'][2]}f),")
            lines.append(f"                        BezierC = new float3({s['c'][0]}f, {s['c'][1]}f, {s['c'][2]}f),")
            lines.append(f"                        BezierD = new float3({s['d'][0]}f, {s['d'][1]}f, {s['d'][2]}f),")
            lines.append(f"                        Elevation = new float2({s['elev'][0]}f, {s['elev'][1]}f),")
            lines.append(f"                        WidthClass = {s['width']},")
            lines.append(f"                        IsElevated = {is_elev}")
            lines.append(f"                    }}{comma}")
        lines.append("                },")

        # Terminal ports
        lines.append(f"                TerminalPorts = new TemplateTerminalPort[{len(r['terminal_ports'])}]")
        lines.append("                {")
        for i, tp in enumerate(r['terminal_ports']):
            comma = "," if i < len(r['terminal_ports']) - 1 else ""
            is_inb = "true" if tp['is_inbound'] else "false"
            lines.append("                    new TemplateTerminalPort")
            lines.append("                    {")
            lines.append(f"                        NodeIndex = {tp['node']},")
            lines.append(f"                        SegmentIndex = {tp['segment']},")
            lines.append(f"                        IsInbound = {is_inb},")
            lines.append(f"                        CanonicalLegIndex = {tp['canonical_leg']}")
            lines.append(f"                    }}{comma}")
        lines.append("                }")

        lines.append("            };")
        lines.append("        }")
        lines.append("")

    lines.append("    }")
    lines.append("}")
    lines.append("")

    with open(out_path, 'w', encoding='utf-8') as f:
        f.write("\n".join(lines))
    print(f"Successfully generated {out_path} ({os.path.getsize(out_path)} bytes)")

if __name__ == '__main__':
    main()
