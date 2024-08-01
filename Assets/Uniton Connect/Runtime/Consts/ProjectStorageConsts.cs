using UnityEngine;
using TonSdk.Connect;
using UnitonConnect.Runtime.Data;

namespace UnitonConnect.Editor.Common
{
    public sealed class ProjectStorageConsts
    {
        public const string WALLET_PROVIDER_CONFIG_NAME = "WalletConfig";
        public const string WALLETS_PROVIDERS_STORAGE_NAME = "Wallets Storage";

        public const string CREATE_PATH_WALLET_PROVIDER = "Uniton Connect/Wallet";
        public const string CREATE_PATH_WALLETS_PROVIDERS_STORAGE = "Uniton Connect/Wallets Storage";

        public const string EDITOR_FILE_NAME = "dAppsData.asset";
        public const string EDITOR_STORAGE = "Assets/Uniton Connect/Editor/Internal Resources";

        public const string RUNTIME_FILE_NAME_WITOUT_FORMAT = "dAppsRuntimeData";
        public const string RUNTIME_FILE_NAME = RUNTIME_FILE_NAME_WITOUT_FORMAT + ".asset";

        public const string RUNTIME_STORAGE = "Assets/Resources/Uniton Connect";
        public const string RUNTIME_FOLDER_IN_RESOURCES = "Uniton Connect";

        public const string APP_ICON_FILE_NAME = "https://e7.pngegg.com/pngimages/798/262/png-clipart-flappy-bird-illustration-flappy-bird-app-store-sprite-scratch-game-rectangle-thumbnail.png";
        //public const string APP_ICON_FILE_NAME = "icon.png";
        public const string APP_DATA_FILE_NAME = "dAppData.json";

        public const string TEST_APP_URL = "https://mrveit.github.io/Veittech-UnitonConnect/";
        public const string TEST_APP_NAME = "Uniton Connect";

        public const string TEST_SUPPORTED_WALLETS_LINK = "https://raw.githubusercontent.com/ton-blockchain/wallets-list/main/wallets-v2.json";

        public static string GetTestAppManifest()
        {
            return GetAppManifest(TEST_APP_URL, APP_DATA_FILE_NAME);
        }

        public static string GetAppManifest(string url, string manifestFileName)
        {
            return $"{url}/{manifestFileName}";
        }

        public static DAppConfig GetRuntimeAppStorage()
        {
            return Resources.Load<DAppConfig>($"{RUNTIME_FOLDER_IN_RESOURCES}/{RUNTIME_FILE_NAME_WITOUT_FORMAT}");
        }

        public static void DeleteConnectionKey(string connectionKey)
        {
            PlayerPrefs.DeleteKey(GetSaveConnectionKey(connectionKey));
        }

        private static string GetSaveConnectionKey(string connectionKey)
        {
            return $"{RemoteStorage.STORAGE_PREFIX}{connectionKey}";
        }
    }
}