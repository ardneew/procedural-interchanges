# Procedural Interchanges

A procedural interchange generator mod for **Cities: Skylines II**.

## Overview

Procedural Interchanges is an engine-level mod for Cities: Skylines II designed to generate parametric, mathematically sound, and geometrically clean road interchanges directly in-game.

### Key Capabilities (Planned & Under Development)
- **Parametric Interchange Templates**: Standard diamond, trumpet, cloverleaf, stack, roundabout, and single-point urban interchanges (SPUI).
- **Procedural Curve & Grade Generation**: Smooth clothoid/cubic spline transitions, elevation clearance management, and grade adherence.
- **In-Game Tool & Overlay**: Interactive layout tool with real-time preview, control points, and lane connection configuration.
- **Custom React/Gameface UI**: Seamlessly integrated with CS2's native UI system.

## Project Structure

```
procedural-interchanges/
├── Mod.cs                      # Mod entry point implementing IMod
├── Setting.cs                  # In-game settings and keybindings
├── ProceduralInterchanges.csproj# C# project definition importing CS2 Mod toolchain
├── Properties/
│   ├── PublishConfiguration.xml# Paradox Mods metadata
│   └── PublishProfiles/        # Deployment & publishing profiles
└── ui/                         # Coherent Gameface / React UI module
    ├── src/                    # TypeScript / TSX components
    ├── mod.json                # UI module manifest (ID: ProceduralInterchanges)
    ├── package.json
    └── webpack.config.js
```

## Development & Building

### Prerequisites
- Cities: Skylines II (tested against version 1.6.2f1)
- CS2 Modding Toolchain (installed via *Options → Modding* in-game)
- .NET 8 SDK
- Node.js 18+ and npm

### Building the Mod

1. **Build the C# assembly**:
   ```bash
   dotnet build
   ```
   This compiles the mod and automatically deploys the `.dll` and assets to `%CSII_LOCALMODSPATH%\ProceduralInterchanges`.

2. **Build the UI module**:
   ```bash
   cd ui
   npm install
   npm run build
   ```
   This bundles the React frontend with Webpack and deploys it to `%CSII_USERDATAPATH%\Mods\ProceduralInterchanges`.

## License

MIT License.
