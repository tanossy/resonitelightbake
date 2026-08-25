using BepuPhysics.Collidables;
using FrooxEngine;
using ResoniteLink;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public struct AssetMap<A> : IEquatable<AssetMap<A>>
    where A : UnityEngine.Object
{
    public readonly A Asset;
    public readonly AssetMessagePostProcessor PostProcessor;

    // Identity is keyed on GUID + local file ID, not simpler alternatives. Reference identity alone
    // breaks when a file is deleted and regenerated at the same path (e.g.
    // ForceRefreshGeneratedLightmaps): the logical asset is the same but the reference differs, so
    // lookups miss the existing Converter/Resonite-side ID and a duplicate gets sent. Path identity
    // alone is also wrong: multiple sub-assets from one file (e.g. several Meshes in one .fbx) share
    // the same AssetDatabase.GetAssetPath(), so path-only identity conflates them and a later
    // sub-asset overwrites an earlier converter's Source. GUID + local file ID (Unity's own stable
    // per-sub-asset ID pair) distinguishes both cases correctly. Procedural assets (no GUID/path)
    // fall back to reference identity.
    readonly string _path;

    public AssetMap(A asset, AssetMessagePostProcessor postProcessor)
    {
        this.Asset = asset;
        PostProcessor = postProcessor;

        if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long localId)
            && !string.IsNullOrEmpty(guid))
        {
            _path = $"{guid}:{localId}";
        }
        else
        {
            _path = null;
        }
    }

    public bool Equals(AssetMap<A> other)
    {
        if (PostProcessor != other.PostProcessor)
            return false;

        if (_path != null && other._path != null)
            return _path == other._path;

        // No stable path on at least one side (procedural asset, or a null). A path vs. no-path
        // pairing must never match (a procedural asset must never alias a persisted one), so fall
        // back to reference identity.
        return _path == null && other._path == null && Asset == other.Asset;
    }

    public override int GetHashCode() =>
        _path != null
            ? HashCode.Combine(_path, PostProcessor)
            : HashCode.Combine(Asset, PostProcessor);
}

public class AssetConversionManager
{
    public const string ASSETS_ROOT_NAME = "__UnityAssets";

    public SceneConverter Converter { get; private set; }
    public Transform AssetsRoot { get; private set; }

    public bool HasPendingChanges => _scheduledConversions.Count > 0 || _updatedAssetProviderRoots.Count > 0;

    Dictionary<AssetMap<UnityEngine.Mesh>, MeshConverter> _meshes = new Dictionary<AssetMap<UnityEngine.Mesh>, MeshConverter>();
    Dictionary<AssetMap<UnityEngine.Texture2D>, Texture2DConverter> _textures = new Dictionary<AssetMap<UnityEngine.Texture2D>, Texture2DConverter>();
    Dictionary<AssetMap<UnityEngine.Cubemap>, CubemapConverter> _cubemaps = new Dictionary<AssetMap<UnityEngine.Cubemap>, CubemapConverter>();
    Dictionary<AssetMap<UnityEngine.AudioClip>, AudioClipConverter> _audioClips = new Dictionary<AssetMap<UnityEngine.AudioClip>, AudioClipConverter>();

    Dictionary<UnityEngine.Material, ResoniteMaterialConverter> _materials = new Dictionary<UnityEngine.Material, ResoniteMaterialConverter>();
    Dictionary<UnityEngine.Material, FrooxEngine.IAssetProvider<FrooxEngine.Material>> _cachedMaterials = new Dictionary<UnityEngine.Material, IAssetProvider<FrooxEngine.Material>>();

    HashSet<AssetConverter> _checkedConverters = new HashSet<AssetConverter>();
    Queue<AssetConverter> _scheduledConversions = new Queue<AssetConverter>();

    HashSet<Transform> _updatedAssetProviderRoots = new HashSet<Transform>();

    public IEnumerable<Transform> UpdatedAssetProviderRoots => _updatedAssetProviderRoots;

    public AssetConversionManager(SceneConverter converter)
    {
        Converter = converter;

        var roots = SceneManager.GetActiveScene().GetRootGameObjects();

        AssetsRoot = roots.FirstOrDefault(r => r.name == ASSETS_ROOT_NAME)?.transform;

        if(AssetsRoot != null)
        {
            ScanConverters<StaticMesh, StaticMeshWrapper, UnityEngine.Mesh, FrooxEngine.Mesh, MeshConverter>(_meshes);
            ScanConverters<StaticTexture2D, StaticTexture2DWrapper, UnityEngine.Texture2D, FrooxEngine.Texture2D, Texture2DConverter>(_textures);
            ScanConverters<StaticCubemap, StaticCubemapWrapper, UnityEngine.Cubemap, FrooxEngine.Cubemap, CubemapConverter>(_cubemaps);
            ScanConverters<StaticAudioClip, StaticAudioClipWrapper, UnityEngine.AudioClip, FrooxEngine.AudioClip, AudioClipConverter>(_audioClips);

            // Materials use their own scan/cache (keyed on Material alone, no AssetMap/postprocessor)
            ScanMaterials();
        }
        else
            AssetsRoot = (new GameObject(ASSETS_ROOT_NAME)).transform;
    }

    void ScanConverters<TProvider, TWrapper, TUnity, TResonite, TConverter>(Dictionary<AssetMap<TUnity>, TConverter> map)
        where TProvider : FrooxEngine.Component, IAssetProvider<TResonite>, new()
        where TWrapper : ResoniteComponent<TProvider>
        where TResonite : FrooxEngine.IAsset
        where TConverter : AssetConverter<TWrapper, TProvider, TUnity, TResonite>
        where TUnity : UnityEngine.Object
    {
        var converters = AssetsRoot.GetComponentsInChildren<TConverter>();

        foreach (var converter in converters)
        {
            if (converter.Source == null || converter.Provider == null)
            {
                // TODO!!! Cleanup?
                continue;
            }

            var key = new AssetMap<TUnity>(converter.Source, converter.PostProcessor);

            if (map.TryGetValue(key, out var existing))
            {
                if (existing.HasMissingProviderURL() && !converter.HasMissingProviderURL())
                    map[key] = converter;

                Debug.LogWarning($"[ResoniteSDK] Duplicate asset converter found for {converter.Source.name}. " +
                    "Keeping one converter and ignoring the duplicate during scan.");
                continue;
            }

            map.Add(key, converter);
        }
    }

    void ScanMaterials()
    {
        var converters = AssetsRoot.GetComponentsInChildren<ResoniteMaterialConverter>();

        foreach (var converter in converters)
        {
            if (converter.Source == null)
            {
                // TODO!!! Cleanup?
                continue;
            }

            _materials.Add(converter.Source, converter);
        }
    }

    public void BeginConversion()
    {
        // Materials (unlike other asset types) can change between conversions - e.g. parameter
        // tweaks - so clear the cache to force re-conversion every run.
        _cachedMaterials.Clear();

        // Re-check every converter each batch, since assets may have changed since the last run.
        _checkedConverters.Clear();
    }

    public int ScheduleMissingAssetURLRetries()
    {
        if (AssetsRoot == null)
            return 0;

        int scheduled = 0;

        foreach (var converter in AssetsRoot.GetComponentsInChildren<AssetConverter>(true))
        {
            if (converter == null || !converter.CanRetryMissingProviderURL())
                continue;

            if (_scheduledConversions.Contains(converter))
                continue;

            _scheduledConversions.Enqueue(converter);
            _updatedAssetProviderRoots.Add(converter.transform);
            scheduled++;
        }

        return scheduled;
    }

    public int RetryMissingAssetURLs(LinkInterface link)
    {
        var scheduled = ScheduleMissingAssetURLRetries();

        if (scheduled > 0)
            ProcessConversions(link);

        return scheduled;
    }

    public bool HasMesh(UnityEngine.Mesh mesh, AssetMessagePostProcessor postProcessor = null) =>
        _meshes.ContainsKey(new AssetMap<UnityEngine.Mesh>(mesh, postProcessor));

    public bool HasTexture2D(UnityEngine.Texture2D texture2D, AssetMessagePostProcessor postProcessor = null) =>
        _textures.ContainsKey(new AssetMap<UnityEngine.Texture2D>(texture2D, postProcessor));
    public bool HasCubemap(UnityEngine.Cubemap cubemap, AssetMessagePostProcessor postProcessor = null) =>
        _cubemaps.ContainsKey(new AssetMap<UnityEngine.Cubemap>(cubemap, postProcessor));
    public bool HasAudioClip(UnityEngine.AudioClip audioClip, AssetMessagePostProcessor postProcessor = null) => 
        _audioClips.ContainsKey(new AssetMap<UnityEngine.AudioClip>(audioClip, postProcessor));
    public bool HasMaterial(UnityEngine.Material material, AssetMessagePostProcessor postProcessor = null) => _materials.ContainsKey(material);

    public IAssetProvider<FrooxEngine.Mesh> GetMesh(UnityEngine.Mesh mesh, AssetMessagePostProcessor postProcessor = null) =>
        GetAsset<StaticMesh, StaticMeshWrapper, UnityEngine.Mesh, FrooxEngine.Mesh, MeshConverter>(
            mesh, postProcessor, _meshes);

    public IAssetProvider<FrooxEngine.Texture2D> GetTexture2D(UnityEngine.Texture2D texture, AssetMessagePostProcessor postProcessor = null) =>
        GetAsset<StaticTexture2D, StaticTexture2DWrapper, UnityEngine.Texture2D, FrooxEngine.Texture2D, Texture2DConverter>(
            texture, postProcessor, _textures);

    public IAssetProvider<FrooxEngine.Cubemap> GetCubemap(UnityEngine.Cubemap cubemap, AssetMessagePostProcessor postProcessor = null) =>
        GetAsset<StaticCubemap, StaticCubemapWrapper, UnityEngine.Cubemap, FrooxEngine.Cubemap, CubemapConverter>(
            cubemap, postProcessor, _cubemaps);

    public IAssetProvider<FrooxEngine.AudioClip> GetAudioClip(UnityEngine.AudioClip audioClip, AssetMessagePostProcessor postProcessor = null) =>
        GetAsset<StaticAudioClip, StaticAudioClipWrapper, UnityEngine.AudioClip, FrooxEngine.AudioClip, AudioClipConverter>(
            audioClip, postProcessor, _audioClips);

    TProvider GetAsset<TProvider, TWrapper, TUnity, TResonite, TConverter>(TUnity unity, AssetMessagePostProcessor postProcessor,
        Dictionary<AssetMap<TUnity>, TConverter> converters)
        where TProvider : FrooxEngine.Component, IAssetProvider<TResonite>, new()
        where TWrapper : ResoniteComponent<TProvider>
        where TResonite : FrooxEngine.IAsset
        where TConverter : AssetConverter<TWrapper, TProvider, TUnity, TResonite>
        where TUnity : UnityEngine.Object
    {
        if (unity == null)
            throw new ArgumentNullException(nameof(unity));

        bool needsToConvert = false;

        var identity = new AssetMap<TUnity>(unity, postProcessor);

        if (!converters.TryGetValue(identity, out var converter))
        {
            var go = new GameObject();
            go.transform.parent = AssetsRoot;

            converter = go.AddComponent<TConverter>();
            converter.Initialize(unity, postProcessor);

            needsToConvert = true;

            converters.Add(identity, converter);
        }
        else
        {
            // Live reference may differ even though identity matched on the stable path (see
            // AssetMap's doc comment) - re-point Source before checking for changes so
            // HasAssetChanged() isn't evaluating a stale/destroyed object, and the resulting update
            // targets the already-sent Resonite-side component instead of spawning a duplicate.
            if (!ReferenceEquals(converter.Source, unity))
                converter.Source = unity;

            if (_checkedConverters.Add(converter) && converter.HasAssetChanged())
                needsToConvert = true;
        }

        if (needsToConvert)
        {
            _scheduledConversions.Enqueue(converter);

            _updatedAssetProviderRoots.Add(converter.Provider.transform);
        }

        return converter.Provider.Data;
    }

    public IAssetProvider<FrooxEngine.Material> GetMaterial(UnityEngine.Material material)
    {
        // Reuse this run's result if we've already resolved this material (separate from _materials,
        // which persists the converter itself across runs)
        if (_cachedMaterials.TryGetValue(material, out var provider))
            return provider;

        if (!_materials.TryGetValue(material, out var converter))
        {
            var converterType = MaterialConverterRepository.TryGetConverter(material);

            if(converterType == null)
            {
                Debug.LogWarning($"Unable to convert material {material}. Shader: {material.shader?.name}");

                // Cache the null result too, so we don't repeat this failed lookup on every request
                converter = null;
            }
            else
            {
                var root = new GameObject($"Material - {material.name}");
                root.transform.parent = AssetsRoot;

                converter = (ResoniteMaterialConverter)root.AddComponent(converterType);
                converter.Source = material;

                // Stored across conversions since materials are usually updated in place rather than
                // recreated
                _materials.Add(material, converter);
            }
        }

        // UpdateConversion() can swap the actual material instance depending on its properties, hence
        // re-fetching the provider here; converter may be null, hence the null-conditional
        provider = converter?.UpdateConversion(material, Converter);

        if (provider != null)
            _updatedAssetProviderRoots.Add(converter.transform);

        _cachedMaterials.Add(material, provider);

        return provider;
    }

    public void ProcessConversions(LinkInterface link)
    {
        // EditorUtility.DisplayProgressBar/ClearProgressBar is intentionally omitted from this
        // loop: it pumps GUI events on the Editor main thread while job.Convert() blocks that same
        // thread on an async WebSocket send (Task.Run(...).Wait()). ResoniteLink.dll has a known
        // unsynchronized race where ReceiverHandler can misfire "no pending response with this ID"
        // while a send is in flight and call _client.Dispose(); leaving the progress bar out rules
        // it out as a contributor to widening that race's window.
        while (_scheduledConversions.Count > 0)
        {
            if (link == null || !link.IsConnected)
            {
                throw new InvalidOperationException("Asset conversion stopped because ResoniteLink is no longer connected. Reconnect and run Send Current Scene again; conversion state will be rebuilt.");
            }

            var job = _scheduledConversions.Dequeue();

            try
            {
                job.Convert(Converter, link);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Asset conversion stopped while converting {job.AssetClass} {job.AssetName}. ResoniteLink likely disconnected during asset upload. " +
                    "Reconnect and run Send Current Scene again; conversion state will be rebuilt.",
                    ex);
            }
        }

        // Only relevant before conversions run; clear once done
        _updatedAssetProviderRoots.Clear();
    }
}
