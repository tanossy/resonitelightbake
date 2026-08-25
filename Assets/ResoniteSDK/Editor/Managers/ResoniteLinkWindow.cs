using ResoniteLink;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ResoniteLinkWindow : EditorWindow
{
    public enum ConnectionMethod
    {
        AutoDiscovery,
        Manual
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Initializing,
        Connected,
    }

    public int Port;

    ConnectionMethod _connectionMethod;

    public ConnectionState State
    {
        get
        {
            if(_linkInterface != null)
            {
                if (_linkInterface.IsConnected)
                {
                    if (_uniqueSessionId == null)
                        return ConnectionState.Initializing;
                    else
                        return ConnectionState.Connected;
                }
                else if (_connecting)
                    return ConnectionState.Connecting;
            }

            return ConnectionState.Disconnected;
        }
    }

    [SerializeField]
    public bool ConvertSkybox = true;

    [SerializeField]
    public bool LogMessageJSON;

    public string UniqueSessionId => _uniqueSessionId;

    public LinkInterface Link => _linkInterface;

    LinkSessionListener _listener;
    List<ResoniteLinkSession> _sessions = new List<ResoniteLinkSession>();

    [SerializeField]
    SceneConverter _converter;

    string _lastConnectionError;
    LinkInterface _linkInterface;
    bool _connecting;

    string _resoniteVersion;
    string _resoniteLinkVersion;
    string _uniqueSessionId;

    string _lastUniqueSessionId;
    int _lastPort;

    [MenuItem("Resonite SDK/Open Resonite SDK Manager")]
    public static void ShowWindow()
    {
        ResoniteLinkWindow window = GetWindow<ResoniteLinkWindow>();
        window.titleContent = new GUIContent("Resonite SDK");
    }

    void OnGUI()
    {
        if(_listener == null)
        {
            _listener = new LinkSessionListener();
            _listener.SessionDiscovered += SessionDiscovered;
            _listener.SessionUpdated += SessionUpdated;
            _listener.SessionClosed += SessionClosed;
            _listener.Start();
        }

        EnsureConverter();

        var connectionMethodNames = Enum.GetNames(typeof(ConnectionMethod));

        GUILayout.Label($"⚠️ CAUTION!!! ⚠️\n\n" +
            $"The SDK is currently in beta and missing a lot of converters.\n" +
            $"If you want a smooth experience, consider giving it a few more weeks until we remove this notice.\n\n" +
            $"Before installing a new version of the SDK, please delete the ResoniteSDK folder first!");

        GUI.enabled = State == ConnectionState.Disconnected;

        _connectionMethod = (ConnectionMethod)GUILayout.SelectionGrid((int)_connectionMethod, 
            connectionMethodNames, connectionMethodNames.Length);

        switch(_connectionMethod)
        {
            case ConnectionMethod.AutoDiscovery:
                _sessions.Clear();
                _listener.GetDiscoveredSessions(_sessions);

                if (_sessions.Count == 0)
                    GUILayout.Label("No ResoniteLink sessions found");
                else
                {
                    foreach(var session in _sessions)
                        if(GUILayout.Button($"Connect to session \"{session.SessionName}\" (port: {session.LinkPort})"))
                        {
                            Port = session.LinkPort;
                            ConnectPressed();
                        }
                }
                break;

            case ConnectionMethod.Manual:
                Port = EditorGUILayout.IntField("Port: ", Port);

                GUI.enabled = State != ConnectionState.Connecting;

                if (GUILayout.Button(ConnectButtonLabel))
                    ConnectPressed();
                break;
        }        

        GUI.enabled = true;
        EditorGUILayout.LabelField($"ResoniteLinkStatus: ", StatusLabel);

        if (!string.IsNullOrEmpty(_lastConnectionError))
            GUILayout.Label($"ERROR: {_lastConnectionError}");

        GUILayout.Space(16);

        // The current scene can only be sent when the realtime mode isn't active
        GUI.enabled = State == ConnectionState.Connected && !_converter.IsRealtimeModeActive;

        ConvertSkybox = GUILayout.Toggle(ConvertSkybox, "Convert Skybox");

        // "Force Refresh Generated Lightmaps" and "Send Tonemap Compensation (experimental)"
        // (overlay additions, unlike Convert Skybox above which is vanilla Resonite.UnitySDK)
        // moved to the Lightmap Pipeline panel's "Send-Time Options" section
        // (LightmapPipelineWindow.cs's DrawSendTimeOptionsSection()), keeping this panel limited
        // to what's actually original. Backing state (ConversionPassState
        // .ForceRefreshGeneratedLightmaps / ToneMapCompensationState.Enabled) unchanged, just no
        // longer written from here.

        if (GUILayout.Button("Send Current Scene"))
            SendCurrentScene();

        GUI.enabled = State == ConnectionState.Connected;

        if (!(_converter?.IsRealtimeModeActive ?? false))
        {
            if (GUILayout.Button("Start Realtime Mode"))
                StartRealtimeMode();
        }
        else
        {
            if (GUILayout.Button("STOP Realtime Mode"))
                StopRealtimeMode();
        }

        GUI.enabled = true;

        // The "Meshes/Materials/Lightmaps Only" sends, "Retry Missing Asset URLs", and the whole
        // DEBUGGING section (Cleanup tools, Reset conversion state, Log Messages JSON) live
        // outside this official-looking panel rather than in it. Only the always-used controls
        // (Connect, Send Current Scene, Realtime Mode) remain here.
        //
        // They first landed in a standalone ResoniteSDKDebugWindow.cs (menu: Resonite SDK/Open
        // Debug Tools), but that window turned out redundant with LightmapPipelineWindow.cs
        // (menu: Resonite SDK/Lightmap Pipeline) and has since been deleted - its buttons now
        // live in that window's "Debug / Cleanup" section instead, called via
        // LightmapTestHarness.cs's RetryMissingAssetURLs()/ResetConversionState()/
        // LogMessageJSON, same as its other sections. This window's own public passthrough
        // methods below (SendMeshesOnly/SendMaterialsOnly/SendLightmapsOnly/
        // RetryMissingAssetURLs/ResetConversionState) are unaffected by that move - they're
        // still invoked via reflection through LightmapTestHarness.InvokeConnectedSdkSend(),
        // same as before. CleanupConverters() is not called this way at all - it's only ever
        // invoked internally, from ResetConversionState() itself (see that method's own
        // comment below); it never had (and still doesn't have) a standalone button.
    }

    // Force an update, which should refresh the UI
    void SessionClosed(ResoniteLinkSession obj) => EditorApplication.QueuePlayerLoopUpdate();
    void SessionUpdated(ResoniteLinkSession obj) => EditorApplication.QueuePlayerLoopUpdate();
    void SessionDiscovered(ResoniteLinkSession obj) => EditorApplication.QueuePlayerLoopUpdate();

    void EnsureConverter()
    {
        if (State != ConnectionState.Connected)
            return;

        if(_converter != null)
        {
            if(_converter.IsCorrupted)
            {
                Debug.Log("Conversion state potentially corrupted due to erorrs. Resetting conversion state");
                ResetConversionState();
                return;
            }

            if (UniqueSessionId != _lastUniqueSessionId || _lastPort != Port)
            {
                Debug.Log("Connected to a different ResoniteLink session. Resetting conversion state.\n" +
                    $"PrevID: {_lastUniqueSessionId}, NewID: {UniqueSessionId}, PrevPort: {_lastPort}, NewPort: {Port}");

                ResetConversionState();
                return;
            }
        }

        if (_converter == null)
            _converter = new SceneConverter();

        _converter.EnsureInitialized(this);

        _lastUniqueSessionId = UniqueSessionId;
        _lastPort = Port;
    }

    void SendCurrentScene()
    {
        EnsureConverter();

        _converter.ConvertScene();
    }

    public void SendMeshesOnly()
    {
        EnsureConverter();

        _converter.ConvertMeshesOnly();
    }

    public void SendMaterialsOnly()
    {
        EnsureConverter();

        _converter.ConvertMaterialsOnly();
    }

    public void SendLightmapsOnly()
    {
        EnsureConverter();

        _converter.ConvertLightmapsOnly();
    }

    public void RetryMissingAssetURLs()
    {
        EnsureConverter();

        _converter.RetryMissingAssetURLs();
    }

    void StartRealtimeMode()
    {
        EnsureConverter();

        _converter.StartRealtimeMode();
    }

    void StopRealtimeMode()
    {
        _converter.StopRealtimeMode();
    }

    void ConnectPressed()
    {
        _lastConnectionError = null;

        if (State == ConnectionState.Disconnected)
        {
            // Cleanup any previous interface if it exists
            _linkInterface?.Dispose();

            _resoniteLinkVersion = null;
            _resoniteVersion = null;
            _uniqueSessionId = null;

            _connecting = true;
            _linkInterface = new LinkInterface();

            Task.Run(async () =>
            {
                try
                {
                    await _linkInterface.Connect(new System.Uri($"ws://localhost:{Port}"), System.Threading.CancellationToken.None);

                    var sessionInfo = await _linkInterface.GetSessionData();

                    if (!sessionInfo.Success)
                        throw new System.Exception($"Error getting session info: {sessionInfo.ErrorInfo}");

                    _resoniteLinkVersion = sessionInfo.ResoniteLinkVersion;
                    _resoniteVersion = sessionInfo.ResoniteVersion;

                    _uniqueSessionId = sessionInfo.UniqueSessionId;
                }
                catch(System.Exception ex)
                {
                    _lastConnectionError = ex.Message;

                    // Cleanup
                    _linkInterface.Dispose();
                    _linkInterface = null;
                }
                finally
                {
                    _connecting = false;
                }
            });
        }
        else
        {
            var __linkInterface = _linkInterface;
            _linkInterface = null;

            Task.Run(async () =>
            {
                __linkInterface.Dispose();
            });
        }
    }

    public void CleanupConverters()
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (var root in roots)
            foreach (var converter in root.GetComponentsInChildren<ResoniteComponentConverter>())
                DestroyImmediate(converter);
    }

    public void CleanupReosniteComponents()
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (var root in roots)
            foreach (var component in root.GetComponentsInChildren<ResoniteComponent>())
                DestroyImmediate(component);
    }

    public void ResetConversionState()
    {
        // Recreating _converter alone is not sufficient: converters already living under the
        // __UnityAssets / __UnitySkybox roots (Texture2DConverter/MeshConverter/AudioClipConverter
        // etc.) persist in the scene, and AssetConversionManager's constructor scans and reuses the
        // contents of a root with the same name if one already exists. Because these converters
        // still hold IDs issued by the previous session, a new session (whether UniqueSessionId
        // changed, or the connection dropped and reconnected) would see them as pointing to
        // components that no longer exist, causing failures like "Component with ID '...' not
        // found", and could leave converters duplicated across sessions. CleanupConverters() and
        // CleanupAssetConversionRoots() destroy the full set of converter components (both the
        // scene hierarchy side and the asset side) before _converter is recreated, so the next
        // conversion starts from a clean state.
        //
        // CleanupReosniteComponents() is deliberately not called here: every ResoniteComponent on
        // the scene hierarchy side is already destroyed via
        // ResoniteComponentConverter.OnDestroy() -> Cleanup() -> DestroyImmediate(Binding), which
        // CleanupConverters()'s DestroyImmediate(converter) triggers, and the asset-side
        // ResoniteComponent(Provider) is destroyed as a child when CleanupAssetConversionRoots()
        // removes __UnityAssets/__UnitySkybox entirely. Calling CleanupReosniteComponents()
        // afterwards would re-collect and try to destroy the same, already-destroyed components,
        // producing "Destroying object multiple times" warnings.
        CleanupConverters();
        CleanupAssetConversionRoots();

        _converter = null;
        EnsureConverter();
    }

    // __UnityAssets / __UnitySkybox are designed to "reuse the contents if a GameObject with the
    // same name already exists" (AssetConversionManager's constructor, SkyboxConverter.EnsureRoot()),
    // so simply removing individual components via CleanupConverters()/CleanupReosniteComponents()
    // isn't enough to prevent the accident where "converters from the previous session get
    // rescanned and resurrected into the empty container" - the root GameObject itself must be
    // destroyed too.
    void CleanupAssetConversionRoots()
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (var root in roots)
        {
            if (root.name == AssetConversionManager.ASSETS_ROOT_NAME ||
                root.name == SkyboxConverter.SKYBOX_ROOT_NAME)
            {
                DestroyImmediate(root);
            }
        }
    }

    string ConnectButtonLabel => State switch
    {
        ConnectionState.Disconnected => "Connect",
        ConnectionState.Connecting => "Connecting...",
        ConnectionState.Initializing => "Initializing...",
        ConnectionState.Connected => "Disconnect",
    };

    string StatusLabel => State switch
    {
        ConnectionState.Disconnected => "Disconnected",
        ConnectionState.Connecting => $"Connecting to port {Port}...",
        ConnectionState.Initializing => $"Initializing session...",
        ConnectionState.Connected => $"Connected on port {Port} (Resonite: {_resoniteVersion})"
    };
}
