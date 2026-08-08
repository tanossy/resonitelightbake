# ResoniteSDK Bakery Lightmap Overlay

日本語版: [README.ja.md](README.ja.md)

An overlay for the official [`Resonite.UnitySDK`](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK)
(Yellow-Dog-Man) that drops cleanly on top of it — mainly adding a pipeline for importing
Unity's baked lightmaps into Resonite (which has no baked-GI system of its own), plus a
handful of related fixes and small extra converters. Changes to official SDK files are kept
to the smallest possible diff; almost all new logic and tunable values live in new,
standalone files (see "Design principle" below).

This repo holds the readable source tree. A ready-to-import `.unitypackage` build is
attached to each [Release](../../releases).

**Requirements:** a Unity project with the official
[`Resonite.UnitySDK`](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK) already set up —
this is an overlay, not a standalone SDK, and does nothing on its own.

License: [MIT](LICENSE) (same license as the official SDK it overlays)
Target branch: `feature/water-and-audio-presets`
Fork point: commit `5eea4b03` (right after PR #117 merged)

## ⚠️ Updating the official SDK will wipe this overlay

The official SDK's own in-panel instructions say: *"Before installing a new version of the
SDK, please delete the ResoniteSDK folder first!"* Since this overlay's files live inside
that same `Assets/ResoniteSDK/` folder (both new files and modified official ones), following
that official update process **deletes this overlay entirely** — not a partial/silent
breakage, a full wipe. You need to re-import this overlay's package again afterward to get
its functionality back.

There's a second, subtler risk: the files this overlay modifies (see "[official, modified]"
in the folder structure below) are shipped as complete replacement files, not as patches. If
the official SDK has changed those same files since this overlay's fork point, re-applying
the overlay on top of a newer official SDK will **revert them to this fork point's content**,
silently discarding whatever the official update changed there. Before re-applying this
overlay after an official update, diff the official files it touches against this repo's
fork-point commit to check whether anything upstream needs to be manually re-merged.

*(Status as of 2026-08-08: this fork point, `5eea4b03`, is exactly even with
`Yellow-Dog-Man/Resonite.UnitySDK`'s `main` branch tip — verified live against the GitHub
API, zero commits of drift, no open PRs pending. That will very likely change over time, so
don't assume it's still current just because it says so here — check for yourself.)*

## Installation

1. Set up the latest `Resonite.UnitySDK` in your Unity project as usual
2. Download the `.unitypackage` from the [latest Release](../../releases/latest), then
   double-click it or import it via `Assets > Import Package > Custom Package...`
   (or copy `Assets/ResoniteSDK/` from this repo directly into your project instead, if
   you'd rather track the source via git)
3. Import with everything checked
4. Once compilation finishes, connect as usual from `Resonite SDK > Open Resonite SDK Manager`

## Usage

### Normal scene send

1. Open `Resonite SDK > Open Resonite SDK Manager`
2. Connect via AutoDiscovery (recommended) or Manual (specify a port)
3. Check the three toggles as needed:
   - **Convert Skybox** — whether to also send the skybox (Material/ReflectionProbe)
   - **Force Refresh Generated Lightmaps** — whether to force-regenerate the generated
     lightmap-variant files every time
   - **Send Tonemap Compensation (experimental)** — whether to apply the tonemap
     approximation to material colors / Reflection Probe intensity (see "Tonemap
     Compensation" below; default ON)
4. Click `Send Current Scene`. A single `Unity Import` slot is created directly under World
   Root (an existing one is deleted and rebuilt, so re-sending never produces duplicates),
   and everything is placed under it
5. `AssetConverter.cs` enforces a 60-second timeout on asset conversion. If you hit a
   timeout or WebSocket disconnect, use `Reset conversion state` in the Debug Tools window
   and resend

### Debugging / partial sends

Open `Resonite SDK > Open Debug Tools` (requires `Resonite SDK Manager` to already be
connected):

- **Send Meshes Only / Send Materials Only / Send Lightmaps Only** — resend just one
  category instead of redoing a full scene send (internally scoped via
  `ConversionPassState.ActivePass`)
- **Retry Missing Asset URLs** — use when only the asset upload step failed while
  everything else succeeded
- **Log Messages JSON** — log sent messages as JSON (debug only, default OFF)
- **Cleanup converters in the scene / Cleanup Resonite Components in the scene** — bulk-
  remove conversion helper components from the scene
- **Reset conversion state** — rebuilds `SceneConverter` and also runs the two cleanups
  above. The go-to recovery step when the connection gets into a bad state

### Objects excluded automatically

The following are automatically excluded from the set of scene roots sent
(`SceneRootFilter.cs`):

- Any root carrying a `UnityEngine.Camera` (Resonite has its own camera system, so
  Unity's is never needed)
- Any root in a Missing Prefab state (e.g. the leftover `VRCWorld` root when importing a
  VRChat world — there's no source asset left to convert)

Nested child objects are not currently handled (only root-level objects are filtered).

### Baked lightmap → Resonite import

A pipeline that approximates Bakery (if installed) or Unity's built-in Progressive
Lightmapper baked lightmaps in Resonite. Operational notes:

- After baking, just `Send Current Scene` (or `Send Lightmaps Only`) again. With `Force
  Refresh Generated Lightmaps` ON, the diff files are rebuilt every time
- Resonite has no baked-GI (indirect lighting) system of its own, so
  `BakedLightmapStandardConverter` approximates it by baking the lightmap data into the
  material's `SecondaryAlbedoTexture` (multiply). Real Resonite lights (via
  `LightConverter`) stay active at the same time by design (see "Send-time brightness/color
  tuning" below)
- If brightness or metallic response doesn't match on the real client, adjust the static
  values in `LightTuning.cs` / `BakedLightmapStandardConverter.cs` (see below)

### Send-time brightness/color tuning (live-tuned values)

There's a real gap between how a scene looks in the Unity Editor and how it looks on a
live Resonite client. The following static values were tuned iteratively against a running
client. To change them, edit the field directly in code (there's no in-panel UI for this):

| File | Field | Current value | Meaning |
|---|---|---|---|
| `LightTuning.cs` | `IntensityMultiplier` | 1.8 | Multiplier applied to every light's Unity `Light.intensity` |
| `LightTuning.cs` | `WhiteBalanceShift` | 0.7 | Blends light color toward white at send time (0 = original color, 1 = pure white) |
| `LightmapDecoder.cs` | `RangeScale` | 1.1 | Pre-decode gain applied to baked lightmap data |
| `LightmapDecoder.cs` | `ColorSaturationCompensation` | 0.6 | Saturation reduction applied to the baked lightmap data itself (0–1) |
| `BakedLightmapStandardConverter.cs` | `SmoothnessCompensation` | 0.05 | Multiplier on scalar Smoothness (only affects materials with no MetallicMap) |
| `BakedLightmapStandardConverter.cs` | `MetallicCompensation` | 0.0 | Multiplier on scalar Metallic (same scope as above) |
| `BakedLightmapStandardConverter.cs` | `AdditiveFillStrength` | 0.0 | Strength of the additive fill from baked lightmap data (currently disabled — multiply-only) |

**Materials with a MetallicMap** (couch01/lights/tables/wall_door in the reference scene)
ignore the scalar coefficients entirely (the `PBSMultiUV.shader`'s `_METALLICMAP` branch
reads R=Metallic/A=Smoothness straight from the texture). Those four materials had their
textures re-baked directly at Metallic=0, Smoothness×8/9 (`*_MetallicSmoothness.png`,
DXT5 with alpha). Full history in
`C:/urd/wiki/concepts/resonite/dev-recipes/2026-08-08_rezo_con_baked_lightmap_metallic_compensation_values.md`.

### Tonemap Compensation (experimental)

Reproduces Unity PPv2's Neutral Tonemapper-style color compression and applies it (to
material colors — AlbedoColor/EmissiveColor, saturation only — and to Reflection Probe
intensity). Resonite's renderer (Renderite) does no camera-side post-process tonemapping,
so the same HDR values look glarier/blown-out in Resonite than they did tonemapped in
Unity. Toggle it from the panel's "Send Tonemap Compensation" checkbox (default ON).

## What this does (feature summary)

### 1. Baked lightmap import into Resonite (this package's headline feature)

- Imports Bakery (if installed) or Unity's Progressive Lightmapper bake results, approximating
  Resonite's lack of a baked-GI system via a `SecondaryAlbedoTexture` multiply
  (`LightmapDecoder.cs`/`LightmapMaterialCache.cs`/`BakedLightmapStandardConverter.cs`/
  `BakedLightmapStandard.shader`/`DirectionalLightmapBaker.cs`, all new)
- Lightmap preview textures are capped at 256px on send (restored from an earlier 64px
  stopgap; tunable via `LightmapDecoder.MaxPreviewTextureSize`)
- Generated lightmap textures (under `Assets/ResoniteSDK/Generated/LightmapVariants/`) are
  sent as raw pixel data instead of via file import (`Texture2DConverter.cs`) — file-path
  import wasn't always picking up regenerated diffs
- Skips generating/uploading the desaturated "additive fill" companion texture entirely
  when `AdditiveFillStrength` is 0 (the current default) — see "Structural notes" below

### 2. Tonemap Compensation (experimental, new)

- `PPv2ToneMapMath.cs`: reproduces Unity `com.unity.postprocessing@3.4.0`'s grading
  pipeline (LogC-space Contrast/WhiteBalance/Saturation/NeutralTonemap)
- `ColorGradingApproximation.cs`: the material-color application point (saturation only,
  `MaterialGradingEnabled`)
- `ReflectionProbeConverter.cs`: application to Reflection Probe intensity
  (`ComputeReflectionProbeCompensationFactor()`)
- Implementation lives in its own `Assets/ResoniteSDK/ToneMapCompensation/` folder,
  separate from the main SDK

### 3. Send-time light tuning (new, already externalized)

- `LightTuning.cs`: consolidates the overall light brightness multiplier and white-shift
  blend into one file (`LightConverter.cs` itself is untouched apart from a 2-line call-out)

### 4. Scene-import hygiene (new, already externalized)

- **Camera / Missing Prefab exclusion**: `SceneRootFilter.cs` — automatically excludes
  Unity's own Camera and any Missing Prefab root (e.g. left over from importing a VRChat
  world) from the send
- **ID-collision-proof allocation**: `GlobalIdAllocator.cs` — switched to a process-wide,
  monotonically increasing static counter. Guarantees the same ID string can never be
  generated twice for the lifetime of the Unity Editor process, even if `SceneConverter` is
  rebuilt with an unchanged `UniqueSessionId` across a reconnect (fixes an `"ID '...'
  already in use"` FATAL ERROR)
- **Duplicate-tree prevention**: `ImportRootSlotHelper.cs` — on reconnect, checks for an
  existing `Unity Import` slot directly under World Root and deletes/rebuilds it instead of
  leaving a duplicate
- **Asset-identity bug fix**: `AssetConversionManager.cs` — switched the identity key from
  reference equality to GUID + local file ID. Fixes a real bug where multiple sub-assets
  inside the same .fbx (Cube/curtain/Cylinder, etc.) were incorrectly treated as the same
  asset, causing meshes to get mixed up
- **60-second timeout**: `AssetConverter.cs` — throws if an asset conversion hangs, so it's
  detectable (mitigation for a known ResoniteLink WebSocket desync issue)

### 5. Panel cleanup (new file, no need to touch upstream)

- `ResoniteLinkWindow.cs` (main panel) trimmed down to just connect / Send Current Scene /
  Realtime Mode plus 3 toggles
- Partial-send testing, cleanup, and state-reset tools moved to a new
  `ResoniteSDKDebugWindow.cs` (`Resonite SDK > Open Debug Tools`). It only calls public
  methods already on `ResoniteLinkWindow` — no duplicated logic

### 6. Automatic water-material detection (new)

- `WaterPanningConverter.cs`: detects any custom shader whose name contains "water" and
  converts it to the community-standard water pattern seen in real Resonite worlds
  (PBS_Metallic + Panner2D UV scroll)

### 7. Audio effect conversion (new, not yet verified on a live client)

- `AudioEffectConverter.cs`: converts Unity's `AudioReverbFilter` to Resonite's
  `AudioZitaReverb`. Parameter mapping is inferred from the public Zita-Rev1 algorithm
  (Resonite's actual in-world audio behavior has not been verified — only compile
  correctness and mapping-logic soundness have been checked)

### 8. Post-processing conversion (new)

- `PostProcessingConverter.cs`: converts a PPv2 Global Volume to Resonite's
  `PostProcessingSettings` (only 5 fields: MotionBlur/Bloom/AO intensity, SSR on/off, AA
  method)

### 9. Particle system conversion bug fixes

- `ParticleSystemConverter.cs`: fixes for BoxShell/BoxEdge shapes producing no emitter at
  all, Cone shape's Height being miscopied, Over Lifetime modules not converting, the
  Emission-enabled gate being missed, and legacy particle shaders (Alpha Blended/Additive)
  not being handled

### 10. Other bug fixes

- `MeshColliderConverter.cs`/`MeshRendererConverter.cs`/`StandardBaseConverter.cs`, etc.:
  added guards so `Send Meshes/Materials/Lightmaps Only` partial sends don't accidentally
  send out-of-scope data (meshes, source textures, etc.)
- `MeshRendererConverter.cs`: logs a warning when a renderer ends up with a mixed outcome
  (some material slots lightmap-eligible, some not — non-Standard shader, etc.)
- `SkyboxConverter.cs`: fixed a `MissingReferenceException` from touching Unity's "fake
  null" references after a domain reload. Spherical-harmonics (SH2) conversion is disabled
  by default (`ConvertSphericalHarmonics = false`) since it isn't serializable
- `Texture2DConverter.cs`/`StandardConverter.cs`/`UnlitConverter.cs`: assorted fallback
  handling for missing properties and legacy-shader support tied to items 1 and 9 above

## Design principle: minimize the diff against official files

With open-sourcing in mind, the guiding rule for this work has been: **keep changes to
official SDK files down to a handful of call-out lines, and externalize all real logic and
tunable values into new files.**

Example (`LightConverter.cs`):
```csharp
// The official file's only change is these 2 lines
resonite.Intensity = LightTuning.ApplyIntensity(unity.intensity);
resonite.Color = new ColorX(LightTuning.ApplyColor(unity.color));
```
The actual multiplier/logic lives entirely in the new `LightTuning.cs` in the same folder.

Already applied: `SceneConverter.cs` → `SceneRootFilter.cs` / `GlobalIdAllocator.cs` /
`ImportRootSlotHelper.cs`; `LightConverter.cs` → `LightTuning.cs`.

**Not yet externalized** (official files that still carry a larger diff):
`AssetConverter.cs` (the 60-second timeout mechanism), `AssetConversionManager.cs` (the
asset-identity logic), `ConversionPassState.cs` (a genuinely new file — the partial-send
pass enum doesn't exist upstream so there's nothing to externalize away from), `StandardConverter.cs`/
`StandardSpecularConverter.cs` (a couple of guard lines each), `MeshRendererConverter.cs`/
`MeshColliderConverter.cs` (partial-send guards).

## Structural notes (2026-08-08 review, lightbake feature only)

A dedicated review of the lightbake-specific files found one concrete piece of wasted
work, since fixed:

- With `AdditiveFillStrength = 0` (the current tuned default), the additive-fill mechanism
  contributes nothing — `SecondaryEmissiveColor` is pure black regardless of what texture
  feeds it. Before the fix, `LightmapMaterialCache.cs` unconditionally decoded a
  desaturated "gray" companion texture for every lightmap on every send (a second full
  RenderTexture blit + resize + PNG encode + AssetDatabase import pass), and
  `BakedLightmapStandardConverter.cs` unconditionally uploaded it to Resonite over
  ResoniteLink — all for zero visual effect. Both are now gated behind
  `AdditiveFillStrength > 0`; raising the value again re-enables the full path exactly as
  before. Verified live: sends now produce zero `LMTex_N_gray` textures (previously one per
  lightmap).
- `DirectionalLightmapBaker.cs` (~767 lines) is a legitimate, well-documented opt-in
  feature for directional lightmap bakes, but is currently fully dormant for this project
  since the reference scene uses a NonDirectional bake. Not flagged as waste — just an
  observation for anyone auditing code size.

## Folder structure

```
Assets/ResoniteSDK/
├── ToneMapCompensation/                      ← fully separate from the core SDK (new folder)
│   ├── PPv2ToneMapMath.cs                    Reproduces the PPv2 grading pipeline
│   └── ToneMapCompensationState.cs           Shared static state the panel toggle writes to
│
├── Editor/
│   ├── SceneConverter.cs                     [official, modified] conversion orchestrator
│   ├── SceneRootFilter.cs                    ★new Camera/Missing Prefab exclusion logic
│   ├── GlobalIdAllocator.cs                  ★new process-wide monotonic ID allocator
│   ├── ImportRootSlotHelper.cs               ★new existing-"Unity Import"-slot lookup
│   ├── SkyboxConverter.cs                    [official, modified] fake-null fix, SH2 default-off
│   ├── AssetConversionManager.cs             [official, modified] asset identity (GUID+localId)
│   └── Managers/
│       ├── ResoniteLinkWindow.cs             [official, modified] main panel (connect/send/3 toggles)
│       └── ResoniteSDKDebugWindow.cs         ★new debug panel (partial send / cleanup)
│
├── AssetConverters/
│   ├── AssetConverter.cs                     [official, modified] 60-second timeout
│   └── Texture2DConverter.cs                 [official, modified] raw-pixel send for generated lightmaps
│
├── ConversionPassState.cs                    ★new partial-send passes (Full/MeshesOnly/MaterialsOnly/LightmapsOnly)
│
├── ComponentConverters/Unity Core/
│   ├── Audio/
│   │   └── AudioEffectConverter.cs           ★new AudioReverbFilter → AudioZitaReverb (unverified live)
│   ├── Colliders/
│   │   └── MeshColliderConverter.cs          [official, modified] partial-send guard
│   └── Rendering/
│       ├── LightConverter.cs                 [official, modified (2 lines)] → calls LightTuning.cs
│       ├── LightTuning.cs                    ★new brightness multiplier / white-shift blend
│       ├── LightmapDecoder.cs                ★new bake-data decode / gain / saturation adjustment
│       ├── LightmapMaterialCache.cs          ★new per-lightmap material-variant management
│       ├── LightmapDecode.shader             ★new decode shader
│       ├── DirectionalLightmapBaker.cs       ★new directional-lightmap baking
│       ├── RawPassthroughBlit.shader         ★new
│       ├── Uv2NormalPatch.shader             ★new
│       ├── MeshRendererConverter.cs          [official, modified] lightmap-variant swap + partial-send guard
│       ├── SkinnedMeshRendererConverter.cs   [official, modified (3 lines)]
│       ├── ReflectionProbeConverter.cs       [official, modified] Tonemap Compensation applied
│       ├── ParticleSystemConverter.cs        [official, modified] assorted bug fixes (see item 9 above)
│       └── PostProcessingConverter.cs        ★new PPv2 GlobalVolume → PostProcessingSettings
│
├── MaterialConverters/
│   ├── ColorGradingApproximation.cs          ★new Tonemap Compensation's material-color entry point
│   ├── Custom/
│   │   └── WaterPanningConverter.cs          ★new "water"-shader auto-detect → Panner2D
│   ├── PBS/
│   │   ├── StandardBaseConverter.cs          [official, modified] missing-property fallback + partial-send guard
│   │   ├── StandardConverter.cs              [official, modified] same
│   │   ├── StandardSpecularConverter.cs      [official, modified] same
│   │   ├── BakedLightmapStandard.shader      ★new baked-lightmap marker material
│   │   └── BakedLightmapStandardConverter.cs ★new baked-lightmap conversion (tunable values live here)
│   └── Unlit/
│       └── UnlitConverter.cs                 [official, modified] legacy particle shader support
```

Legend: `★new` = file that doesn't exist in the official SDK. `[official, modified]` =
an official file we edit (aiming for the smallest diff possible; files not yet
externalized may still carry a larger one).

## Not included

- `Assets/ResoniteSDK/Generated/` (scene-specific generated output) is deliberately
  excluded
- `Assets/Editor/LightmapTestHarness.cs` and friends (the file-driven real-machine test
  automation harness — its own header comment explicitly says "test-only, not part of the
  SDK fork")

## Known open issues

- ResoniteLink.dll occasionally drops the WebSocket mid-send in an unsynchronized way (a
  candidate for an upstream issue report; `AssetConverter.cs`'s 60-second timeout only
  detects this, it doesn't fix the root cause)
- `AudioEffectConverter.cs` has not been verified against real in-world audio in Resonite
- `LightTuning.WhiteBalanceShift` blends every light toward white uniformly regardless of
  its original hue, so a scene with multiple differently-colored lights would have those
  color differences washed out evenly (this SDK's reference scene uses a single warm color
  scheme, so multi-color support hasn't been built yet)
- Whether the MetallicMap's alpha channel affects Albedo visibility on a real Resonite
  client (user-reported, unconfirmed) is recorded as an open question in
  `2026-08-08_rezo_con_baked_lightmap_metallic_compensation_values.md`

## License

[MIT](LICENSE) — the same license as the official `Resonite.UnitySDK` this overlays.

Generated: 2026-08-08
