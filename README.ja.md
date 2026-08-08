# ResoniteSDK Bakery Lightmap Overlay

`Resonite.UnitySDK`（Yellow-Dog-Man公式）の最新版に、そのまま重ねて使えるUnityPackage。
公式ファイルへの変更は最小限に抑え、新機能・調整値のロジックはできる限り新規ファイルへ
外だししてある（詳細は「設計方針」参照）。

English version: [README.md](README.md)

このリポジトリはソースツリーを閲覧できる形で公開しています。すぐにインポートできる
`.unitypackage`は各[Release](../../releases)に添付しています。

**前提条件**: 公式[`Resonite.UnitySDK`](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK)が
既にセットアップ済みのUnityプロジェクトが必要です——これは単独動作するSDKではなくオーバーレイ
（上乗せパッケージ）なので、これ単体では何も動きません。

ライセンス: [MIT](LICENSE)（重ねる対象の公式SDKと同じライセンス）
対象ブランチ: `feature/water-and-audio-presets`
フォーク元コミット: `5eea4b03`（PR #117マージ直後）

## ⚠️ 公式SDKアップデート時にこのオーバーレイは消えます

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

## 導入方法

1. 最新の `Resonite.UnitySDK` をUnityプロジェクトへ通常通りセットアップする
2. [最新のRelease](../../releases/latest)から`.unitypackage`をダウンロードし、
   ダブルクリックまたはUnityの`Assets > Import Package > Custom Package...`からインポート
   （ソースをgit管理したい場合は、このリポジトリの`Assets/ResoniteSDK/`を直接
   プロジェクトへコピーしてもよい）
3. 全チェックを入れたままImport
4. コンパイル完了後、`Resonite SDK > Open Resonite SDK Manager` から通常通り接続

## 使い方

### 通常のシーン送信

1. `Resonite SDK > Open Resonite SDK Manager` を開く
2. AutoDiscovery（推奨）または Manual（ポート指定）で接続
3. 必要に応じて3つのトグルを確認:
   - **Convert Skybox** — スカイボックス（Material/ReflectionProbe）も一緒に送るか
   - **Force Refresh Generated Lightmaps** — 生成済みライトマップ差分ファイルを毎回強制再生成するか
   - **Send Tonemap Compensation (experimental)** — マテリアル色/Reflection Probe強度への
     トーンマップ近似補正を掛けるか（下記「Tonemap Compensation」参照。デフォルトON）
4. `Send Current Scene` を押す。World Root直下に単一の `Unity Import` スロットが作られ
   （既存があれば削除してから作り直す＝再送信しても重複しない）、その下に全内容が入る
5. 送信中は `AssetConverter.cs` 側に60秒のタイムアウトが設定されている。
   タイムアウトやWebSocket切断が起きたら「デバッグツール」の `Reset conversion state` →
   再送信で復帰できる

### デバッグ・部分送信

`Resonite SDK > Open Debug Tools` を開く（`Resonite SDK Manager` が先に接続されている必要あり）:

- **Send Meshes Only / Send Materials Only / Send Lightmaps Only** — フルシーン送信をやり直さず
  一部だけ再送信したい時に使用（`ConversionPassState.ActivePass` で内部的に何を送るか絞り込む）
- **Retry Missing Asset URLs** — アセットアップロードだけ失敗して他は成功している場合に使用
- **Log Messages JSON** — 送信メッセージをJSONでログ出力（デバッグ用、デフォルトOFF）
- **Cleanup converters in the scene / Cleanup Resonite Components in the scene** —
  変換用ヘルパーコンポーネントをシーンから一括削除
- **Reset conversion state** — `SceneConverter` を作り直し、上記2つのクリーンアップも実行。
  接続がおかしくなった時の基本の復旧手段

### 除外されるオブジェクト

送信対象のシーンルートから、以下は自動的に除外される（`SceneRootFilter.cs`）:

- `UnityEngine.Camera` を持つルート（Resoniteは独自のカメラ系を持つため不要）
- Missing Prefab状態のルート（VRChatワールドをインポートした際の `VRCWorld` 等、
  ソースアセットが存在せず変換しようがないもの）

子オブジェクトとして紛れ込んでいる場合は未対応（現状はシーン直下のルート単位の判定のみ）。

### ライトマップベイク → Resonite取り込み

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

### 送信時の明るさ・色調整（実機チューニング値）

Unity上での見た目とResonite実機での見た目にギャップがあり、以下のstatic値を実機検証しながら
調整している。値を変えたい場合はコード内の該当フィールドを直接編集する（UI化はしていない）:

| ファイル | フィールド | 現在値 | 意味 |
|---|---|---|---|
| `LightTuning.cs` | `IntensityMultiplier` | 1.8 | 全ライト共通、Unity側`Light.intensity`への掛け率 |
| `LightTuning.cs` | `WhiteBalanceShift` | 0.7 | 光源色を送信時だけ白側へブレンド（0=元の色, 1=純白）|
| `LightmapDecoder.cs` | `RangeScale` | 1.1 | ベイクデータのデコード前ゲイン |
| `LightmapDecoder.cs` | `ColorSaturationCompensation` | 0.6 | ベイクデータ自体の彩度低減（0〜1）|
| `BakedLightmapStandardConverter.cs` | `SmoothnessCompensation` | 0.05 | スカラーSmoothnessへの係数（MetallicMap無しの材質のみ有効）|
| `BakedLightmapStandardConverter.cs` | `MetallicCompensation` | 0.0 | スカラーMetallicへの係数（同上）|
| `BakedLightmapStandardConverter.cs` | `AdditiveFillStrength` | 0.0 | ベイクデータの加算フィル強度（現在無効、乗算のみ）|

**MetallicMapがあるマテリアル**（couch01/lights/tables/wall_door）はスカラー係数が無視される
（`PBSMultiUV.shader`の`_METALLICMAP`分岐でR=Metallic/A=Smoothnessをテクスチャから直接読む
ため）。この4マテリアルはテクスチャ自体をMetallic=0・Smoothness×8/9で焼き直し済み
（`*_MetallicSmoothness.png`、DXT5・アルファ有り）。詳細経緯は
`C:/urd/wiki/concepts/resonite/dev-recipes/2026-08-08_rezo_con_baked_lightmap_metallic_compensation_values.md`。

### Tonemap Compensation（実験的機能）

UnityのPPv2 Neutral Tonemapper相当の色調圧縮を再現し、マテリアル色
（AlbedoColor/EmissiveColor、彩度のみ）とReflection Probe強度に反映する。Resonite
(Renderite)は主カメラのポスト処理トーンマッピングを持たないため、同じHDR値でもUnity側より
反射・発光がぎらつく問題への対策。パネルの「Send Tonemap Compensation」トグルでON/OFF可能
（デフォルトON）。

## 何を行っているか（機能別サマリ）

### 1. ベイクライトマップのResonite取り込み（本パッケージの中核機能）

- Bakery（導入されていれば）またはUnity標準Progressive Lightmapperのベイク結果を、
  Resoniteに存在しない「ベイクGI」を`SecondaryAlbedoTexture`乗算で近似して転送
  （`LightmapDecoder.cs`/`LightmapMaterialCache.cs`/`BakedLightmapStandardConverter.cs`/
  `BakedLightmapStandard.shader`/`DirectionalLightmapBaker.cs`、全て新規）
- ライトマッププレビュー用テクスチャは256px上限で送信（従来の応急処置64pxから復帰、
  `LightmapDecoder.MaxPreviewTextureSize`で調整可能）
- 生成ライトマップテクスチャ（`Assets/ResoniteSDK/Generated/LightmapVariants/`配下）は
  ファイルインポートではなく生ピクセルデータとして送信（`Texture2DConverter.cs`、
  ファイルパス経由だと差分が反映されないケースがあったため）
- `AdditiveFillStrength`が0（現在の既定値）の場合、加算フィル用のデサチュレート
  （彩度ゼロ化）テクスチャの生成・アップロード自体をスキップ（詳細は下記「構造上の所見」参照）

### 2. Tonemap Compensation（実験的、新規）

- `PPv2ToneMapMath.cs`: Unity `com.unity.postprocessing@3.4.0`のグレーディングパイプライン
  （LogC空間Contrast/WhiteBalance/Saturation/NeutralTonemap）を再現
- `ColorGradingApproximation.cs`: マテリアル色への適用口（彩度のみ適用、`MaterialGradingEnabled`）
- `ReflectionProbeConverter.cs`: Reflection Probe強度への適用（`ComputeReflectionProbeCompensationFactor()`）
- 本体SDKと独立した`Assets/ResoniteSDK/ToneMapCompensation/`フォルダに実装を分離

### 3. 送信時ライト調整（新規、外だし済み）

- `LightTuning.cs`: ライト全体の明るさ倍率・白色寄せブレンドを1ファイルに集約
  （`LightConverter.cs`本体は公式のまま、呼び出し2行のみ変更）

### 4. シーン取り込み衛生（新規、外だし済み）

- **Camera/Missing Prefab除外**: `SceneRootFilter.cs` — Unity独自のCameraと、VRChatワールード
  インポート等で残るMissing Prefabルートを送信対象から自動除外
- **ID採番の衝突防止**: `GlobalIdAllocator.cs` — プロセス全体で単調増加するstaticカウンタに
  変更。再接続でUniqueSessionIdが変わらないまま`SceneConverter`が作り直されても、
  同じID文字列が二度と生成されないことを保証（`"ID '...' already in use"` FATAL ERROR対策）
- **重複ツリー防止**: `ImportRootSlotHelper.cs` — 再接続時、World Root直下に既存の
  `Unity Import`スロットが無いか確認し、あれば削除してから作り直す
- **アセット同一性判定のバグ修正**: `AssetConversionManager.cs` — 判定キーを参照ベースから
  GUID+ローカルファイルIDベースに変更。同一.fbx内の複数サブアセット（Cube/curtain/Cylinder等）
  が誤って同一アセット扱いされ、メッシュが取り違わる実害を修正
- **60秒タイムアウト**: `AssetConverter.cs` — アセット変換がハングした場合に例外を投げて
  検知可能にする（ResoniteLink側の未同期WebSocket切断対策）

### 5. パネル整理（新規、外だし不要＝独自ファイル）

- `ResoniteLinkWindow.cs`（メインパネル）は接続・Send Current Scene・Realtime Mode・
  3トグルのみのシンプルな見た目に整理
- 部分送信テスト・クリーンアップ・状態リセット等のデバッグ機能は新規`ResoniteSDKDebugWindow.cs`
  （メニュー: `Resonite SDK > Open Debug Tools`）へ分離。呼び出し先は`ResoniteLinkWindow`側の
  publicメソッドをそのまま呼ぶだけでロジックの複製なし

### 6. 水面マテリアル自動検出（新規）

- `WaterPanningConverter.cs`: シェーダー名に"water"を含む任意のカスタムシェーダーを検出し、
  実在するResoniteワールドの水面表現パターン（PBS_Metallic + Panner2D UVスクロール）へ変換

### 7. オーディオエフェクト変換（新規、未実機検証）

- `AudioEffectConverter.cs`: Unity `AudioReverbFilter`をResonite `AudioZitaReverb`へ変換。
  Zita-Rev1アルゴリズムのパラメータ対応関係に基づく推定マッピング（Resonite実機での
  音声確認は未実施、コンパイル・ロジックの妥当性のみ検証済み）

### 8. ポストプロセス変換（新規）

- `PostProcessingConverter.cs`: PPv2の Global Volume を Resonite `PostProcessingSettings`
  （MotionBlur/Bloom/AO強度、SSR有無、AA方式の5項目のみ）へ変換

### 9. パーティクルシステム変換バグ修正

- `ParticleSystemConverter.cs`: BoxShell/BoxEdge形状でエミッターが生成されない不具合、
  Cone形状のHeight誤代入、Over Lifetimeモジュール未変換、Emission enabled判定漏れ、
  レガシーパーティクルシェーダー（Alpha Blended/Additive）未対応を修正

### 10. その他バグ修正

- `MeshColliderConverter.cs`/`MeshRendererConverter.cs`/`StandardBaseConverter.cs`等:
  `Send Meshes/Materials/Lightmaps Only`の部分送信パスで、対象外のデータ（メッシュ/
  ソーステクスチャ等）を誤って送らないようガード追加
- `MeshRendererConverter.cs`: ライトマップバリアント差し替え対象外（非Standardシェーダー等）
  のマテリアルが一部だけ混在した場合に警告ログを出す
- `SkyboxConverter.cs`: ドメインリロード後にUnityの"fake null"参照へアクセスして
  `MissingReferenceException`になる不具合を修正。球面調和関数(SH2)変換は
  シリアライズ不可のため`ConvertSphericalHarmonics=false`でデフォルト無効化
- `Texture2DConverter.cs`/`StandardConverter.cs`/`UnlitConverter.cs`: 上記1・9と連動する
  各種プロパティ欠落時のフォールバック処理・レガシーシェーダー対応

## 設計方針: 公式ファイルへの変更を最小化

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

## フォルダ構成

```
Assets/ResoniteSDK/
├── ToneMapCompensation/                      ← 本体SDKと完全分離（新規フォルダ）
│   ├── PPv2ToneMapMath.cs                    PPv2グレーディング再現
│   └── ToneMapCompensationState.cs           パネルのON/OFFトグルが書き込む共有static state
│
├── Editor/
│   ├── SceneConverter.cs                     [公式・変更あり] 変換オーケストレーター
│   ├── SceneRootFilter.cs                    ★新規 Camera/Missing Prefab除外判定
│   ├── GlobalIdAllocator.cs                  ★新規 プロセス全体で単調増加するID採番
│   ├── ImportRootSlotHelper.cs               ★新規 既存"Unity Import"スロット探索
│   ├── SkyboxConverter.cs                    [公式・変更あり] fake-null対策・SH2デフォルト無効化
│   ├── AssetConversionManager.cs             [公式・変更あり] アセット同一性判定(GUID+localId)
│   └── Managers/
│       ├── ResoniteLinkWindow.cs             [公式・変更あり] メインパネル(接続・送信・トグル3つ)
│       └── ResoniteSDKDebugWindow.cs         ★新規 デバッグパネル(部分送信・クリーンアップ)
│
├── AssetConverters/
│   ├── AssetConverter.cs                     [公式・変更あり] 60秒タイムアウト機構
│   └── Texture2DConverter.cs                 [公式・変更あり] 生成ライトマップの生ピクセル送信
│
├── ConversionPassState.cs                    ★新規 部分送信パス(Full/MeshesOnly/MaterialsOnly/LightmapsOnly)
│
├── ComponentConverters/Unity Core/
│   ├── Audio/
│   │   └── AudioEffectConverter.cs           ★新規 AudioReverbFilter→AudioZitaReverb(未実機検証)
│   ├── Colliders/
│   │   └── MeshColliderConverter.cs          [公式・変更あり] 部分送信ガード
│   └── Rendering/
│       ├── LightConverter.cs                 [公式・変更あり(2行)] → LightTuning.cs呼び出し
│       ├── LightTuning.cs                    ★新規 明るさ倍率・白色寄せブレンド値
│       ├── LightmapDecoder.cs                ★新規 ベイクデータのデコード・ゲイン・彩度調整
│       ├── LightmapMaterialCache.cs          ★新規 ライトマップ別マテリアルバリアント管理
│       ├── LightmapDecode.shader             ★新規 デコード用シェーダー
│       ├── DirectionalLightmapBaker.cs       ★新規 方向性ライトマップ焼き込み
│       ├── RawPassthroughBlit.shader         ★新規
│       ├── Uv2NormalPatch.shader             ★新規
│       ├── MeshRendererConverter.cs          [公式・変更あり] ライトマップバリアント差替+部分送信ガード
│       ├── SkinnedMeshRendererConverter.cs   [公式・変更あり(3行)]
│       ├── ReflectionProbeConverter.cs       [公式・変更あり] Tonemap Compensation適用
│       ├── ParticleSystemConverter.cs        [公式・変更あり] 各種バグ修正(本README「9」参照)
│       └── PostProcessingConverter.cs        ★新規 PPv2 GlobalVolume→PostProcessingSettings
│
├── MaterialConverters/
│   ├── ColorGradingApproximation.cs          ★新規 Tonemap Compensationのマテリアル適用口
│   ├── Custom/
│   │   └── WaterPanningConverter.cs          ★新規 "water"シェーダー自動検出→Panner2D
│   ├── PBS/
│   │   ├── StandardBaseConverter.cs          [公式・変更あり] プロパティ欠落フォールバック+部分送信ガード
│   │   ├── StandardConverter.cs              [公式・変更あり] 同上
│   │   ├── StandardSpecularConverter.cs      [公式・変更あり] 同上
│   │   ├── BakedLightmapStandard.shader      ★新規 ベイクライトマップ用マテリアル
│   │   └── BakedLightmapStandardConverter.cs ★新規 ベイクライトマップ変換本体(調整値集約)
│   └── Unlit/
│       └── UnlitConverter.cs                 [公式・変更あり] レガシーパーティクルシェーダー対応
```

凡例: `★新規` = 公式SDKに存在しない新規ファイル。`[公式・変更あり]` = 公式ファイルを編集
（できる限り最小差分を志向、外だし未着手のものは差分がまだ大きい場合あり）。

## 構造上の所見（2026-08-08レビュー、ライトベイク機能のみ対象）

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

## 含まれないもの

- `Assets/ResoniteSDK/Generated/`（テストシーン固有の生成物）は意図的に除外
- `Assets/Editor/LightmapTestHarness.cs`等（アテナ駆動の実機テスト自動化ハーネス、
  ヘッダーコメントに明記の通り「テスト専用・SDKフォークには含めない」）

## 既知の未解決事項

- ResoniteLink.dll側の未同期WebSocket切断（送信中に稀に発生、上流Issue報告候補。
  `AssetConverter.cs`の60秒タイムアウトは検知のみで根治ではない）
- `AudioEffectConverter.cs`は実機（Resonite内での実際の音声）で未検証
- `LightTuning.WhiteBalanceShift`は光源の元色相に関わらず一律で白へブレンドするため、
  シーン内に複数の異なる色の光源がある場合は色差が均等に薄まる（単一色系照明のシーンでの
  運用を前提、複数色対応は未着手）
- MetallicMapのアルファチャンネルがResonite実機でAlbedoの見え方に影響するか（ユーザー報告あり、
  未確証）は`2026-08-08_rezo_con_baked_lightmap_metallic_compensation_values.md`で未解決事項として記録済み

## ライセンス

[MIT](LICENSE)。本プロジェクトは
[`Resonite.UnitySDK`](https://github.com/Yellow-Dog-Man/Resonite.UnitySDK)
（Copyright (c) 2026 Yellow Dog Man Studios）に重ねるオーバーレイであり、重ねる対象も
同じくMITライセンスです。

生成日: 2026-08-08（最終更新: LightConverter/SceneConverter外だし・Camera/VRCWorld除外・
明るさ調整2.5→1.8・加算フィル無駄排除・コメント英語化・英語版README追加を反映）
