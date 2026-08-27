using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Best.SocketIO;
using Best.SocketIO.Events;
using Newtonsoft.Json;

public class SocketIOManager : MonoBehaviour
{
    [SerializeField] private string testToken = "test-token";
    protected string testSocketURL = "https://devrealtime.dingdinghouse.com/";
    protected string nameSpace = "playground";


    [Header("References")]
    [SerializeField] private GameManager gameManager;

    [SerializeField] private UIManager uiManager;
    [SerializeField] private PopupManager popupManager;
    [SerializeField] internal JSFunctCalls JSManager;
    [SerializeField] private GameObject RaycastBlocker;

    private SocketManager socketManager;
    private Socket gameSocket;

    private string authToken;
    private string socketURL;

    internal bool isConnected;
    internal bool isInitialized;
    internal bool isExiting;   // True when CloseSocket is called intentionally (exit button)
    private bool isDestroyed;  // True when scene is unloading or application is quitting
    private bool socketSetupStarted;

    private bool hasFocus = true;
    private float focusLostTime = 0f;
    private Coroutine focusCheckRoutine;
    private float maxBackgroundTime = 60f;

    [Header("Debug Settings")]
    [SerializeField] private bool enablePingDebug = false;

    private Coroutine pingCoroutine;
    private float lastPongTime;
    private float pingSendTime;
    private bool waitingForPong;
    private int missedPongs;
    private const int MAX_MISSED_PONGS = 5;
    private const float PING_INTERVAL = 2f;
    private const float PONG_TIMEOUT = 5f;

    #region Initialization

    private void Awake()
    {
        isInitialized = false;
        isConnected = false;
        isExiting = false;
        isDestroyed = false;
        socketSetupStarted = false;
    }

    private void Start()
    {
        RequestAuthToken();
    }

    private void RequestAuthToken()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (JSManager != null)
        {
            JSManager.SendCustomMessage("authToken");
        }
#else
        authToken = testToken;
        socketURL = testSocketURL;
        InitializeSocket();
#endif
    }

    void ReceiveAuthToken(string jsonData)
    {
        Debug.Log($"[SocketIO] Auth received");

        try
        {
            var authData = JsonUtility.FromJson<AuthTokenData>(jsonData);
            string incomingToken = authData != null ? authData.cookie : authToken;
            string incomingSocketURL = authData != null ? authData.socketURL : socketURL;
            string incomingNameSpace = (authData != null && !string.IsNullOrEmpty(authData.nameSpace)) ? authData.nameSpace : nameSpace;

            // If socket is already initialized and the incoming token matches our current token, bypass re-initialization
            if (socketSetupStarted && authToken == incomingToken && socketURL == incomingSocketURL)
            {
                Debug.LogWarning("[SocketIO] Matching auth token received, bypassing re-initialization.");
                return;
            }

            Debug.Log("[SocketIO] New or updated auth token received. Cleaning up old socket and re-initializing.");
            authToken = incomingToken;
            socketURL = incomingSocketURL;
            nameSpace = incomingNameSpace;

            // Defensive: clean up any existing socket before building a new one
            if (socketManager != null)
            {
                try { socketManager.Close(); } catch { }
                socketManager = null;
            }

            socketSetupStarted = false;
            InitializeSocket();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SocketIO] Auth parse failed: {e.Message}");
        }
    }

    private void InitializeSocket()
    {
        if (socketSetupStarted) return;
        socketSetupStarted = true;

        // Defensive: tear down any prior manager before building a new one
        if (socketManager != null)
        {
            try { socketManager.Close(); } catch { }
            socketManager = null;
        }

        if (RaycastBlocker) RaycastBlocker.SetActive(true);

        SocketOptions options = new SocketOptions
        {
            AutoConnect = false,
            Reconnection = false,
            Timeout = TimeSpan.FromSeconds(3),
            ConnectWith = Best.SocketIO.Transports.TransportTypes.WebSocket
        };

        options.Auth = (SocketManager manager, Socket socket) => new { token = authToken };

#if UNITY_EDITOR
        socketManager = new SocketManager(new Uri(testSocketURL), options);
#else
        socketManager = new SocketManager(new Uri(socketURL), options);
#endif

        gameSocket = string.IsNullOrEmpty(nameSpace)
            ? socketManager.Socket
            : socketManager.GetSocket("/" + nameSpace);

        gameSocket.On<ConnectResponse>(SocketIOEventTypes.Connect, OnSocketConnected);
        gameSocket.On(SocketIOEventTypes.Disconnect, OnSocketDisconnected);
        gameSocket.On<Error>(SocketIOEventTypes.Error, OnSocketError);

        gameSocket.On<string>("game:init", OnInitReceived);
        gameSocket.On<string>("result", OnResultReceived);
        gameSocket.On<string>("pong", OnPongReceived);
        gameSocket.On("pong", OnPongReceivedNoArgs);
        gameSocket.On<string>("AnotherDevice", OnAnotherDevice);
        gameSocket.On<string>("balance:sync", OnBalanceSyncReceived);
        gameSocket.On<string>("jackpot:sync", OnJackpotSyncReceived);

        socketManager.Open();
    }

    #endregion

    #region Socket Events

    private void OnSocketConnected(ConnectResponse resp)
    {
        Debug.Log("[SocketIO] Connected");

        isConnected = true;
        waitingForPong = false;
        missedPongs = 0;
        lastPongTime = Time.time;
        pingSendTime = Time.realtimeSinceStartup;

        if (popupManager != null)
        {
            popupManager.CloseReconnectionPopup();
        }

        StartPingRoutine();
        SendPing();
    }

    private void OnSocketDisconnected()
    {
        Debug.Log("[SocketIO] Disconnected");

        isConnected = false;
        StopPingRoutine();

        if (uiManager != null)
        {
            uiManager.UpdatePingDisplay("-- ms");
        }

        if (isDestroyed)
        {
            // Do not execute UI transitions or popup animations when destroying object / shutting down scene
            return;
        }

        if (isExiting)
        {
            // Intentional exit — show loading popup (animation) instead of disconnect popup
            if (popupManager != null)
            {
                popupManager.ShowLoadingPopup(0f); // 0 = indefinite, JS will reload the page
            }
            // Do NOT call gameManager.OnDisconnected for an intentional exit
        }
        else
        {
            // Unexpected disconnection — show the regular disconnection popup
            if (popupManager != null)
            {
                popupManager.ShowDisconnectionPopup();
            }

            if (gameManager != null)
            {
                gameManager.OnDisconnected();
            }
        }
    }

    private void OnSocketError(Error err)
    {
        Debug.LogError($"[SocketIO] Error: {err.message}");

        if (!gameManager.isInitialized)
        {
            gameManager.initializationFailed = true;
        }

        if (!string.IsNullOrEmpty(err.message) && err.message.Contains("Session expired"))
        {
            Debug.LogWarning("Session expired detected");
            OnSocketDisconnected();
#if UNITY_WEBGL && !UNITY_EDITOR
        JSManager.SendCustomMessage("session_expired");
#endif
        }
        else
        {

            if (popupManager != null)
            {
                popupManager.ShowServerError(err.message);
            }
#if UNITY_WEBGL && !UNITY_EDITOR
        JSManager.SendCustomMessage("error");
#endif
        }
    }

    private void OnInitReceived(string jsonData)
    {
        Debug.Log($"[SocketIO] Init received: {jsonData}");

        try
        {
            var initData = JsonConvert.DeserializeObject<InitData>(jsonData);
            var gameConfig = InitDataConverter.ConvertToGameConfig(initData);
            var playerData = InitDataConverter.ConvertToPlayerData(initData.player);
            var initialMatrix = GenerateRandomMatrix(gameConfig.rowCount);

            isInitialized = true;

            gameManager.OnInitDataReceived(gameConfig, playerData, initialMatrix);

            if (initData.jackpotData != null && initData.jackpotData.values != null && uiManager != null)
            {
                uiManager.UpdateJackpotDisplay(initData.jackpotData.values);
            }

            if (RaycastBlocker) RaycastBlocker.SetActive(false);

#if UNITY_WEBGL && !UNITY_EDITOR
            if (JSManager != null)
            {
                JSManager.SendCustomMessage("OnEnter");
            }
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"[SocketIO] Init parse failed: {e.Message}");
            gameManager.initializationFailed = true;
            if (popupManager != null)
            {
                popupManager.ShowServerError("Failed to parse game initialization data.");
            }
        }
    }

    private void OnResultReceived(string jsonData)
    {
        if (!jsonData.Contains("\"id\":\"ResultData\""))
        {
            return;
        }

        Debug.Log($"[SocketIO] Result received: {jsonData}");

        try
        {
            var serverResponse = JsonConvert.DeserializeObject<ServerSpinResponse>(jsonData);

            if (!serverResponse.success)
            {
                Debug.LogError("[SocketIO] Spin failed");
                return;
            }

            double currentBalance = gameManager.playerData.balance;
            double betAmount = gameManager.currentBetAmount;
            GameConfig gameConfig = gameManager.gameConfig;

            SpinResult result = InitDataConverter.ConvertServerResponseToSpinResult(
                serverResponse,
                currentBalance,
                betAmount,
                gameConfig
            );

            result.playerData.currentBetIndex = gameManager.currentBetIndex;

            gameManager.OnSpinResultReceived(result);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SocketIO] Result parse failed: {e.Message}");
        }
    }

    private void OnAnotherDevice(string data)
    {
        Debug.Log("[SocketIO] Another device login");

        if (popupManager != null)
        {
            popupManager.ShowAnotherDeviceError();
        }
    }

    private void OnBalanceSyncReceived(string jsonData)
    {
        Debug.Log($"[SocketIO] Balance Sync received: {jsonData}");

        try
        {
            var syncData = JsonConvert.DeserializeObject<BalanceSyncData>(jsonData);
            
            if (gameManager != null && gameManager.playerData != null)
            {
                gameManager.playerData.balance = syncData.balance;
                
                if (uiManager != null)
                {
                    uiManager.UpdateBalanceDisplay();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SocketIO] Balance Sync parse failed: {e.Message}");
        }
    }

    private void OnJackpotSyncReceived(string jsonData)
    {
        Debug.Log($"[SocketIO] Jackpot Sync received: {jsonData}");

        try
        {
            var syncData = JsonConvert.DeserializeObject<JackpotSyncData>(jsonData);

            if (syncData != null && syncData.values != null && uiManager != null)
            {
                uiManager.UpdateJackpotDisplay(syncData.values);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SocketIO] Jackpot Sync parse failed: {e.Message}");
        }
    }

    #endregion

    internal void SetRaycastBlocker(bool active)
    {
        if (RaycastBlocker != null) RaycastBlocker.SetActive(active);
    }

    #region Focus / Background Timeout
    internal void HandleFocusChange(bool focus)
    {
        hasFocus = focus;

        if (!focus)
        {
            focusLostTime = Time.time;
            if (focusCheckRoutine == null && !isExiting && !isDestroyed)
                focusCheckRoutine = StartCoroutine(FocusTimeoutCheck());
        }
        else
        {
            if (focusCheckRoutine != null)
            {
                StopCoroutine(focusCheckRoutine);
                focusCheckRoutine = null;
            }
        }
    }

    private IEnumerator FocusTimeoutCheck()
    {
        while (!hasFocus && !isExiting && !isDestroyed)
        {
            if (Time.time - focusLostTime >= maxBackgroundTime)
            {
                Debug.LogWarning("[SOCKET] Background timeout — closing connection");
                isConnected = false;
                StopPingRoutine();

                if (socketManager != null)
                {
                    try { socketManager.Close(); }
                    catch (Exception e) { Debug.LogWarning($"[SOCKET] Focus close error: {e.Message}"); }
                }

                if (popupManager != null)
                {
                    popupManager.ShowDisconnectionPopup();
                }

                focusCheckRoutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(1f);
        }

        focusCheckRoutine = null;
    }
    #endregion

    #region Ping/Pong Health Check

    private void StartPingRoutine()
    {
        if (pingCoroutine != null)
            StopCoroutine(pingCoroutine);

        pingCoroutine = StartCoroutine(PingRoutine());
    }

    private void StopPingRoutine()
    {
        if (pingCoroutine != null)
        {
            StopCoroutine(pingCoroutine);
            pingCoroutine = null;
        }
    }

    private IEnumerator PingRoutine()
    {
        while (isConnected)
        {
            yield return new WaitForSeconds(PING_INTERVAL);

            if (waitingForPong && isInitialized)
            {
                missedPongs++;

                if (missedPongs >= MAX_MISSED_PONGS)
                {
                    Debug.LogWarning("[SocketIO] Max pongs missed - disconnecting");
                    OnSocketDisconnected();
                    yield break;
                }

                if (missedPongs >= 2 && popupManager != null)
                {
                    popupManager.ShowReconnectionPopup(missedPongs, MAX_MISSED_PONGS);
                }
            }

            SendPing();
        }
    }

    private void SendPing()
    {
        if (gameSocket != null && isConnected)
        {
            pingSendTime = Time.realtimeSinceStartup;
            waitingForPong = true;
            gameSocket.Emit("ping");

            if (enablePingDebug)
            {
                Debug.Log($"[SocketIO] Ping sent at {pingSendTime:F3}s");
            }
        }
    }

    private void OnPongReceivedNoArgs()
    {
        OnPongReceived(string.Empty);
    }

    private void OnPongReceived(string data)
    {
        waitingForPong = false;
        lastPongTime = Time.time;

        if (pingSendTime > 0f)
        {
            float rtt = Time.realtimeSinceStartup - pingSendTime;
            int pingMs = Mathf.Max(1, Mathf.RoundToInt(rtt * 1000f));
            if (uiManager != null)
            {
                uiManager.UpdatePingDisplay(pingMs);
            }

            if (enablePingDebug)
            {
                Debug.Log($"[SocketIO] Pong received | Latency: {pingMs} ms");
            }
        }

        if (missedPongs > 0)
        {
            missedPongs = 0;

            if (popupManager != null)
            {
                popupManager.CloseReconnectionPopup();
            }
        }
    }

    #endregion

    #region Spin Request

    internal void SendSpinRequest(int betIndex, bool isFreeSpin)
    {
        Debug.Log($"[SocketIO] Spin request: betIndex={betIndex}, isFreeSpin={isFreeSpin}");

        var request = new SpinRequest
        {
            type = "SPIN",
            payload = new SpinPayload
            {
                betIndex = betIndex,
                isFreeSpin = isFreeSpin
            }
        };

        string json = JsonUtility.ToJson(request);
        gameSocket.Emit("request", json);
    }


    #endregion



    #region Cleanup

    internal void CloseSocket()
    {
        if (isDestroyed) return;

        // Mark as intentional exit BEFORE closing so OnSocketDisconnected shows
        // the loading popup (with its animation) instead of the disconnect popup.
        isExiting = true;

        if (RaycastBlocker) RaycastBlocker.SetActive(true);

        StopPingRoutine();

        if (socketManager != null)
        {
            try { socketManager.Close(); } catch { }
            socketManager = null;
        }

        isConnected = false;
        isInitialized = false;
        socketSetupStarted = false;

        // If the socket close does not fire OnSocketDisconnected (e.g. already disconnected),
        // still show the loading popup so the exit transition always looks clean.
        if (popupManager != null && !popupManager.IsLoadingPopupActive())
        {
            popupManager.ShowLoadingPopup(0f);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        if (JSManager != null)
        {
            JSManager.SendCustomMessage("OnExit");
        }
#endif
    }

    private void OnDisable()
    {
        StopPingRoutine();
    }

    private void OnApplicationQuit()
    {
        isDestroyed = true;
    }

    private void OnDestroy()
    {
        isDestroyed = true;
        StopPingRoutine();

        if (socketManager != null)
        {
            try { socketManager.Close(); } catch { }
            socketManager = null;
        }
        isConnected = false;
        isInitialized = false;
        socketSetupStarted = false;
    }

    #endregion
    private List<List<int>> GenerateRandomMatrix(int rowCount)
    {
        var matrix = new List<List<int>>();
        for (int col = 0; col < 5; col++)
        {
            var column = new List<int>();
            for (int row = 0; row < rowCount; row++)
            {
                column.Add(UnityEngine.Random.Range(0, 10));
            }
            matrix.Add(column);
        }
        return matrix;
    }
}


[Serializable]
public class AuthTokenData
{
    public string cookie;
    public string socketURL;
    public string nameSpace;
}

[Serializable]
public class BalanceSyncData
{
    public string userId;
    public string gameId;
    public double balance;
}