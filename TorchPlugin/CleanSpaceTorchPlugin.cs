#define USE_HARMONY

using CleanSpaceShared.Networking;
using HarmonyLib;
using Sandbox.Game;
using CleanSpaceShared.Config;
using CleanSpaceShared.Logging;
using CleanSpaceTorch.Patch;
using CleanSpaceShared.Plugin;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.Session;
using CleanSpaceTorch.Tracker;
using CleanSpaceTorch.Util;
using VRage.Utils;

namespace CleanSpaceTorch
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class CleanSpaceTorchPlugin : TorchPluginBase, IWpfPlugin, ICommonPlugin
    {

        public const string PluginName = "Clean Space";
        public static CleanSpaceTorchPlugin Instance { get; private set; }

        public long Tick { get; private set; }

        public IPluginLogger Log => Logger;
        private static readonly IPluginLogger Logger = new PluginLogger(PluginName);
        
        public IPluginConfig Config => config?.Data;
        private PersistentConfig<ViewModelConfig> config;
        private static readonly string ConfigFileName = $"{PluginName}.cfg";

        // ReSharper disable once UnusedMember.Global
        public UserControl GetControl() => control ?? (control = new ConfigView());
        private ConfigView control;

        private TorchSessionManager sessionManager;
        private CleanSpaceAssemblyManager assemblyManager;
        private ClientSessionManager cleanSpaceClientManager;
        private bool initialized;
        private bool failed;

        // ReSharper disable once UnusedMember.Local
        private readonly Commands commands = new Commands();

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            if (initialized) return;
            sessionManager = torch.Managers.GetManager<TorchSessionManager>();
            ConfigView.Log = Log;
#if DEBUG
            // Allow the debugger some time to connect once the plugin assembly is loaded
            Thread.Sleep(100);
#endif
           
            Instance = this;
            Log.Info("Init"); 

            var configPath = Path.Combine(StoragePath, ConfigFileName);
            config = PersistentConfig<ViewModelConfig>.Load(Log, configPath);
            
            var gameVersionNumber = MyPerGameSettings.BasicGameInfo.GameVersion ?? 0;
            var gameVersion = new StringBuilder(MyBuildNumbers.ConvertBuildNumberFromIntToString(gameVersionNumber)).ToString();
            Common.SetPlugin(this, gameVersion, StoragePath, "Clean Space", true, Logger);

            PatchHelpers.Configure();
            var harm = new Harmony(Name);
            if (!PatchHelpers.HarmonyPatchAll(Log, harm))
            {
                failed = true;
                return;
            }
            else
            {
                Log.Info("Patches applied.");
            }            
         
            cleanSpaceClientManager = new ClientSessionManager();            
            Common.Logger.Info($"Torch directory is {AppDomain.CurrentDomain.BaseDirectory}");           

            assemblyManager = new CleanSpaceAssemblyManager(AppDomain.CurrentDomain.BaseDirectory);
            sessionManager.SessionStateChanged += SessionStateChanged;

            ServerSessionParameterProviders.RegisterProviders();
            assemblyManager.Init_Events();
            Init_Critical_Types();
            initialized = true; 
        }

        private void Init_Critical_Types()
        {
            


        }

        private bool events_initialized = false;
        private void Init_Events()
        {
            if(events_initialized) return;
            events_initialized = true;
            cleanSpaceClientManager.Init_Events();
           

        }      

        private void SessionStateChanged(ITorchSession session, TorchSessionState newstate)
        {
            switch (newstate)
            {
                case TorchSessionState.Loading:
                    
                    PacketRegistry.InstallHandler();
                    RegisterPackets();
                    Init_Events();
                    break;

                case TorchSessionState.Loaded:
                 
                    break;
             
                case TorchSessionState.Unloading:

                    break;


                case TorchSessionState.Unloaded:
                    break;
            }
        }


        private void RegisterPackets()
        {
            PacketRegistry.Register<CleanSpaceHelloPacket>(
              107, () => new ProtoPacketData<CleanSpaceHelloPacket>(), SecretPacketFactory<CleanSpaceHelloPacket>.handler < ProtoPacketData<CleanSpaceHelloPacket>>
            );

            PacketRegistry.Register < CleanSpaceChatterPacket>(
              108, () => new ProtoPacketData<CleanSpaceChatterPacket>(), SecretPacketFactory<CleanSpaceChatterPacket>.handler<ProtoPacketData<CleanSpaceChatterPacket>>
            );

            PacketRegistry.Register<PluginValidationRequest>(
              110, () => new ProtoPacketData<PluginValidationRequest>(), SecretPacketFactory<PluginValidationRequest>.handler<ProtoPacketData<PluginValidationRequest>>
            );

            PacketRegistry.Register<PluginValidationResponse>(
               111, () => new ProtoPacketData<PluginValidationResponse>(),  SecretPacketFactory<PluginValidationResponse>.handler<ProtoPacketData<PluginValidationResponse>>
            );
          
            PacketRegistry.Register<PluginValidationResult>(
              112,  () => new ProtoPacketData<PluginValidationResult>(), SecretPacketFactory<PluginValidationResult>.handler<ProtoPacketData<PluginValidationResult>>
            );

        }

        public override void Dispose()
        {
            if (initialized)
            {
                Log.Debug("Disposing");

                sessionManager.SessionStateChanged -= SessionStateChanged;
                sessionManager = null;
              

                Log.Debug("Disposed");
            }

            Instance = null;

            base.Dispose();
        }

        public override void Update()
        {
            if (failed)
                return;

            try
            {
                CustomUpdate();
                Tick++;
            }
            catch (Exception e)
            {
                Log.Critical(e, "Update failed");
                failed = true;
            }
        }

        private void CustomUpdate()
        {
            // TODO: Put your update processing here. It is called on every simulation frame!            no -F
            PatchHelpers.PatchUpdates();
        }
    }
}