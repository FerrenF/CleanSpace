using System;
using System.IO;
using CleanSpaceShared.Config;
using CleanSpaceShared.Logging;
using CleanSpaceShared.Util;
#if TORCH

#else
#endif
namespace CleanSpaceShared.Plugin
{
    public static class Common
    {
        public static ICommonPlugin Plugin { get; private set; }
        public static IPluginLogger Logger { get; private set; }
        public static IPluginConfig Config { get; private set; }

        public static string GameVersion;
        public static string DataDir;
        public static string PluginName;
        public static bool IsServer;
        private static string _instanceSecret;
        public static ulong CleanSpaceGroupID => 103582791475284354;

        public static Type[] CriticalTypes = new Type[]{
                        typeof(CleanSpaceShared.Scanner.AssemblyScanner),
                        typeof(Hasher.HasherRunner)
        };

        public static string InstanceSecret = _instanceSecret != null ? _instanceSecret :  (_instanceSecret = TokenUtility.GenerateToken(DateTime.UtcNow.ToLongTimeString(), DateTime.UtcNow.AddDays(1), "secret"));
        public static void SetPlugin(ICommonPlugin plugin, string gameVersion, string storageDir, string pluginName, bool isServer, IPluginLogger logger, IPluginConfig pluginConfig = null)
        {
            Plugin = plugin;
            Logger = logger;
            if(plugin.Config != null)
                Config = plugin.Config;
            else if(pluginConfig != null)
                Config = pluginConfig;
            PluginName = pluginName;
            IsServer = isServer;
            GameVersion = gameVersion;
            if(DataDir != null)
                DataDir = Path.Combine(storageDir, "CleanSpace");
        }
    }
}