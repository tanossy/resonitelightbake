# ResoniteSDK Bakery Lightmap Overlay

An overlay for the official [`Resonite.UnitySDK`](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK)
(Yellow-Dog-Man) — mainly a pipeline for importing Unity's baked lightmaps into Resonite.
`Resonite.UnitySDK`（Yellow-Dog-Man公式）に重ねて使うオーバーレイ。Unityのベイク済み
ライトマップをResoniteへ取り込むパイプラインが中核機能です。

Jump to: [English](#english) | [日本語](#日本語) | [Folder structure / フォルダ構成](#folder-structure--フォルダ構成)

**Requirements / 前提条件:** a Unity project with the official `Resonite.UnitySDK` already
set up — this is an overlay, not a standalone SDK, and does nothing on its own. /
公式`Resonite.UnitySDK`が既にセットアップ済みのUnityプロジェクトが必要です（単独では動作しません）。

License / ライセンス: [MIT](LICENSE)
Target branch / 対象ブランチ: `feature/water-and-audio-presets`
Fork point / フォーク元コミット: `5eea4b03` (right after PR #117 merged / PR #117マージ直後)

### Quickstart / クイックスタート

1. Get the official `Resonite.UnitySDK` working in your Unity project /
   公式`Resonite.UnitySDK`をUnityプロジェクトで使えるようにする
2. Install the `ResoniteLightbake` UnityPackage /
   `ResoniteLightbake`のUnityPackageを入れる
3. Launch Resonite and get ResoniteLink ready to connect /
   Resoniteを起動し、ResoniteLinkが使えるように設定する
4. Click **Bake & Send** in the UnitySDK panel (`Resonite SDK > Lightmap Pipeline`) /
   UnitySDKのパネル（`Resonite SDK > Lightmap Pipeline`）で **Bake & Send** を押す

---

## English

An overlay for the official [`Resonite.UnitySDK`](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK)
(Yellow-Dog-Man) that drops cleanly on top of it — mainly adding a pipeline for importing
Unity's baked lightmaps into Resonite (which has no baked-GI system of its own), plus a
handful of related fixes and small extra converters. Changes to official SDK files are kept
to the smallest possible diff; almost all new logic and tunable values live in new,
standalone files (see "Design principle" below).

This repo holds the readable source tree. A ready-to-import `.unitypackage` build is
attached to each [Release](../../releases).

### ⚠️ Updating the official SDK will wipe this overlay

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

### Installation

1. Set up the latest `Resonite.UnitySDK` in your Unity project as usual
2. Download the `.unitypackage` from the [latest Release](../../releases/latest), then
   double-click it or import it via `Assets > Import Package > Custom Package...`
   (or copy `Assets/ResoniteSDK/` from this repo directly into your project instead, if
   you'd rather track the source via git)
3. Import with everything checked
4. Once compilation finishes, connect as usual from `Resonite SDK > Open Resonite SDK Manager`

### Usage

#### Normal scene send

1. Open `Resonite SDK > Open Resonite SDK Manager`
2. Connect via AutoDiscovery (recommended) or Manual (specify a port)
3. Check the toggle as needed:
   - **Convert Skybox** — whether to also send the skybox (Material/ReflectionProbe); the
     only one of these that's actually part of vanilla Resonite.UnitySDK
   - Force Refresh Generated Lightmaps and Send Tonemap Compensation now live in the
     **Lightmap Pipeline** panel's "Send-Time Options" section instead (`Resonite SDK >
     Lightmap Pipeline`; applies to either baker — see "Tonemap Compensation" below)
4. Click `Send Current Scene`. A single `Unity Import` slot is created directly under World
   Root (an existing one is deleted and rebuilt, so re-sending never produces duplicates),
   and everything is placed under it
5. `AssetConverter.cs` enforces a 60-second timeout on asset conversion. If you hit a
   timeout or WebSocket disconnect, use `Reset conversion state` in the Debug Tools window
   and resend

#### Debugging / partial sends

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
- **Clear Generated Lightmap Variants** — deletes the whole
  `Assets/ResoniteSDK/Generated/LightmapVariants` folder (variant materials and decoded
  lightmap PNGs alike). Fully reproducible by re-running scene conversion, so always safe
  to run; doesn't need a ResoniteLink connection

#### Objects excluded automatically

The following are automatically excluded from the set of scene roots sent
(`SceneRootFilter.cs`):

- Any root carrying a `UnityEngine.Camera` (Resonite has its own camera system, so
  Unity's is never needed)
- Any root in a Missing Prefab state (e.g. the leftover `VRCWorld` root when importing a
  VRChat world — there's no source asset left to convert)
- Any root whose hierarchy (root or any descendant, active or inactive) contains a
  MonoBehaviour with an unresolvable script — Unity surfaces this as a literal `null`
  entry in `GetComponentsInChildren<Component>(true)`, the same idiom Unity's own
  "select prefabs with missing scripts" tooling uses. Catches VRChat-derived leftovers
  that aren't broken prefab instances themselves (e.g. a non-broken `Easy Mirror` prefab
  whose nested "UI" child carries an unresolvable VRChat SDK script) — this check walks
  the whole hierarchy rather than just the root, since not every case sits at the top.

#### Baked lightmap → Resonite import

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

#### Send-time brightness/color tuning (live-tuned values)

There's a real gap between how a scene looks in the Unity Editor and how it looks on a
live Resonite client. The following static values were tuned iteratively against a running
client. `IntensityCeiling`/`WhiteBalanceShift` have sliders in the **Lightmap Pipeline**
panel's "Send-Time Light Tuning" section, and `RangeScale`/`ColorSaturationCompensation`
have sliders in that same panel's "Baked Lightmap Exposure" section (`Resonite SDK >
Lightmap Pipeline`, both sections apply to either baker — see that panel's own section
below); the rest still require a code edit:

| File | Field | Current value | Meaning |
|---|---|---|---|
| `LightTuning.cs` | `IntensityCeiling` | 0.9 | Target intensity for the scene's single brightest light; every light is scaled by the ratio needed to put the brightest one exactly here (self-normalizing per scene, not a fixed multiplier) |
| `LightTuning.cs` | `WhiteBalanceShift` | 0.55 | Blends light color toward white at send time (0 = original color, 1 = pure white) |
| `LightmapDecoder.cs` | `RangeScale` | 1.1 | Pre-decode gain applied to baked lightmap data |
| `LightmapDecoder.cs` | `ColorSaturationCompensation` | 0.6 | Saturation reduction applied to the baked lightmap data itself (0–1) |
| `BakedLightmapStandardConverter.cs` | `SmoothnessCompensation` | 0.05 | Multiplier on scalar Smoothness (only affects materials with no MetallicMap) |
| `BakedLightmapStandardConverter.cs` | `MetallicCompensation` | 0.0 | Multiplier on scalar Metallic (same scope as above) |
| `BakedLightmapStandardConverter.cs` | `AdditiveFillStrength` | 0.0 | Strength of the additive fill from baked lightmap data (currently disabled — multiply-only) |

**Materials with a MetallicMap** (couch01/lights/tables/wall_door in the reference scene)
ignore the scalar coefficients entirely (the `PBSMultiUV.shader`'s `_METALLICMAP` branch
reads R=Metallic/A=Smoothness straight from the texture). Those four materials had their
textures re-baked directly at Metallic=0, Smoothness×8/9 (`*_MetallicSmoothness.png`,
DXT5 with alpha).

#### Tonemap Compensation (experimental)

Reproduces Unity PPv2's Neutral Tonemapper-style color compression and applies it (to
material colors — AlbedoColor/EmissiveColor, saturation only — and to Reflection Probe
intensity). Resonite's renderer (Renderite) does no camera-side post-process tonemapping,
so the same HDR values look glarier/blown-out in Resonite than they did tonemapped in
Unity. Toggle it from the **Lightmap Pipeline** panel's "Send Tonemap Compensation" checkbox
in the "Send-Time Options" section (default ON).

**Currently only the Reflection Probe half has a visible effect.** The material-color half
(`ColorGradingApproximation.Apply`) applies saturation only, and saturation adjustment is a
mathematical no-op on achromatic (white/gray, r=g=b) colors — verified live: AlbedoColor
stayed exactly `(1,1,1)` with the toggle on. Since almost every material in the reference
scene has `_Color = white` (the actual color comes from its texture, not this field), this
half of the feature currently does nothing observable there. It would matter for a material
with a genuinely tinted `_Color`, or if `ColorGradingApproximation.Apply()` were switched
back to full grading (`ApplyGrading()` instead of `ApplySaturationOnly()` — see that file's
comments for why full grading was tried and reverted).

### What this does (feature summary)

1. **Baked lightmap import into Resonite** (this package's headline feature) — imports
   Bakery (if installed) or Unity's Progressive Lightmapper bake results, approximating
   Resonite's lack of a baked-GI system via a `SecondaryAlbedoTexture` multiply
   (`LightmapDecoder.cs`/`LightmapMaterialCache.cs`/`BakedLightmapStandardConverter.cs`/
   `BakedLightmapStandard.shader`/`DirectionalLightmapBaker.cs`, all new). Lightmap preview
   textures are capped at 256px on send (tunable via `LightmapDecoder.MaxPreviewTextureSize`).
   Generated lightmap textures are sent as raw pixel data instead of via file import
   (`Texture2DConverter.cs`). Skips generating/uploading the desaturated "additive fill"
   companion texture entirely when `AdditiveFillStrength` is 0 (the current default) — see
   "Structural notes" below.
2. **Tonemap Compensation** (experimental, new) — `PPv2ToneMapMath.cs` reproduces Unity
   `com.unity.postprocessing@3.4.0`'s grading pipeline (LogC-space Contrast/WhiteBalance/
   Saturation/NeutralTonemap); `ColorGradingApproximation.cs` is the material-color
   application point; `ReflectionProbeConverter.cs` applies it to Reflection Probe intensity.
   Lives in its own `Assets/ResoniteSDK/ToneMapCompensation/` folder.
3. **Send-time light tuning** (new, already externalized) — `LightTuning.cs` consolidates
   the overall light brightness multiplier and white-shift blend into one file
   (`LightConverter.cs` itself is untouched apart from a 2-line call-out).
4. **Scene-import hygiene** (new, already externalized):
   - Camera / Missing Prefab exclusion — `SceneRootFilter.cs`
   - ID-collision-proof allocation — `GlobalIdAllocator.cs`, a process-wide, monotonically
     increasing static counter (fixes an `"ID '...' already in use"` FATAL ERROR)
   - Duplicate-tree prevention on reconnect — `ImportRootSlotHelper.cs`
   - Asset-identity bug fix — `AssetConversionManager.cs`, switched the identity key from
     reference equality to GUID + local file ID (fixes multiple sub-assets inside the same
     .fbx getting mixed up)
   - 60-second timeout — `AssetConverter.cs`, throws if an asset conversion hangs
5. **Panel cleanup** (new file, no need to touch upstream) — `ResoniteLinkWindow.cs` (main
   panel) trimmed to just connect / Send Current Scene / Realtime Mode plus 3 toggles;
   partial-send/cleanup/reset tools moved to a new `ResoniteSDKDebugWindow.cs`
   (`Resonite SDK > Open Debug Tools`).
6. **Automatic water-material detection** (new) — `WaterPanningConverter.cs` detects any
   custom shader whose name contains "water" and converts it to the community-standard
   water pattern (PBS_Metallic + Panner2D UV scroll).
7. **Audio effect conversion** (new, not yet verified on a live client) —
   `AudioEffectConverter.cs` converts Unity's `AudioReverbFilter` to Resonite's
   `AudioZitaReverb`, mapped from the public Zita-Rev1 algorithm.
8. **Post-processing conversion** (new) — `PostProcessingConverter.cs` converts a PPv2
   Global Volume to Resonite's `PostProcessingSettings` (5 fields only).
9. **Particle system conversion bug fixes** — `ParticleSystemConverter.cs`: BoxShell/BoxEdge
   shapes producing no emitter, Cone shape's Height being miscopied, Over Lifetime modules
   not converting, the Emission-enabled gate being missed, legacy particle shaders not
   handled.
10. **Other bug fixes** — partial-send guards in `MeshColliderConverter.cs`/
    `MeshRendererConverter.cs`/`StandardBaseConverter.cs`; a mixed-lightmap-eligibility
    warning in `MeshRendererConverter.cs`; a Unity "fake null" crash fix and SH2 default-off
    in `SkyboxConverter.cs`; assorted fallback handling in `Texture2DConverter.cs`/
    `StandardConverter.cs`/`UnlitConverter.cs`.
11. **Lightmap Pipeline panel** (new, `Assets/Editor/`) — `LightmapPipelineWindow.cs`
    (`Resonite SDK > Lightmap Pipeline`) turns "pick a quality preset → bake → send" into
    one button (`Bake & Send`), for either Bakery (if installed) or Unity's built-in
    Progressive Lightmapper. Also exposes standalone `Bake`/`Convert Lights`/partial-send
    buttons, a lighting-tuning section (ambient/shadow/sun knobs) for the Unity Standard
    path, an experimental "bake normal detail into lightmap" option, a "Send-Time Options"
    section (Force Refresh Generated Lightmaps / Send Tonemap Compensation toggles, moved
    here from the main SDK panel since they're overlay additions, not vanilla
    Resonite.UnitySDK), a "Send-Time Light Tuning" section
    (`LightTuning.IntensityCeiling`/`WhiteBalanceShift` sliders), and a "Baked Lightmap
    Exposure" section (`LightmapDecoder.RangeScale`/`ColorSaturationCompensation` sliders —
    a bake's own brightness varies per scene, so these are meant to be re-checked whenever
    you switch rooms) — all three of the latter sections are shown regardless of baker since
    they apply at conversion/send time (or, for the last one, at lightmap-decode time), not
    at bake time. The actual bake/
    send logic lives in `LightmapTestHarness.cs`, which the panel calls into (no duplicated
    logic) and which can also be driven by an external process by writing a command string
    to `Temp/lightmap_harness_cmd.txt` (see that file's header comment for the command
    list) — this was originally built as an AI-agent automation hook, but works the same
    way for any external script. `BakeryPresenceDefine.cs` auto-detects whether Bakery is
    installed (`BAKERY_INCLUDED`) so the panel/harness compile and degrade gracefully either
    way; `BakeryTempObjectSuppression.cs` excludes Bakery's own temp storage object from
    scene sends; `PoiyomiBakeStandin.cs` temporarily swaps Poiyomi materials to a Standard
    stand-in for the bake (Poiyomi never receives a baked lightmap otherwise) and restores
    them afterward.

### Design principle: minimize the diff against official files

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
pass enum doesn't exist upstream so there's nothing to externalize away from),
`StandardConverter.cs`/`StandardSpecularConverter.cs` (a couple of guard lines each),
`MeshRendererConverter.cs`/`MeshColliderConverter.cs` (partial-send guards).

### Structural notes (2026-08-08 review, lightbake feature only)

A dedicated review of the lightbake-specific files found one concrete piece of wasted
work, since fixed:

- With `AdditiveFillStrength = 0` (the current tuned default), the additive-fill mechanism
  contributes nothing — `SecondaryEmissiveColor` is pure black regardless of what texture
  feeds it. Before the fix, `LightmapMaterialCache.cs` unconditionally decoded a
  desaturated "gray" companion texture for every lightmap on every send, and
  `BakedLightmapStandardConverter.cs` unconditionally uploaded it to Resonite over
  ResoniteLink — all for zero visual effect. Both are now gated behind
  `AdditiveFillStrength > 0`; raising the value again re-enables the full path exactly as
  before. Verified live: sends now produce zero `LMTex_N_gray` textures (previously one per
  lightmap).
- `DirectionalLightmapBaker.cs` (~767 lines) is a legitimate, well-documented opt-in
  feature for directional lightmap bakes, but is currently fully dormant for this project
  since the reference scene uses a NonDirectional bake. Not flagged as waste — just an
  observation for anyone auditing code size.

### Not included

- `Assets/ResoniteSDK/Generated/` (scene-specific generated output) is deliberately
  excluded
- `Assets/ResoniteSDK/AvatarSetup/` (an unrelated avatar-rigging setup wizard that happens
  to live in the same local project) is a separate concern, not part of this overlay

### Known open issues

- ResoniteLink.dll occasionally drops the WebSocket mid-send in an unsynchronized way (a
  candidate for an upstream issue report; `AssetConverter.cs`'s 60-second timeout only
  detects this, it doesn't fix the root cause)
- `AudioEffectConverter.cs` has not been verified against real in-world audio in Resonite
- `LightTuning.WhiteBalanceShift` blends every light toward white uniformly regardless of
  its original hue, so a scene with multiple differently-colored lights would have those
  color differences washed out evenly (this SDK's reference scene uses a single warm color
  scheme, so multi-color support hasn't been built yet)
- `LightTuning.IntensityCeiling`'s per-scene normalization only bounds the single brightest
  light in the scene, not the cumulative brightness of many lights summed together — a
  scene with dozens of moderate-intensity fill lights, each individually under the ceiling,
  can still add up to an overexposed result
- Whether the MetallicMap's alpha channel affects Albedo visibility on a real Resonite
  client (user-reported, unconfirmed) is an open question

---

## 日本語

`Resonite.UnitySDK`（Yellow-Dog-Man公式）の最新版に、そのまま重ねて使えるUnityPackage。
公式ファイルへの変更は最小限に抑え、新機能・調整値のロジックはできる限り新規ファイルへ
外だししてある（詳細は「設計方針」参照）。

このリポジトリはソースツリーを閲覧できる形で公開しています。すぐにインポートできる
`.unitypackage`は各[Release](../../releases)に添付しています。

### ⚠️ 公式SDKアップデート時にこのオーバーレイは消えます

公式SDK自体のパネル内注意書きには「新しいバージョンのSDKをインストールする前に、必ず
ResoniteSDKフォルダを削除してください」とあります。このオーバーレイのファイル（新規ファイル・
公式ファイルへの変更の両方）は同じ`Assets/ResoniteSDK/`フォルダ配下に置かれているため、
この公式の手順に従うと**このオーバーレイは完全に消えます**——部分的・静かに壊れるのではなく、
完全な消滅です。公式アップデート後は、このオーバーレイのパッケージを再インポートしないと
機能が戻りません。

もう1つ、より見えにくいリスクがあります。このオーバーレイが変更する公式ファイル（下記
フォルダ構成の「[公式・変更あり]」参照）は「パッチ」ではなく「完全な差し替えファイル」として
配布しています。もし公式SDKがこのオーバーレイのフォーク元以降に同じファイルへ変更を加えていた
場合、公式アップデート後にこのオーバーレイを再適用すると**それらのファイルがフォーク元時点の
内容へ巻き戻り**、公式側が加えた変更を静かに消してしまいます。公式アップデート後にこの
オーバーレイを再適用する前に、対象ファイルをこのリポジトリのフォーク元コミットと比較し、
上流側の変更を手動でマージし直す必要が無いか確認してください。

*（2026-08-08時点の状況: このフォーク元コミット`5eea4b03`は`Yellow-Dog-Man/Resonite.UnitySDK`の
`main`ブランチ先端と完全に一致しています——GitHub APIで直接確認済み、差分ゼロ・オープンPRも
無し。ただしこれは時間とともに変わる前提で書いているので、ここに書いてあるからといって
「今も最新のはず」と思い込まず、自分で確認してください。）*

### 導入方法

1. 最新の `Resonite.UnitySDK` をUnityプロジェクトへ通常通りセットアップする
2. [最新のRelease](../../releases/latest)から`.unitypackage`をダウンロードし、
   ダブルクリックまたはUnityの`Assets > Import Package > Custom Package...`からインポート
   （ソースをgit管理したい場合は、このリポジトリの`Assets/ResoniteSDK/`を直接
   プロジェクトへコピーしてもよい）
3. 全チェックを入れたままImport
4. コンパイル完了後、`Resonite SDK > Open Resonite SDK Manager` から通常通り接続

### 使い方

#### 通常のシーン送信

1. `Resonite SDK > Open Resonite SDK Manager` を開く
2. AutoDiscovery（推奨）または Manual（ポート指定）で接続
3. 必要に応じてトグルを確認:
   - **Convert Skybox** — スカイボックス（Material/ReflectionProbe）も一緒に送るか
     （このパネルの項目のうち、これだけが本家Resonite.UnitySDK由来のvanilla機能）
   - Force Refresh Generated LightmapsとSend Tonemap Compensationは**Lightmap Pipeline**
     パネルの「送信時オプション」セクションに移設済み（`Resonite SDK > Lightmap Pipeline`。
     どちらのBakerでも使える。下記「Tonemap Compensation」参照）
4. `Send Current Scene` を押す。World Root直下に単一の `Unity Import` スロットが作られ
   （既存があれば削除してから作り直す＝再送信しても重複しない）、その下に全内容が入る
5. 送信中は `AssetConverter.cs` 側に60秒のタイムアウトが設定されている。
   タイムアウトやWebSocket切断が起きたら「デバッグツール」の `Reset conversion state` →
   再送信で復帰できる

#### デバッグ・部分送信

`Resonite SDK > Open Debug Tools` を開く（`Resonite SDK Manager` が先に接続されている必要あり）:

- **Send Meshes Only / Send Materials Only / Send Lightmaps Only** — フルシーン送信をやり直さず
  一部だけ再送信したい時に使用（`ConversionPassState.ActivePass` で内部的に何を送るか絞り込む）
- **Retry Missing Asset URLs** — アセットアップロードだけ失敗して他は成功している場合に使用
- **Log Messages JSON** — 送信メッセージをJSONでログ出力（デバッグ用、デフォルトOFF）
- **Cleanup converters in the scene / Cleanup Resonite Components in the scene** —
  変換用ヘルパーコンポーネントをシーンから一括削除
- **Reset conversion state** — `SceneConverter` を作り直し、上記2つのクリーンアップも実行。
  接続がおかしくなった時の基本の復旧手段
- **Clear Generated Lightmap Variants** — `Assets/ResoniteSDK/Generated/LightmapVariants`
  フォルダ（バリアントマテリアル・デコード済みライトマップPNG含む）を丸ごと削除。
  シーン再変換で完全に再生成されるため常に安全に実行可能。ResoniteLink接続不要

#### 除外されるオブジェクト

送信対象のシーンルートから、以下は自動的に除外される（`SceneRootFilter.cs`）:

- `UnityEngine.Camera` を持つルート（Resoniteは独自のカメラ系を持つため不要）
- Missing Prefab状態のルート（VRChatワールドをインポートした際の `VRCWorld` 等、
  ソースアセットが存在せず変換しようがないもの）
- 階層内（ルート自身または子孫、非アクティブ含む）のどこかに、スクリプト解決不能な
  MonoBehaviourを含むルート。Unityはこれを`GetComponentsInChildren<Component>(true)`上の
  文字通りの`null`エントリとして表す（Unity公式の「missing scriptを持つprefabを選択」
  ツールと同じ判定方法）。壊れたprefabインスタンス自体ではないVRChat由来の残骸
  （例: `Easy Mirror`——prefab自体は正常だが、その子`UI`が解決不能なVRChat SDKスクリプトを
  持つ）も拾える。ルートだけでなく階層全体を走査する（該当箇所がルート直下とは限らないため）。

#### ライトマップベイク → Resonite取り込み

Bakery（導入されていれば）またはUnity標準Progressive Lightmapperでベイクしたライトマップを
Resonite側でも近い見た目に近似するパイプライン。運用の要点:

- ベイク後は毎回 `Send Current Scene`（または `Send Lightmaps Only`）でOK。
  `Force Refresh Generated Lightmaps` がONなら差分ファイルを都度作り直す
- Resoniteはベイク済みGI（間接光）機構を持たないため、`BakedLightmapStandardConverter` が
  ベイクデータをマテリアルの `SecondaryAlbedoTexture`（乗算）へ焼き込むことで近似している。
  Resonite側で実ライト（`LightConverter`経由）も同時に有効なままにする設計
  （下記「送信時の明るさ・色調整」参照）
- 明るさ・金属感が実機で合わない場合は `LightTuning.cs` / `BakedLightmapStandardConverter.cs` の
  各static値を調整する（後述）

#### 送信時の明るさ・色調整（実機チューニング値）

Unity上での見た目とResonite実機での見た目にギャップがあり、以下のstatic値を実機検証しながら
調整している。`IntensityCeiling`/`WhiteBalanceShift` は **Lightmap Pipeline** パネルの
「送信時ライト調整」セクションに、`RangeScale`/`ColorSaturationCompensation` は同パネルの
「ベイクライトマップ露出」セクションにスライダーがある（`Resonite SDK > Lightmap Pipeline`。
いずれもどちらのBakerでも使える。下記のパネル説明も参照）。残りは値を変えたい場合コード内の
該当フィールドを直接編集する:

| ファイル | フィールド | 現在値 | 意味 |
|---|---|---|---|
| `LightTuning.cs` | `IntensityCeiling` | 0.9 | シーン内で最も明るいライトがこの値になるよう、全ライトへ同じ比率で倍率を逆算（固定倍率ではなくシーンごとに自動正規化） |
| `LightTuning.cs` | `WhiteBalanceShift` | 0.55 | 光源色を送信時だけ白側へブレンド（0=元の色, 1=純白）|
| `LightmapDecoder.cs` | `RangeScale` | 1.1 | ベイクデータのデコード前ゲイン |
| `LightmapDecoder.cs` | `ColorSaturationCompensation` | 0.6 | ベイクデータ自体の彩度低減（0〜1）|
| `BakedLightmapStandardConverter.cs` | `SmoothnessCompensation` | 0.05 | スカラーSmoothnessへの係数（MetallicMap無しの材質のみ有効）|
| `BakedLightmapStandardConverter.cs` | `MetallicCompensation` | 0.0 | スカラーMetallicへの係数（同上）|
| `BakedLightmapStandardConverter.cs` | `AdditiveFillStrength` | 0.0 | ベイクデータの加算フィル強度（現在無効、乗算のみ）|

**MetallicMapがあるマテリアル**（couch01/lights/tables/wall_door）はスカラー係数が無視される
（`PBSMultiUV.shader`の`_METALLICMAP`分岐でR=Metallic/A=Smoothnessをテクスチャから直接読む
ため）。この4マテリアルはテクスチャ自体をMetallic=0・Smoothness×8/9で焼き直し済み
（`*_MetallicSmoothness.png`、DXT5・アルファ有り）。

#### Tonemap Compensation（実験的機能）

UnityのPPv2 Neutral Tonemapper相当の色調圧縮を再現し、マテリアル色
（AlbedoColor/EmissiveColor、彩度のみ）とReflection Probe強度に反映する。Resonite
(Renderite)は主カメラのポスト処理トーンマッピングを持たないため、同じHDR値でもUnity側より
反射・発光がぎらつく問題への対策。**Lightmap Pipeline**パネルの「送信時オプション」内
「Send Tonemap Compensation」トグルでON/OFF可能（デフォルトON）。

**現状、実際に見た目へ効いているのはReflection Probe側のみ。** マテリアル色側
（`ColorGradingApproximation.Apply`）は彩度のみを適用する実装になっているが、彩度調整は
無彩色（白・グレー、r=g=b）に対しては数式上no-op（実機確認済み: トグルON時もAlbedoColorが
`(1,1,1)`のまま変化なし）。参照シーンのマテリアルはほぼ全て`_Color=白`固定（実際の色味は
テクスチャ由来でこのフィールドではない）ため、この機能のマテリアル色側は現状観測可能な効果が
無い。実際に色味付きの`_Color`を持つマテリアルであれば効果が出る、あるいは
`ColorGradingApproximation.Apply()`をフルグレーディング（`ApplySaturationOnly()`ではなく
`ApplyGrading()`）に戻せば違いが出る（フルグレーディングを試して差し戻した経緯は同ファイルの
コメント参照）。

### 何を行っているか（機能別サマリ）

1. **ベイクライトマップのResonite取り込み**（本パッケージの中核機能） — Bakery（導入されて
   いれば）またはUnity標準Progressive Lightmapperのベイク結果を、Resoniteに存在しない
   「ベイクGI」を`SecondaryAlbedoTexture`乗算で近似して転送（`LightmapDecoder.cs`/
   `LightmapMaterialCache.cs`/`BakedLightmapStandardConverter.cs`/`BakedLightmapStandard.shader`/
   `DirectionalLightmapBaker.cs`、全て新規）。ライトマッププレビュー用テクスチャは256px上限で
   送信（`LightmapDecoder.MaxPreviewTextureSize`で調整可能）。生成ライトマップテクスチャは
   ファイルインポートではなく生ピクセルデータとして送信（`Texture2DConverter.cs`）。
   `AdditiveFillStrength`が0（現在の既定値）の場合、加算フィル用のデサチュレートテクスチャの
   生成・アップロード自体をスキップ（詳細は下記「構造上の所見」参照）。
2. **Tonemap Compensation**（実験的、新規） — `PPv2ToneMapMath.cs`がUnity
   `com.unity.postprocessing@3.4.0`のグレーディングパイプライン（LogC空間Contrast/
   WhiteBalance/Saturation/NeutralTonemap）を再現。`ColorGradingApproximation.cs`が
   マテリアル色への適用口、`ReflectionProbeConverter.cs`がReflection Probe強度への適用。
   本体SDKと独立した`Assets/ResoniteSDK/ToneMapCompensation/`フォルダに実装を分離。
3. **送信時ライト調整**（新規、外だし済み） — `LightTuning.cs`がライト全体の明るさ倍率・
   白色寄せブレンドを1ファイルに集約（`LightConverter.cs`本体は公式のまま、呼び出し2行のみ変更）。
4. **シーン取り込み衛生**（新規、外だし済み）:
   - Camera/Missing Prefab除外 — `SceneRootFilter.cs`
   - ID採番の衝突防止 — `GlobalIdAllocator.cs`（プロセス全体で単調増加するstaticカウンタ、
     `"ID '...' already in use"` FATAL ERROR対策）
   - 重複ツリー防止 — `ImportRootSlotHelper.cs`（再接続時、既存の`Unity Import`スロットを
     削除してから作り直す）
   - アセット同一性判定のバグ修正 — `AssetConversionManager.cs`（判定キーをGUID+
     ローカルファイルIDベースに変更、同一.fbx内の複数サブアセットの取り違えを修正）
   - 60秒タイムアウト — `AssetConverter.cs`（アセット変換がハングした場合に検知可能にする）
5. **パネル整理**（新規、外だし不要＝独自ファイル） — `ResoniteLinkWindow.cs`（メインパネル）
   は接続・Send Current Scene・Realtime Mode・3トグルのみに整理。部分送信テスト・
   クリーンアップ・状態リセット等は新規`ResoniteSDKDebugWindow.cs`
   （`Resonite SDK > Open Debug Tools`）へ分離。
6. **水面マテリアル自動検出**（新規） — `WaterPanningConverter.cs`がシェーダー名に"water"を
   含む任意のカスタムシェーダーを検出し、実在するResoniteワールドの水面表現パターン
   （PBS_Metallic + Panner2D UVスクロール）へ変換。
7. **オーディオエフェクト変換**（新規、未実機検証） — `AudioEffectConverter.cs`がUnity
   `AudioReverbFilter`をResonite `AudioZitaReverb`へ、Zita-Rev1アルゴリズムに基づく
   推定マッピングで変換。
8. **ポストプロセス変換**（新規） — `PostProcessingConverter.cs`がPPv2のGlobal Volumeを
   Resonite `PostProcessingSettings`（5項目のみ）へ変換。
9. **パーティクルシステム変換バグ修正** — `ParticleSystemConverter.cs`: BoxShell/BoxEdge
   形状でエミッターが生成されない不具合、Cone形状のHeight誤代入、Over Lifetimeモジュール
   未変換、Emission enabled判定漏れ、レガシーパーティクルシェーダー未対応を修正。
10. **その他バグ修正** — `MeshColliderConverter.cs`/`MeshRendererConverter.cs`/
    `StandardBaseConverter.cs`等の部分送信ガード、`MeshRendererConverter.cs`の混在時警告、
    `SkyboxConverter.cs`のfake-null対策・SH2デフォルト無効化、`Texture2DConverter.cs`/
    `StandardConverter.cs`/`UnlitConverter.cs`のフォールバック処理。
11. **Lightmap Pipelineパネル**（新規、`Assets/Editor/`） — `LightmapPipelineWindow.cs`
    （メニュー: `Resonite SDK > Lightmap Pipeline`）が「品質プリセット選択→ベイク→送信」を
    `Bake & Send`ボタン1つに集約する。対象はBakery（導入されていれば）またはUnity標準
    Progressive Lightmapperのどちらでも。単体の`Bake`/`Convert Lights`/部分送信ボタン、
    Unity標準経路向けのライティング調整セクション（環境光/影/太陽の各つまみ）、実験的な
    「法線を焼き込む」オプション、「送信時オプション」セクション（Force Refresh Generated
    Lightmaps / Send Tonemap Compensationトグル。本家Resonite.UnitySDK非搭載のオーバーレイ
    独自機能のためメインSDKパネルからこちらへ移設）、「送信時ライト調整」セクション
    （`LightTuning.IntensityCeiling`/`WhiteBalanceShift`のスライダー）、「ベイクライトマップ
    露出」セクション（`LightmapDecoder.RangeScale`/`ColorSaturationCompensation`のスライダー。
    焼きデータの明るさはシーンごとに違うため部屋を変えたら要再確認）も備える。後者3つは
    ベイク時ではなく変換/送信時（露出は正確にはデコード時）に効く値のためBaker非依存で
    常時表示。実際のベイク・送信
    ロジックは`LightmapTestHarness.cs`
    にあり、このパネルはそれを呼ぶだけ（ロジックの重複なし）。`Temp/lightmap_harness_cmd.txt`
    にコマンド文字列を書き込むことで外部プロセスからも駆動できる（コマンド一覧は同ファイルの
    ヘッダーコメント参照——元々はAI駆動の自動化フック用に作られたが、任意の外部スクリプトから
    同様に使える）。`BakeryPresenceDefine.cs`がBakery導入有無を自動検出（`BAKERY_INCLUDED`）し
    パネル/ハーネス双方が導入有無に関わらずコンパイル・動作する。`BakeryTempObjectSuppression.cs`
    はBakery自身の一時ストレージオブジェクトをシーン送信から除外、`PoiyomiBakeStandin.cs`は
    Poiyomiマテリアルをベイク中だけ一時的にStandard材へ差し替え（Poiyomiはベイクライトマップを
    受け取れないため）、完了後に復元する。

### 設計方針: 公式ファイルへの変更を最小化

オープンソース公開を見据え、**公式SDKファイルへの変更はできる限り呼び出し1〜3行に留め、
実質的なロジック・調整値は全て新規ファイルへ外だしする**方針で作業している。

例（`LightConverter.cs`）:
```csharp
// 公式ファイル側の変更はこの2行のみ
resonite.Intensity = LightTuning.ApplyIntensity(unity.intensity);
resonite.Color = new ColorX(LightTuning.ApplyColor(unity.color));
```
実体（倍率・ロジック）は同フォルダの新規`LightTuning.cs`側に完全分離。

同様のパターンを適用済み: `SceneConverter.cs`→`SceneRootFilter.cs`/`GlobalIdAllocator.cs`/
`ImportRootSlotHelper.cs`、`LightConverter.cs`→`LightTuning.cs`。

**外だし未着手**（公式ファイル自体への変更がまだ多く残っている箇所）:
`AssetConverter.cs`（60秒タイムアウト機構）、`AssetConversionManager.cs`
（アセット同一性判定ロジック）、`ConversionPassState.cs`（部分送信パスの列挙型自体は
公式に存在しないため外だし不可、これは新規ファイル）、`StandardConverter.cs`/
`StandardSpecularConverter.cs`（各2行程度のガード）、`MeshRendererConverter.cs`/
`MeshColliderConverter.cs`（部分送信ガード）。

### 構造上の所見（2026-08-08レビュー、ライトベイク機能のみ対象）

ライトベイク機能に絞った構造レビューで、具体的なムダを1件発見・修正済み:

- `AdditiveFillStrength = 0`（現在の確定値）の場合、加算フィル機構は視覚的に一切効果が無い
  （`SecondaryEmissiveColor`が常に真っ黒になるため、何を掛けても結果はゼロ）。修正前は
  `LightmapMaterialCache.cs`が毎回無条件でデサチュレート版「gray」テクスチャをデコード
  （RenderTexture blit + リサイズ + PNG encode + AssetDatabaseインポートのフルパス）し、
  `BakedLightmapStandardConverter.cs`がそれを毎回Resoniteへアップロードしていた——完全に
  無駄な処理。両箇所とも`AdditiveFillStrength > 0`の時だけ動くようガードした（機構自体は
  温存、値を戻せば従来通り動作）。実機検証済み：修正後は送信のたびに`LMTex_N_gray`テクスチャが
  1枚も生成されないことを確認（修正前は1ライトマップにつき1枚生成されていた）。
- `DirectionalLightmapBaker.cs`（約767行）は正当なオプトイン機能（方向性ライトマップベイク用）
  だが、現在の参照シーンはNonDirectionalベイクを使っているため完全に不稼働。ムダとしては
  扱っていない（コード量の把握用の備考のみ）。

### 含まれないもの

- `Assets/ResoniteSDK/Generated/`（テストシーン固有の生成物）は意図的に除外
- `Assets/ResoniteSDK/AvatarSetup/`（同じローカルプロジェクトに同居しているだけの、
  無関係なアバターセットアップウィザード）は別件として対象外

### 既知の未解決事項

- ResoniteLink.dll側の未同期WebSocket切断（送信中に稀に発生、上流Issue報告候補。
  `AssetConverter.cs`の60秒タイムアウトは検知のみで根治ではない）
- `AudioEffectConverter.cs`は実機（Resonite内での実際の音声）で未検証
- `LightTuning.WhiteBalanceShift`は光源の元色相に関わらず一律で白へブレンドするため、
  シーン内に複数の異なる色の光源がある場合は色差が均等に薄まる（単一色系照明のシーンでの
  運用を前提、複数色対応は未着手）
- `LightTuning.IntensityCeiling`のシーンごと正規化は、シーン内で最も明るい単体ライトしか
  基準にしていないため、中程度の明るさのライトが大量にある場合（個々は上限内でも合計で
  過露出になる）には対応できない
- MetallicMapのアルファチャンネルがResonite実機でAlbedoの見え方に影響するか（ユーザー報告あり、
  未確証）

---

## Folder structure / フォルダ構成

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
│       ├── ParticleSystemConverter.cs        [official, modified] assorted bug fixes
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

```
Assets/Editor/                                  ← Lightmap Pipeline panel (sibling of ResoniteSDK/, all new)
├── LightmapPipelineWindow.cs                  ★new "pick preset → Bake & Send" one-button panel
├── LightmapTestHarness.cs                     ★new actual bake/send logic (also file-command drivable)
├── BakeryPresenceDefine.cs                    ★new auto-detects Bakery (BAKERY_INCLUDED)
├── BakeryTempObjectSuppression.cs             ★new excludes Bakery's temp storage object from sends
└── PoiyomiBakeStandin.cs                      ★new temporary Poiyomi → Standard swap for baking
```

Legend / 凡例: `★new` / `★新規` = file that doesn't exist in the official SDK / 公式SDKに
存在しない新規ファイル. `[official, modified]` / `[公式・変更あり]` = an official file we
edit (aiming for the smallest diff possible) / 公式ファイルを編集（できる限り最小差分を志向）.

---

## License / ライセンス

[MIT](LICENSE). This project overlays
[`Resonite.UnitySDK`](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK) (Copyright (c)
2026 Yellow Dog Man Studios), which is also MIT-licensed. / 本プロジェクトは公式
`Resonite.UnitySDK`に重ねるオーバーレイであり、重ねる対象も同じくMITライセンスです。

Generated / 生成日: 2026-08-08
