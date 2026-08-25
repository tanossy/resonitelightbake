using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Approximates Unity's directional lightmap shading ("Bake in normals") for Resonite, which has
// no directional-lightmap material input and no custom-shader support, so a directional bake's
// per-pixel normal response would otherwise be silently discarded (only the flat lightmapColor
// would reach Resonite). Only invoked from LightmapMaterialCache.GetVariantOrOriginalInner, once
// that call site confirms the renderer's LightmapData.lightmapDir is non-null.
//
// Approach, done once per renderer at Editor conversion time (not runtime — output is a static
// baked texture, like the plain LightmapDecoder path):
//   1. Rasterize the renderer's own geometry (vertex) normal into UV2 space at the same texel
//      density as its lightmap-atlas footprint (RenderUv2NormalPatch / Uv2NormalPatch.shader).
//      Geometry-only — no tangent-space normal map sampling (see limitations below).
//   2. Read back that normal patch plus the matching sub-rect of the already-decoded color
//      lightmap (LightmapDecoder.GetDecodedLightmap, reused as-is) and the raw directional
//      lightmap (LightmapData.lightmapDir, sampled with no CPU-side decode — matching how
//      UnityGlobalIllumination.cginc samples unity_LightmapInd raw before calling
//      DecodeDirectionalLightmap, unlike the color lightmap which always goes through
//      DecodeLightmap() first).
//   3. Combine per texel using UnityCG.cginc's own formula, in plain C# since this runs once per
//      renderer rather than per-frame:
//        halfLambert = dot(normalWorld, dirTex.xyz - 0.5) + 0.5
//        result = color * halfLambert / max(1e-4, dirTex.w)
//   4. Persist as an 8-bit sRGB PNG (same convention as LightmapDecoder.DecodeAndSave), so it
//      slots into BakedLightmapStandard's _BakedLightmap property like a plain decoded lightmap.
//
// Known limitations: only the mesh's vertex normal is used (no normal-map detail — a possible
// follow-up), and the output isn't dilated into UV-island padding, so a seam is possible at
// UV-chart boundaries under bilinear sampling.
public static class DirectionalLightmapBaker
{
    const string Uv2NormalPatchShaderName = "ResoniteSDK/Internal/Uv2NormalPatch";

    // See BlitReadableAtlas: dirTex's Blit needs this material rather than the argument-less
    // Graphics.Blit(source, rt) overload.
    const string RawPassthroughShaderName = "ResoniteSDK/Internal/RawPassthroughBlit";

    // Shares LightmapVariantStorage.RootFolder's per-scene subfolders with LightmapMaterialCache/
    // LightmapDecoder's own assets, so all of it falls under one "Clear Generated Lightmap
    // Variants" cleanup sweep.
    const string FilePrefix = "LMDir_";

    // In-memory front-cache; AssetDatabase remains the source of truth (same pattern as
    // LightmapDecoder._decodedByPath / LightmapMaterialCache._materialByPath), safe even when
    // empty/stale right after a domain reload.
    static readonly Dictionary<string, Texture2D> _bakedByPath = new Dictionary<string, Texture2D>();

    static bool _createdNewAssetThisCall;

    /// <summary>
    /// Returns a persisted, per-renderer combined color lightmap texture that approximates
    /// <paramref name="lightmapData"/>'s directional response for <paramref name="renderer"/> —
    /// see this class's header comment for the full algorithm. Returns null (never throws) if
    /// this renderer/mesh isn't eligible (no MeshFilter, no UV2 channel, etc.) or anything along
    /// the way fails; the caller (LightmapMaterialCache) already treats a null return as "fall
    /// back to the plain non-directional decode".
    /// </summary>
    public static Texture2D GetNormalBakedLightmap(Renderer renderer, string sceneGuid, int lightmapIndex, LightmapData lightmapData, Vector4 lightmapScaleOffset)
    {
        if (renderer == null || lightmapData == null)
            return null;

        _createdNewAssetThisCall = false;

        try
        {
            var result = GetNormalBakedLightmapInner(renderer, sceneGuid, lightmapIndex, lightmapData, lightmapScaleOffset);

            if (_createdNewAssetThisCall)
                AssetDatabase.SaveAssets();

            return result;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ResoniteSDK] DirectionalLightmapBaker: failed to build a normal-baked lightmap for " +
                $"renderer \"{renderer.name}\" (lightmap index {lightmapIndex}): {ex}. Falling back to the plain " +
                "non-directional decode for this renderer.");
            return null;
        }
    }

    static Texture2D GetNormalBakedLightmapInner(Renderer renderer, string sceneGuid, int lightmapIndex, LightmapData lightmapData, Vector4 lightmapScaleOffset)
    {
        var meshFilter = renderer.GetComponent<MeshFilter>();
        var mesh = meshFilter != null ? meshFilter.sharedMesh : null;

        if (mesh == null)
        {
            Debug.LogWarning($"[ResoniteSDK] DirectionalLightmapBaker: renderer \"{renderer.name}\" has no MeshFilter/sharedMesh; skipping normal-bake for it.");
            return null;
        }

        // Mesh.uv2 is Unity's lightmap UV channel - the same one ModelImporter.generateSecondaryUV
        // populates and Uv2NormalPatch.shader's vertex input binds via TEXCOORD1.
        var uv2 = mesh.uv2;
        if (uv2 == null || uv2.Length == 0)
        {
            Debug.LogWarning($"[ResoniteSDK] DirectionalLightmapBaker: mesh \"{mesh.name}\" (renderer \"{renderer.name}\") has no UV2/lightmap-UV channel; skipping normal-bake for it.");
            return null;
        }

        var colorTex = lightmapData.lightmapColor;
        var dirTex = lightmapData.lightmapDir;

        if (colorTex == null || dirTex == null)
            return null; // Caller already checked lightmapDir != null, but stay defensive here too.

        // Reuses LightmapDecoder's own decode/cache path for the shared color atlas rather than
        // reimplementing RGBM/DoubleLDR/HDR decoding a second time.
        var decodedColor = LightmapDecoder.GetDecodedLightmap(sceneGuid, lightmapIndex, colorTex);
        if (decodedColor == null)
            return null; // LightmapDecoder already logged the specific reason.

        int atlasWidth = decodedColor.width;
        int atlasHeight = decodedColor.height;

        // This renderer's tile rect within the shared atlas (atlasUV = uv2 * scale + offset, per
        // Renderer.lightmapScaleOffset's documented semantics).
        //
        // Deliberately NOT clamped here: tileWidth/tileHeight must stay the renderer's true,
        // uncapped footprint size, since RenderUv2NormalPatch sizes its render target (and the
        // whole combined output) off these two values, and the caller relies on a 1:1 crop of
        // this renderer's own uv2 range. Real Unity output can carry a small negative offset
        // (e.g. measured (0.24, 0.24, -0.01, -0.01)), so only the atlas SAMPLE coordinates get
        // bounds-clamped, per-sample, inside ReadAtlasRegionClamped.
        int tileX = Mathf.RoundToInt(lightmapScaleOffset.z * atlasWidth);
        int tileY = Mathf.RoundToInt(lightmapScaleOffset.w * atlasHeight);
        int tileWidth = Mathf.Max(1, Mathf.RoundToInt(lightmapScaleOffset.x * atlasWidth));
        int tileHeight = Mathf.Max(1, Mathf.RoundToInt(lightmapScaleOffset.y * atlasHeight));

        var sceneFolder = LightmapVariantStorage.GetSceneFolder(sceneGuid);
        LightmapVariantStorage.EnsureFolder(sceneFolder);

        string rendererIdentity = GetRendererIdentity(renderer);
        string path = $"{sceneFolder}/{FilePrefix}{rendererIdentity}_{lightmapIndex}.png";

        // Cache-invalidation key: changes when either source texture's baked pixel content
        // changes (Texture2D.imageContentsHash) or the renderer's tile rect moves within the
        // atlas. Does not track "mesh normals changed with no re-bake" as a separate signal —
        // same limitation as LightmapDecoder's own color-only hash.
        string sourceHash = $"{colorTex.imageContentsHash}_{dirTex.imageContentsHash}_{tileX}_{tileY}_{tileWidth}_{tileHeight}";

        if (_bakedByPath.TryGetValue(path, out var cached))
        {
            if (cached != null && IsHashCurrent(path, sourceHash))
                return cached;

            _bakedByPath.Remove(path);
        }

        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null && IsHashCurrent(path, sourceHash))
        {
            _bakedByPath[path] = existing;
            return existing;
        }

        // 1) Rasterize this renderer's own geometry normals into UV2 space, at the same texel
        //    density as its atlas footprint.
        var normalPatch = RenderUv2NormalPatch(renderer, mesh, tileWidth, tileHeight);
        if (normalPatch == null)
            return null; // Already logged inside RenderUv2NormalPatch.

        // color and dir are each materialized as a full, CPU-readable atlas texture (see
        // LoadReadableColorAtlas / BlitReadableAtlas) before cropping, and normalPatch goes
        // through that same BlitReadableAtlas path, so normalPixels/colorPixels/dirPixels below
        // all share one consistent pixel-orientation convention.
        var colorAtlas = LoadReadableColorAtlas(decodedColor);
        if (colorAtlas == null)
        {
            UnityEngine.Object.DestroyImmediate(normalPatch);
            return null; // Already logged inside LoadReadableColorAtlas.
        }

        var dirAtlas = BlitReadableAtlas(dirTex, atlasWidth, atlasHeight);
        if (dirAtlas == null)
        {
            UnityEngine.Object.DestroyImmediate(normalPatch);
            UnityEngine.Object.DestroyImmediate(colorAtlas);
            return null; // Already logged inside BlitReadableAtlas.
        }

        Color[] normalPixels;
        Color[] colorPixels;
        Color[] dirPixels;
        try
        {
            normalPixels = normalPatch.GetPixels();
            colorPixels = ReadAtlasRegionClamped(colorAtlas, atlasWidth, atlasHeight, tileX, tileY, tileWidth, tileHeight);
            dirPixels = ReadAtlasRegionClamped(dirAtlas, atlasWidth, atlasHeight, tileX, tileY, tileWidth, tileHeight);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(normalPatch);
            UnityEngine.Object.DestroyImmediate(colorAtlas);
            UnityEngine.Object.DestroyImmediate(dirAtlas);
        }

        // 2) Combine, per texel, via UnityCG.cginc's own DecodeDirectionalLightmap() formula —
        //    see this class's header comment for the verbatim-quoted source.
        var combined = new Color[tileWidth * tileHeight];
        for (int i = 0; i < combined.Length; i++)
        {
            var np = normalPixels[i];
            Vector3 n = new Vector3(np.r * 2f - 1f, np.g * 2f - 1f, np.b * 2f - 1f);
            float nLen = n.magnitude;
            // Texels outside this renderer's own UV island are cleared to (0.5,0.5,0.5) by
            // RenderUv2NormalPatch, decoding to a zero vector; normalizing that would produce
            // NaN, so fall back to a fixed "up" normal instead (the seam/padding limitation noted
            // in the header comment).
            Vector3 normalWorld = nLen > 1e-5f ? n / nLen : Vector3.up;

            Color dir = dirPixels[i];
            float halfLambert = Vector3.Dot(normalWorld, new Vector3(dir.r - 0.5f, dir.g - 0.5f, dir.b - 0.5f)) + 0.5f;
            float denom = Mathf.Max(1e-4f, dir.a);

            Color c = colorPixels[i];
            combined[i] = new Color(
                c.r * halfLambert / denom,
                c.g * halfLambert / denom,
                c.b * halfLambert / denom,
                1f);
        }

        var savedTex = SaveCombinedTexture(path, tileWidth, tileHeight, combined, sourceHash);
        if (savedTex != null)
        {
            _createdNewAssetThisCall = true;
            _bakedByPath[path] = savedTex;
        }

        return savedTex;
    }

    static bool IsHashCurrent(string path, string sourceHash)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        return importer != null && importer.userData == sourceHash;
    }

    /// <summary>
    /// Deterministic identity string for a scene renderer, used in this class's persisted asset
    /// filename (and therefore its cache key). Keyed off
    /// UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(renderer) rather than a name-derived
    /// hierarchy path, because two sibling GameObjects sharing a name (e.g. duplicate + forgot to
    /// rename) would otherwise fold to the same output path, and the second renderer's bake would
    /// silently overwrite the first's persisted LMDir_*.png with no warning anywhere downstream.
    /// GlobalObjectId is content-derived (scene/prefab GUID + local file identifier), so
    /// same-named siblings can never collide here.
    /// </summary>
    static string GetRendererIdentity(Renderer renderer)
    {
        string idSource;

        try
        {
            idSource = GlobalObjectId.GetGlobalObjectIdSlow(renderer).ToString();
        }
        catch (Exception ex)
        {
            // Defensive only — GetGlobalObjectIdSlow is not expected to throw for a real scene
            // object, but this method must never throw, so fall back to a name-path identity
            // (which cannot distinguish same-named siblings) rather than abort the whole bake.
            Debug.LogWarning($"[ResoniteSDK] DirectionalLightmapBaker: GlobalObjectId.GetGlobalObjectIdSlow failed for " +
                $"renderer \"{renderer.name}\": {ex}. Falling back to a name-path-derived identity, which cannot " +
                "distinguish same-named sibling GameObjects (see this method's own doc comment for why that matters).");
            idSource = BuildNameHierarchyPath(renderer);
        }

        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in idSource)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8");
        }
    }

    /// <summary>
    /// Fallback identity source used only if GlobalObjectId.GetGlobalObjectIdSlow itself throws.
    /// Name-only, so it cannot distinguish same-named sibling GameObjects.
    /// </summary>
    static string BuildNameHierarchyPath(Renderer renderer)
    {
        var pathParts = new List<string>();
        var t = renderer.transform;
        while (t != null)
        {
            pathParts.Add(t.name);
            t = t.parent;
        }
        pathParts.Reverse();
        return string.Join("/", pathParts);
    }

    /// <summary>
    /// Rasterizes <paramref name="mesh"/>'s geometry (vertex) normals, transformed by
    /// <paramref name="renderer"/>'s own object-to-world matrix, into UV2 (lightmap UV) space, at
    /// <paramref name="width"/> x <paramref name="height"/> texels — see Uv2NormalPatch.shader for
    /// the rasterization technique. Returns a readable Texture2D (caller owns/destroys it) or
    /// null if the shader can't be found.
    ///
    /// This CommandBuffer.DrawMesh pass has no camera, so the render target's UV parameterization
    /// itself is the "projection". Do not add a manual Y-flip based on `_ProjectionParams` here:
    /// that global is only populated by SetupCameraProperties for a currently-rendering camera,
    /// so with no camera involved it reads stale leftover state, not this pass's actual
    /// orientation — the same class of bug BlitReadableAtlas's proven Y-orientation path avoids.
    /// The render happens into a temporary RT in DrawMesh's raw output orientation, then that RT
    /// is routed through BlitReadableAtlas (same path as dirTex/colorAtlas) so normalPatch ends up
    /// in the identical pixel-orientation convention the combine loop in
    /// GetNormalBakedLightmapInner requires.
    /// </summary>
    static Texture2D RenderUv2NormalPatch(Renderer renderer, Mesh mesh, int width, int height)
    {
        var shader = Shader.Find(Uv2NormalPatchShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"[ResoniteSDK] DirectionalLightmapBaker: could not find shader \"{Uv2NormalPatchShaderName}\".");
            return null;
        }

        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture rawRt = null;
        CommandBuffer cmd = null;

        try
        {
            // ARGBHalf + Linear read/write, matching LightmapDecoder's own Blit-based readback
            // convention for consistency with the rest of this feature's pipeline.
            rawRt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

            cmd = new CommandBuffer { name = "ResoniteSDK.Uv2NormalPatch" };
            cmd.SetRenderTarget(rawRt);
            // Mid-gray decodes to the zero vector via `raw*2-1`, marking texels outside this
            // renderer's UV island as "no data" (see the NaN-guard in
            // GetNormalBakedLightmapInner's combine loop) rather than an arbitrary real direction.
            cmd.ClearRenderTarget(true, true, new Color(0.5f, 0.5f, 0.5f, 0f));

            var matrix = renderer.transform.localToWorldMatrix;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                cmd.DrawMesh(mesh, matrix, material, sub);

            Graphics.ExecuteCommandBuffer(cmd);

            // Route through the same proven Blit+ReadPixels helper used for dirTex (see this
            // method's own doc comment) rather than reading pixels directly off the DrawMesh RT.
            return BlitReadableAtlas(rawRt, width, height);
        }
        finally
        {
            if (rawRt != null)
                RenderTexture.ReleaseTemporary(rawRt);
            if (cmd != null)
                cmd.Release();
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    /// <summary>
    /// Loads <paramref name="decodedColor"/>'s persisted PNG file bytes directly
    /// (File.ReadAllBytes + Texture2D.LoadImage) instead of Blit-ing it through a RenderTexture,
    /// avoiding any Y-origin ambiguity a Blit readback could introduce. Also sidesteps
    /// decodedColor's TextureImporter likely having isReadable = false, since LoadImage decodes
    /// into a brand-new, always-CPU-readable Texture2D independent of import settings.
    ///
    /// The PNG is an 8-bit sRGB "Color" asset (LightmapDecoder.DecodeAndSave), so
    /// Texture2D.GetPixels() returns raw gamma-encoded values with no implicit sRGB->linear
    /// conversion; this method converts every pixel back to linear (Color.linear) before
    /// returning so downstream combine math still receives the values it expects.
    /// </summary>
    static Texture2D LoadReadableColorAtlas(Texture2D decodedColor)
    {
        var assetPath = AssetDatabase.GetAssetPath(decodedColor);

        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogWarning("[ResoniteSDK] DirectionalLightmapBaker: the decoded color lightmap has no on-disk asset path; cannot load it for the directional-bake pass.");
            return null;
        }

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(Path.GetFullPath(assetPath));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ResoniteSDK] DirectionalLightmapBaker: failed to read \"{assetPath}\" for the directional-bake pass: {ex}.");
            return null;
        }

        var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);

        if (!loaded.LoadImage(bytes))
        {
            Debug.LogWarning($"[ResoniteSDK] DirectionalLightmapBaker: Texture2D.LoadImage failed for \"{assetPath}\".");
            UnityEngine.Object.DestroyImmediate(loaded);
            return null;
        }

        var pixels = loaded.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = pixels[i].linear;

        loaded.SetPixels(pixels);
        loaded.Apply();

        return loaded;
    }

    /// <summary>
    /// Replicates LightmapDecoder.DecodeAndSave's Graphics.Blit(source, rt, material) +
    /// whole-image ReadPixels sequence for <paramref name="source"/>, rather than a material-less
    /// Graphics.Blit(source, rt) call. Measured on this project's D3D11 Editor target: the
    /// material-less overload does not reliably preserve the bottom-origin pixel orientation the
    /// material-based Blit gets from its vert()'s UnityObjectToClipPos automatic Y-compensation.
    ///
    /// Uses RawPassthroughBlit.shader (same vert() as LightmapDecode.shader, but a pure
    /// passthrough frag()) because LightmapDecode.shader's frag() forces alpha to 1, which would
    /// destroy dirTex's own alpha channel — needed as DecodeDirectionalLightmap's denominator.
    ///
    /// Reads with no gamma/sRGB decode (Linear RT + linear:true readback), since this handles
    /// non-color direction/normal data, not albedo. Caller owns/destroys the returned texture.
    /// Also used by RenderUv2NormalPatch for its own DrawMesh-rendered RenderTexture, so both
    /// share this one proven orientation path.
    /// </summary>
    static Texture2D BlitReadableAtlas(Texture source, int atlasWidth, int atlasHeight)
    {
        var shader = Shader.Find(RawPassthroughShaderName);

        if (shader == null)
        {
            Debug.LogWarning($"[ResoniteSDK] DirectionalLightmapBaker: could not find shader \"{RawPassthroughShaderName}\".");
            return null;
        }

        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture rt = null;
        RenderTexture previousActive = null;
        Texture2D readable = null;

        try
        {
            rt = RenderTexture.GetTemporary(atlasWidth, atlasHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            // Material-based 3-arg Blit — see this method's doc comment for why the argument-less
            // 2-arg overload doesn't preserve orientation here.
            Graphics.Blit(source, rt, material);

            previousActive = RenderTexture.active;
            RenderTexture.active = rt;

            readable = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBAFloat, false, true);
            readable.ReadPixels(new Rect(0, 0, atlasWidth, atlasHeight), 0, 0);
            readable.Apply();

            return readable; // Caller owns/destroys this.
        }
        finally
        {
            if (previousActive != null || rt != null)
                RenderTexture.active = previousActive;
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    /// <summary>
    /// Reads this renderer's <paramref name="tileWidth"/> x <paramref name="tileHeight"/> atlas
    /// footprint starting at (<paramref name="tileX"/>, <paramref name="tileY"/>). Per
    /// Renderer.lightmapScaleOffset's semantics, this rect can legitimately land partly outside
    /// the atlas bounds — real measured Unity output has produced offsets like
    /// (0.24, 0.24, -0.01, -0.01).
    ///
    /// Naively clamping tileX/tileY to the atlas bounds while leaving tileWidth/tileHeight
    /// unclamped would slide the read rect's origin without shrinking it, importing an unrelated
    /// same-size chunk of neighboring atlas content instead of this renderer's own tile. Instead,
    /// tileWidth/tileHeight (and the returned array's size) always stay the renderer's true,
    /// unclamped footprint — callers depend on that 1:1 uv2<->pixel mapping — and only the
    /// per-pixel atlas SAMPLE coordinates get clamped to the nearest in-bounds texel
    /// (clamp-to-edge) where the true rect falls outside the atlas.
    /// </summary>
    static Color[] ReadAtlasRegionClamped(Texture2D atlas, int atlasWidth, int atlasHeight, int tileX, int tileY, int tileWidth, int tileHeight)
    {
        // Sub-rect of the requested tile that's actually inside the atlas bounds. Guaranteed
        // non-empty even for a tile rect entirely outside the atlas.
        int readX = Mathf.Clamp(tileX, 0, Mathf.Max(0, atlasWidth - 1));
        int readY = Mathf.Clamp(tileY, 0, Mathf.Max(0, atlasHeight - 1));
        int readRight = Mathf.Clamp(tileX + tileWidth, readX + 1, atlasWidth);
        int readTop = Mathf.Clamp(tileY + tileHeight, readY + 1, atlasHeight);
        int readWidth = readRight - readX;
        int readHeight = readTop - readY;

        var validPixels = atlas.GetPixels(readX, readY, readWidth, readHeight);

        // Fast path: tile was already fully inside the atlas, so the region read above is
        // already the full result.
        if (readX == tileX && readY == tileY && readWidth == tileWidth && readHeight == tileHeight)
            return validPixels;

        // Slow path: place the valid pixels at their correct offset within the full tile,
        // clamp-to-edge extending into whatever border the atlas-bounds clamp introduced.
        var dest = new Color[tileWidth * tileHeight];
        int offsetX = readX - tileX;
        int offsetY = readY - tileY;

        for (int y = 0; y < tileHeight; y++)
        {
            int sy = Mathf.Clamp(y - offsetY, 0, readHeight - 1);
            int destRow = y * tileWidth;
            int srcRow = sy * readWidth;

            for (int x = 0; x < tileWidth; x++)
            {
                int sx = Mathf.Clamp(x - offsetX, 0, readWidth - 1);
                dest[destRow + x] = validPixels[srcRow + sx];
            }
        }

        return dest;
    }

    /// <summary>
    /// Gamma-encodes (matching LightmapDecoder.DecodeAndSave's convention, since the output is
    /// imported as an 8-bit sRGB "Color" texture and the GPU undoes this on sample) and persists
    /// <paramref name="combinedLinear"/> as a new PNG asset at <paramref name="path"/>.
    /// </summary>
    static Texture2D SaveCombinedTexture(string path, int width, int height, Color[] combinedLinear, string sourceHash)
    {
        var pixels = new Color[combinedLinear.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = combinedLinear[i];
            var clamped = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            pixels[i] = clamped.gamma;
        }

        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        tex.SetPixels(pixels);
        tex.Apply();

        byte[] png;
        try
        {
            png = tex.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(tex);
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllBytes(fullPath, png);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
        {
            Debug.LogError($"[ResoniteSDK] DirectionalLightmapBaker: failed to import combined lightmap PNG at {path}");
            return null;
        }

        importer.sRGBTexture = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.userData = sourceHash;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
