using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using TonSdk.Core;
using TonSdk.Connect;
using Newtonsoft.Json;
using UnitonConnect.Core.Data;
using UnitonConnect.Core.Data.Common;
using UnitonConnect.Core.Common;
using UnitonConnect.Core.Utils;
using UnitonConnect.Core.Utils.Debugging;
using UnitonConnect.Editor.Common;

namespace UnitonConnect.Core
{
    [SelectionBase]
    [DisallowMultipleComponent]
    [HelpURL("https://github.com/MrVeit/Veittech-UnitonConnect")]
    public sealed class UnitonConnectSDK : MonoBehaviour, IUnitonConnectSDKCallbacks,
        IUnitonConnectWalletCallbacks, IUnitonConnectTransactionCallbacks
    {
        private static readonly object _lock = new();

        private static UnitonConnectSDK _instance;

        public static UnitonConnectSDK Instance
        {
            get
            {
                if (_instance)
                {
                    return _instance;
                }

                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = FindObjectOfType<UnitonConnectSDK>();
                    }
                }

                return _instance;
            }
        }

        [Header("SDK Settings"), Space]
        [Tooltip("Enable if you want to test the SDK without having to upload data about your dApp")]
        [SerializeField, Space] private bool _testMode;
        [Tooltip("Enable if you want to activate SDK logging for detailed analysis before releasing a dApp")]
        [SerializeField] private bool _debugMode;
        [Tooltip("Turn it off if you want to do your own cdk initialization in your scripts")]
        [SerializeField, Space] private bool _initializeOnAwake;
        [Tooltip("Enable if you want to restore a saved connection from storage (recommended)")]
        [SerializeField] private bool _restoreConnectionOnAwake;
        [Header("TonConnect Settings"), Space]
        [SerializeField, Space] private WalletsListData _walletsListConfig;
        [Tooltip("[Temporarily unsupported] Disable if you want to use Injected/Web wallets. This only works in WebGL builds!")]
        [SerializeField, Space] private bool _useWebWallets;
        [Tooltip("Enable if you want to instantly receive wallet icons in your interface without waiting for them to be downloaded from the server")]
        [SerializeField] private bool _useCachedWalletsIcons;
        [Tooltip("Configuration of supported wallets for your dApp. You can change their order and number, and override the way their configurations are loaded by hosting it yourself")]
        [SerializeField, Space] private WalletsProvidersData _supportedWallets;

        private TonConnect _tonConnect;

        private TonConnectOptions _tonConnectOptions;
        private AdditionalConnectOptions _additionalConnectOptions;
        private RemoteStorage _remoteStorage;

        public TonConnect TonConnect => _tonConnect;

        public List<WalletProviderConfig> SupportedWallets => _supportedWallets.Config;

        public bool IsTestMode => _testMode;
        public bool IsDebugMode => _debugMode;
        public bool IsWalletConnected => _tonConnect.IsConnected;

        public bool IsUseWebWallets => _useWebWallets;
        public bool IsUseCachedWalletsIcons => _useCachedWalletsIcons;

        /// <summary>
        /// Callback if sdk initialization is successful
        /// </summary>
        public event IUnitonConnectSDKCallbacks.OnUnitonConnectInitialize OnInitialized;

        /// <summary>
        /// Callback in case of successful initialization of sdk and loading of wallet configurations for further connection
        /// </summary>
        public event IUnitonConnectWalletCallbacks.OnWalletConnectionFinish OnWalletConnectionFinished;

        /// <summary>
        /// Callback for error handling, in case of unsuccessful loading of wallet configurations
        /// </summary>
        public event IUnitonConnectWalletCallbacks.OnWalletConnectionFail OnWalletConnectionFailed;

        /// <summary>
        /// Callback for processing the status of restored connection to the wallet
        /// </summary>
        public event IUnitonConnectWalletCallbacks.OnWalletConnectionRestore OnWalletConnectionRestored;

        /// <summary>
        /// Callback to handle the status of pausing the connection to the wallet
        /// </summary>
        public event IUnitonConnectWalletCallbacks.OnWalletConnectionPause OnWalletConnectionPaused;

        /// <summary>
        /// Callback to handle the shutdown status of a previously activated connection pause
        /// </summary>
        public event IUnitonConnectWalletCallbacks.OnWalletConnectionUnPause OnWalletConnectonUnPaused;

        /// <summary>
        /// Callback to handle wallet connection disconnection status
        /// </summary>
        public event IUnitonConnectWalletCallbacks.OnWalletDisconnect OnWalletDisconnected;

        /// <summary>
        /// Callback to process the status of a recently sent transaction
        /// </summary>
        public event IUnitonConnectTransactionCallbacks.OnTransactionSendingFinish OnTransactionSendingFinished;

        private void Awake()
        {
            CreateInstance();

            if (!_initializeOnAwake)
            {
                return;
            }

            Initialize();
        }

        /// <summary>
        /// Initialization of the Uniton Connect sdk if you want to do it manually.
        /// </summary>
        public void Initialize()
        {
            var dAppManifestLink = string.Empty;
            var dAppConfig = ProjectStorageConsts.GetRuntimeAppStorage();

            dAppManifestLink = WebRequestUtils.GetAppManifestLink(_testMode, dAppConfig);

            if (string.IsNullOrEmpty(dAppManifestLink))
            {
                UnitonConnectLogger.LogError("Failed to initialize Uniton Connect SDK due" +
                    " to missing configuration of your dApp. \r\nIf you want to test the operation of" +
                    " the SDK without integrating your project, activate test mode.");

                return;
            }

            ConfigureWalletsConfig();

            _useWebWallets = false;

            _tonConnectOptions = GetOptions(dAppManifestLink);
            _remoteStorage = GetRemoteStorage();
            _additionalConnectOptions = GetAdditionalConnectOptions();

            _tonConnect = GetTonConnectInstance(_tonConnectOptions,
                _remoteStorage, _additionalConnectOptions);

            _tonConnect.OnStatusChange(OnWalletConnectionFinish, OnWalletConnectionFail);

            RestoreConnectionAsync(_remoteStorage);

            OnInitialize();

            UnitonConnectLogger.Log("SDK successfully initialized");
        }

        /// <summary>
        /// Set the connection event listener to pause
        /// </summary>
        public void PauseConnection()
        {
            _tonConnect.PauseConnection();

            OnWalletConnectionPause();
        }

        /// <summary>
        /// Switching off the activated pause for the connection event listener
        /// </summary>
        public void UnPauseConnection()
        {
            _tonConnect.UnPauseConnection();

            OnWalletConnectionUnPause();
        }

        /// <summary>
        /// Start downloading wallet configurations by the specified link to the json file
        /// </summary>
        /// <param name="supportedWalletsUrl">Link to a list of all supported wallets to get their configurations. Use ProjectStorageConsts.TEST_SUPPORTED_WALLETS_LINK to get the whole list of available wallets, or bind your manifest</param>
        /// <param name="walletsClaimed">Callback to retrieve successfully downloaded wallet configurations</param>
        public void LoadWalletsConfigs(string supportedWalletsUrl,
            Action<List<WalletConfig>> walletsClaimed)
        {
            StartCoroutine(LoadWallets(supportedWalletsUrl, walletsClaimed));
        }

        /// <summary>
        /// Connecting to an HTTP bridged wallet via deep links
        /// </summary>
        /// <param name="wallet">Wallet configuration for connection</param>
        /// <param name="connectUrl">Link to connect to the wallet</param>
        public void ConnectHttpBridgeWalletViaDeepLink(
            WalletConfig wallet, string connectUrl)
        {
            if (!WalletConnectUtils.HasHttpBridge(wallet))
            {
                UnitonConnectLogger.LogWarning("The specified wallet configuration has no HTTP bridge detected," +
                    " the deeplink connection was terminated.");

                return;
            }

            OpenWalletViaDeepLink(Uri.EscapeUriString(connectUrl));
        }

        /// <summary>
        /// Connection to a JavaScript bridged wallet via deep links
        /// </summary>
        /// <param name="wallet">Wallet configuration for connection</param>
        public async void ConnectJavaScriptBridgeWalletViaDeeplink(WalletConfig wallet)
        {
            if (!WalletConnectUtils.HasJSBridge(wallet))
            {
                UnitonConnectLogger.LogWarning("The specified wallet configuration has no JavaScript bridge detected," +
                    " the deeplink connection was terminated.");

                return;
            }

            await GenerateConnectURL(wallet);
        }

        /// <summary>
        /// Send TonCoin to the specified recipient address
        /// </summary>
        /// <param name="currentWallet">Current authorized wallet for the transaction</param>
        /// <param name="recipientAddress">Token recipient address</param>
        /// <param name="amount">Number of tokens to send</param>
        public async Task SendTransaction(WalletConfig currentWallet,
            string recipientAddress, double amount)
        {
            var transactionMessages = GetTransactionMessages(recipientAddress, amount);
            var transactionRequest = GetTransactionRequest(transactionMessages);

            SendTransactionResult? transactionResult = null;

            try
            {
                UnitonConnectLogger.Log($"Created a request to send a TON" +
                    $" to the recipient: {recipientAddress} in amount {amount}");

                if (WalletConnectUtils.IsAddressesMatch(recipientAddress))
                {
                    UnitonConnectLogger.LogWarning("Transaction canceled because the recipient and sender addresses match");

                    return;
                }

                OpenWalletViaDeepLink(currentWallet.UniversalUrl);

                transactionResult = await _tonConnect.SendTransaction(transactionRequest);

                UnitonConnectLogger.Log($"Transaction successfully completed, Boc: {transactionResult.Value.Boc}");

                OnTransactionSendingFinish(transactionResult, true);
            }
            catch (WalletNotConnectedError connectionError)
            {
                UnitonConnectLogger.LogError($"{connectionError.Message}");

                OnTransactionSendingFinish(transactionResult, false);
            }
            catch (Exception exception)
            {
                UnitonConnectLogger.LogError($"Failed to send tokens due" +
                    $" to the following reason: {exception.Message}");

                OnTransactionSendingFinish(transactionResult, false);
            }
        }

        /// <summary>
        /// Disconnect of previously connected wallet
        /// </summary>
        public async Task DisconnectWallet()
        {
            try
            {
                if (!IsWalletConnected)
                {
                    UnitonConnectLogger.LogError("No connected wallets are detected for disconnection");

                    return;
                }

                await _tonConnect.Disconnect();
            }
            catch (TonConnectError error)
            {
                UnitonConnectLogger.LogError($"Error: {error.Message}");
            }
            catch (Exception exception)
            {
                UnitonConnectLogger.LogError($"The previously connected wallet could not be " +
                    $"disconnected due to the following reason: {exception.Message}");
            }
        }

        /// <summary>
        /// Get a link to connect to the wallet via the specified config
        /// </summary>
        /// <param name="wallet">Wallet configuration to connect</param>.
        public async Task<string> GenerateConnectURL(WalletConfig wallet)
        {
            var connectUrl = string.Empty;

            try
            {
                connectUrl = await _tonConnect.Connect(wallet);
            }
            catch (WalletAlreadyConnectedError error)
            {
                UnitonConnectLogger.LogError($"Error: {error.Message}");
            }
            catch (Exception exceoption)
            {
                UnitonConnectLogger.LogError($"Failed to connect to the wallet due to " +
                    $"the following reason: {exceoption.Message}");
            }

            return connectUrl;
        }

        private void CreateInstance()
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = this;

                    DontDestroyOnLoad(gameObject);

                    return;
                }

                UnitonConnectLogger.LogError($"Another instance is detected on the scene, running delete...");

                Destroy(gameObject);
            }
        }

        private async void RestoreConnectionAsync(RemoteStorage storage)
        {
            if (!_restoreConnectionOnAwake)
            {
                ResetConnectionStorage(storage);

                return;
            }

            bool isSuccess = await _tonConnect.RestoreConnection();

            OnWalletConnectionRestore(isSuccess);

            UnitonConnectLogger.Log($"Connection restored with status: {isSuccess}");
        }

        private IEnumerator LoadWallets(string supportedWalletsUrl, 
            Action<List<WalletConfig>> walletsClaimed)
        {
            UnityWebRequest request = UnityWebRequest.Get(supportedWalletsUrl);

            yield return request.SendWebRequest();

            if (request.result != WebRequestUtils.SUCCESS)
            {
                UnitonConnectLogger.LogError($"HTTP Error with message: {request.error}");

                yield break;
            }
            else
            {
                var responseResult = request.downloadHandler.text;
                var walletsList = JsonConvert.DeserializeObject<List<WalletProviderData>>(responseResult);

                UnitonConnectLogger.Log($"Wallet list config after load: {JsonConvert.SerializeObject(walletsList)}");

                ParseWalletsConfigs(ref walletsList, walletsClaimed);
            }

            request.Dispose();
        }

        private IEnumerator ActivateInitializationSDKListenerRoutine(
            EventListenerArgumentsData listenerData)
        {
            var url = listenerData.Url;

            var acceptHeader = WebRequestUtils.HEADER_ACCEPT;
            var textEventStreamValue = WebRequestUtils.HEADER_VALUE_TEXT_EVENT_STREAM;

            UnityWebRequest request = new(listenerData.Url, UnityWebRequest.kHttpVerbGET) { };

            WebRequestUtils.SetRequestHeader(request, acceptHeader, textEventStreamValue);

            DownloadHandlerBuffer handlerBuff = new();
            request.downloadHandler = handlerBuff;

            AsyncOperation operation = request.SendWebRequest();

            int currentPosition = 0;

            while (!listenerData.Token.IsCancellationRequested && !operation.isDone)
            {
                if (request.result == WebRequestUtils.CONNECTION_ERROR ||
                        request.result == WebRequestUtils.PROTOCOL_ERROR)
                {
                    listenerData.ErrorProvider(
                        new Exception($"SSE request error: {request.error}"));

                    UnitonConnectLogger.LogError($"Failed to activate event listener with error: {request.error}");

                    break;
                }

                string result = request.downloadHandler.text.Substring(currentPosition);
                string[] lines = result.Split('\n');

                foreach (string line in lines)
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        UnitonConnectLogger.Log(line);

                        listenerData.Provider(line);
                    }
                }

                currentPosition += result.Length;

                yield return null;
            }

            request.Dispose();
        }

        private IEnumerator ActivateGatewaySenderRoutine(GatewayMessageData gatewayMessage)
        {
            var url = WebRequestUtils.GetGatewaySenderLink(gatewayMessage);

            var contentTypeHeader = WebRequestUtils.HEADER_CONTENT_TYPE;
            var textPlainValue = WebRequestUtils.HEADER_VALUE_TEXT_PLAIN;

            UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(gatewayMessage.Message)
            };

            WebRequestUtils.SetRequestHeader(request, contentTypeHeader, textPlainValue);

            yield return request.SendWebRequest();

            if (request.result != WebRequestUtils.SUCCESS)
            {
                UnitonConnectLogger.LogError($"Failed to send Gateway message with error:" +
                    $" {request.error}, response code: {request.responseCode}");
            }
            else
            {
                UnitonConnectLogger.Log("Gateway message successfully sended");
            }

            yield return null;

            request.Dispose();
        }

        private void ActivateInitializationSDKListener(CancellationToken token,
            string url, ProviderMessageHandler initializationSuccess, ProviderErrorHandler initializationFailed)
        {
            var listenerData = new EventListenerArgumentsData()
            {
                Token = token,
                Url = url,
                Provider = initializationSuccess,
                ErrorProvider = initializationFailed
            };

            StartCoroutine(ActivateInitializationSDKListenerRoutine(listenerData));
        }

        private void ActivateGatewaySender(string bridgeUrl, string postPath,
            string sessionId, string receiver, int timeToLive, string topic, byte[] message)
        {
            var gatewayMessage = new GatewayMessageData()
            {
                BridgeUrl = bridgeUrl,
                PostPath = postPath,
                SessionId = sessionId,
                Receiver = receiver,
                TimeToLive = timeToLive,
                Topic = topic,
                Message = message
            };

            StartCoroutine(ActivateGatewaySenderRoutine(gatewayMessage));
        }

        private void ConfigureWalletsConfig()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (!_useWebWallets)
            {
                return;
            }

            _useWebWallets = false;

            UnitonConnectLogger.LogWarning("The 'Use Web Wallets' property was automatically disabled" +
                " due to platform incompatibility. It should only be used in WebGL builds.");
#endif
        }

        private void ParseWalletsConfigs(ref List<WalletProviderData> walletsList, 
            Action<List<WalletConfig>> walletsClaimed)
        {
            var walletNameWithBugBridgeURL = "MyTonWallet";

            var loadedWallets = new List<WalletConfig>();

            foreach (var wallet in walletsList)
            {
                WalletConfig walletConfig = new()
                {
                    Name = wallet.Name,
                    Image = wallet.Image,
                    AboutUrl = wallet.AboutUrl,
                    AppName = wallet.AppName
                };

                foreach (var bridge in wallet.Bridge)
                {
                    if (bridge.Type == WalletConfigComponents.SSE)
                    {
                        walletConfig.BridgeUrl = bridge.Url;
                        walletConfig.UniversalUrl = wallet.UniversalUrl;
                        walletConfig.JsBridgeKey = null;

                        if (walletConfig.Name == walletNameWithBugBridgeURL)
                        {
                            walletConfig.BridgeUrl = bridge.Url.TrimEnd('/');
                        }

                        loadedWallets.Add(walletConfig);
                    }
                    else if (bridge.Type == WalletConfigComponents.JAVA_SCRIPT)
                    {
                        walletConfig.JsBridgeKey = bridge.Key;
                        walletConfig.BridgeUrl = null;

                        loadedWallets.Add(walletConfig);
                    }
                }
            }

            walletsClaimed(loadedWallets);
        }

        private void ResetConnectionStorage(RemoteStorage storage)
        {
            var keyConnection = RemoteStorage.KEY_CONNECTION;
            var lastEventId = RemoteStorage.KEY_LAST_EVENT_ID;

            storage.RemoveItem(keyConnection);
            storage.RemoveItem(lastEventId);

            ProjectStorageConsts.DeleteConnectionKey(keyConnection);
            ProjectStorageConsts.DeleteConnectionKey(lastEventId);

            OnWalletConnectionRestore(false);
        }

        private void OpenWalletViaDeepLink(string deepLinkURL)
        {
            Application.OpenURL(deepLinkURL);
        }

        private TonConnect GetTonConnectInstance(TonConnectOptions options,
            RemoteStorage storage, AdditionalConnectOptions connectOptions)
        {
            return new TonConnect(options, storage, connectOptions);
        }

        private TonConnectOptions GetOptions(string manifestLink)
        {
            TonConnectOptions options = new()
            {
                ManifestUrl = manifestLink,
                WalletsListSource = _walletsListConfig.SourceLink,
                WalletsListCacheTTLMs = _walletsListConfig.CachedTimeToLive
            };

            return options;
        }

        private RemoteStorage GetRemoteStorage()
        {
            return new RemoteStorage(new(PlayerPrefs.GetString), new(PlayerPrefs.SetString),
                new(PlayerPrefs.DeleteKey), new(PlayerPrefs.HasKey));
        }

        private AdditionalConnectOptions GetAdditionalConnectOptions()
        {
            AdditionalConnectOptions connectOptions = new()
            {
                listenEventsFunction = new ListenEventsFunction(ActivateInitializationSDKListener),
                sendGatewayMessage = new SendGatewayMessage(ActivateGatewaySender)
            };

            return connectOptions;
        }

        private SendTransactionRequest GetTransactionRequest(Message[] messages)
        {
            long valudUntil = DateTimeOffset.Now.ToUnixTimeSeconds() + 600;

            return new SendTransactionRequest(messages, valudUntil);
        }

        private Message[] GetTransactionMessages(string receiverAddress,
            double amount)
        {
            Address receiver = new(receiverAddress);
            Coins tokensAmount = new(amount);

            Message[] messages =
            {
                new(receiver, tokensAmount),
                //new(receiver, tokensAmount),
                //new(receiver, tokensAmount),
                //new(receiver, tokensAmount)
            };

            return messages;
        }

        private void OnInitialize() => OnInitialized?.Invoke();

        private void OnWalletConnectionFinish(Wallet wallet)
        {
            if (!IsWalletConnected)
            {
                ResetConnectionStorage(_remoteStorage);

                OnWalletDisconnect();

                UnitonConnectLogger.Log("Connection to the wallet has been successfully disconnected," +
                    " the storage of the previous session has been cleaned up");
            }

            OnWalletConnectionFinished?.Invoke(wallet);
        }

        private void OnInjectedWalletMessageReceive(string message) => _tonConnect.ParseInjectedProviderMessage(message);

        private void OnWalletConnectionFail(string errorMessage) => OnWalletConnectionFailed?.Invoke(errorMessage);

        private void OnWalletConnectionRestore(bool isRestored) => OnWalletConnectionRestored?.Invoke(isRestored);

        private void OnWalletConnectionPause() => OnWalletConnectionPaused?.Invoke();

        private void OnWalletConnectionUnPause() => OnWalletConnectonUnPaused?.Invoke();

        private void OnWalletDisconnect() => OnWalletDisconnected?.Invoke();

        private void OnTransactionSendingFinish(SendTransactionResult? transactionResult,
            bool isSuccess) => OnTransactionSendingFinished?.Invoke(transactionResult, isSuccess);
    }
}