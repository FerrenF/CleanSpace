using CleanSpaceTorch.Patch;
using CleanSpaceShared.Networking;
using CleanSpaceShared.Scanner;
using Sandbox.Engine.Multiplayer;
using Sandbox.Engine.Networking;
using Sandbox.Game.Multiplayer;
using CleanSpaceShared.Config;
using CleanSpaceShared.Events;
using CleanSpaceShared.Logging;
using CleanSpaceShared.Plugin;
using CleanSpaceShared.Struct;
using CleanSpaceShared.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CleanSpaceTorch.Hasher;
using CleanSpaceTorch.Util;
using VRage;
using VRage.GameServices;
using VRage.Network;

namespace CleanSpaceTorch.Tracker
{
    public enum CS_CLIENT_STATE
    {
        PENDING,
        CHATTING,
        VALIDATION_REQUESTED,
        AWAITING_VALIDATION,
        VALIDATION_RESPONDED,
        VALIDATION_FINALIZED,
        CONNECTED,
        DISCONNECTED
    }

    public class ClientSession
    {
        ulong steamId;
        uint origin;
        DateTime sessionStartTime;
        DateTime sessionEndTime;
        DateTime lastInteractionTime;
        DateTime lastValidationTime;
        DateTime nextValidationTime;
        ValidationResultData? validationResultData;
        
        private byte[] clientHasherBytes;
        private byte[] hasherSignature;
        int[] clientHasherSequence;

        private CS_CLIENT_STATE connectionState;
        CS_CLIENT_STATE ConnectionState
        {
            get { return connectionState; }
            set
            {
                EventHub.OnConnectionStateChanged(this, steamId, (int)value);
                connectionState = value;
            }
        }

        ChatterChallenge chatterParameters;
        ChatterChallenge sessionParameters;
        private byte[] clientSalt;
        string currentClientNonce;
        string currentServerNonce;
        string lastServerNonce;
        string lastClientNonce;
        List<string> lastHashList;
        private ConnectedClientDataMsg initialMsg;
     
        bool events_initialized = false;
        private ClientSessionManager Manager => ClientSessionManager.Instance;

        public DateTime SessionStartTime { get => sessionStartTime; set => sessionStartTime = value; }
        public DateTime SessionEndTime { get => sessionEndTime; set => sessionEndTime = value; }

        public ClientSession(ulong steamId, ConnectedClientDataMsg initialMsg)
        {
          
            this.steamId = steamId;
            this.initialMsg = new ConnectedClientDataMsg
            {
                ClientId = initialMsg.ClientId,
                ExperimentalMode = initialMsg.ExperimentalMode,
                IsAdmin = initialMsg.IsAdmin,
                IsProfiling = initialMsg.IsProfiling,
                Join = initialMsg.Join,
                Name = (string)initialMsg.Name.Clone(),
                ServiceName = (string)initialMsg.ServiceName.Clone(),
                Token = (byte[])initialMsg.Token.Clone(),
            };

            this.ConnectionState = CS_CLIENT_STATE.PENDING;
            this.SessionStartTime = DateTime.Now;
            CycleSalt();


            EventHub.ClientCleanSpaceResponded += EventHub_ClientCleanSpaceResponded;
            EventHub.CleanSpaceHelloReceived += EventHub_CleanSpaceHelloReceived;
            EventHub.CleanSpaceChatterReceived += EventHub_CleanSpaceChatterReceived;
            

            MyP2PSessionState state = new MyP2PSessionState();
            if (MyGameService.Peer2Peer.GetSessionState(steamId, ref state))
            {
                this.origin = state.RemoteIP;
            }
            else
            {
                Common.Logger.Debug($"Failed to get a valid remote address for client {steamId}. Disconnecting.");
                Task.Run(()=>DelayedDisconnect());
                return;
            }

            Common.Logger.Debug($"Hasher generated for client with id {steamId}. Doing a quick test. ");

            try
            {
                CleanSpaceShared.Hasher.HasherRunner.ValidateHasherRunnerBytes(clientHasherBytes);
            }
            catch (Exception ex)
            {
                Common.Logger.Error($"Failed to generate hasher for client with id {steamId}. " + ex.Message);
                throw new Exception("Security error: " + ex.Message);
            }

            Common.Logger.Debug($"Hasher validated for client with id {steamId}. Signing.");
            this.hasherSignature = TokenUtility.SignPayloadBytes(clientHasherBytes, clientSalt);
        }

        private void RebuildHasher()
        {
            (this.clientHasherBytes, this.clientHasherSequence) = HasherFactory.GetNewHasherWithSecret(Common.InstanceSecret);
        }

        private void CycleSalt()
        {
            var secretBytes = Encoding.UTF8.GetBytes(Common.InstanceSecret);
            int startIndex;
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] buf = new byte[4];
                rng.GetBytes(buf);
                startIndex = BitConverter.ToInt32(buf, 0) & int.MaxValue;
                startIndex %= (secretBytes.Length - 16);
            }
            clientSalt = new byte[16];
            Array.Copy(secretBytes, startIndex, clientSalt, 0, 16);
            RebuildHasher();
        }



        #region EventHubEvents
        private void EventHub_CleanSpaceChatterReceived(object sender, CleanSpaceTargetedEventArgs e)
        {
            ulong senderId = e.Source;
            if (senderId != this.steamId)
                return;

            object[] args = e.Args;
            try { MiscUtil.ArgsChecks<CleanSpaceChatterPacket>(e, 1); }
            catch (Exception ex)
            {
                Common.Logger.Error($"{Common.PluginName} ArgChecks ran into a problem handling client chatter." + ex.Message);
                RejectConnection();
                return;
            }

            CleanSpaceChatterPacket r = (CleanSpaceChatterPacket)args[0];
            ReceiveChatter(r);
        }

        private void EventHub_CleanSpaceHelloReceived(object sender, CleanSpaceTargetedEventArgs e)
        {
            ulong senderId = e.Source;
            if (senderId != this.steamId)
                return;

            object[] args = e.Args;
            try { MiscUtil.ArgsChecks<CleanSpaceHelloPacket>(e, 1, srcVerify: this.steamId); }
            catch (Exception ex)
            {
                Common.Logger.Error($"{Common.PluginName} ArgChecks ran into a problem handling client chatter." + ex.Message);
                RejectConnection();
                return;
            }

            CleanSpaceHelloPacket r = (CleanSpaceHelloPacket)args[0];
            ReceiveHello(r);
        }

        private void EventHub_ClientCleanSpaceResponded(object sender, CleanSpaceTargetedEventArgs e)
        {
            ulong senderId = e.Target;
            if (senderId != this.steamId)
                return;

            object[] args = e.Args;
            try { MiscUtil.ArgsChecks<PluginValidationResponse>(e, 1, destVerify: this.steamId); }
            catch (Exception ex)
            {
                Common.Logger.Error($"{Common.PluginName} ArgChecks ran into a problem handling validation response." + ex.Message);
                RejectConnection();
                return;
            }
            if(this.ConnectionState != CS_CLIENT_STATE.VALIDATION_REQUESTED)
            {
                Common.Logger.Error($"{Common.PluginName} Received a validation response but state wasn't requested.");
                RejectConnection();
                return;
            }
            PluginValidationResponse r = (PluginValidationResponse)e.Args[0];

            this.ConnectionState = CS_CLIENT_STATE.VALIDATION_RESPONDED;

            // TODO:
            // Add like, an attestation signature to the response and verify that too. Layer that shit on like a cheap frosted cookie from walmart.

            List<PluginListEntry> testAgainst = ValidationManager.GetCleanSpaceAssemblyList();

            string securedCleanSpaceSignature = r.attestationResponse;

            (var hasher, int[] indexes) = HasherFactory.MakeHasher(Common.InstanceSecret, this.clientHasherSequence);

            var signatureTransformer = HasherFactory.CompileHasherAssembly(hasher);
          
            
            List<string> pluginList = r.PluginHashes;
            List<string> analysis = pluginList.Select((element) => AssemblyScanner.UnscrambleSecureFingerprint(element, Encoding.UTF8.GetBytes(r.NonceS))).ToList();

            this.validationResultData = ValidationManager.Validate(steamId, r.NonceS, securedCleanSpaceSignature, signatureTransformer, analysis);
            this.ConnectionState = CS_CLIENT_STATE.VALIDATION_FINALIZED;
            SessionEndTime = DateTime.Now;
            EventHub.OnServerCleanSpaceFinalized(this, steamId, this, validationResultData);

            if (!r.newSteamToken.IsNullOrEmpty())
            {
                this.initialMsg.Token = r.newSteamToken;
            }

            if (this.validationResultData?.Code == ValidationResultCode.ALLOWED)
            {
                this.AcceptConnection();               
            }
            else
            {
                this.RejectConnection();
            }

        }


        #endregion
        private void AcceptConnection(bool force = false)
        {
            if (connectionState != CS_CLIENT_STATE.CONNECTED)
            {
                connectionState = CS_CLIENT_STATE.CONNECTED; 
                EventHub.OnServerConnectionAccepted(this, steamId, validationResultData);
                Common.Logger.Info($"Sending join message to ID {steamId}");
                ConnectedClientPatch.CallPrivateMethod((MyDedicatedServerBase)MyDedicatedServerBase.Instance, ref initialMsg, new EndpointId(steamId));
            }
        }


        // We have room to try to say hi three times before the disconnection task kicked off in ReceiveHello fires and sees that the client state hasn't changed.

        private int _helloTriesMax = Constants.CFG_HELLO_TASK_RETRIES_MAX;
        private int _hasSentHello = 0;
        private bool _hasReceivedHello = false;
        public void SendHello()
        {
            StringBuilder mmb = new StringBuilder();

            if (_hasSentHello < _helloTriesMax && !_hasReceivedHello) {
                var newNonce = GenerateNewNonce();
                var sayHello = new CleanSpaceHelloPacket
                {
                    SenderId = MyGameService.OnlineUserId,
                    TargetType = MessageTarget.Client,
                    Target = steamId,
                    UnixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    NonceS = newNonce,
                    NonceC = String.Empty,
                    sessionParameters = ProtoUtil.Serialize(this.sessionParameters = ChatterChallengeFactory.CreateSessionParameters(this.clientSalt)),
                    client_ip_echo =  this.origin
                }; 

                _hasSentHello += 1;
                mmb.Append($"Saying hello to ID {steamId}... ");
                if (_hasSentHello > 0)
                    mmb.Append($"Try {_hasSentHello} of {_helloTriesMax}");              
                PacketRegistry.Send(sayHello, new EndpointId(steamId), newNonce);
                Task.Run(() => TryHelloTask());
            }
            else
            {
                mmb.Append($"Exhausted attempts to say hello to {steamId}.");
            }              

            Common.Logger.Info(mmb.ToString());
        }
        
        // When the client gets here, it will not have a Session. The default behaviour always will be to discard the session at the manager level if one already exists. 
        private void ReceiveHello(CleanSpaceHelloPacket r)
        {
            if ( !Common.Config.Enabled)
            {
                // Disabled; Bypass the entire process and just accept the connection.
                AcceptConnection();
                return;
            }      

            if (this.sessionParameters.sessionSalt == null)
            {
                Common.Logger.Error($"{Common.PluginName}: Client ID {steamId} sent us a hello packet but we had no recorded session parameters. Discarding the whole thing.");
                // TODO: Robust ErrorD
                RejectConnection();
                return;
            }

            if (this.ConnectionState != CS_CLIENT_STATE.PENDING)
            {
                Common.Logger.Error($"{Common.PluginName} Client ID {steamId} sent us a hello, but their state wasn't pending.");
                RejectConnection();
                return;
            }

            _hasReceivedHello = true;     

            var challengeResponses = ChatterChallengeValidater.UnpackChallengeResponse(r.sessionParameters);            

            if (ChatterChallengeValidater.ValidateResponse(this.sessionParameters, r.sessionParameters, currentServerNonce, clientSalt) <= 0)
            {
                Common.Logger.Error($"{Common.PluginName}: Client ID {steamId} sent us a hello packet but failed validation.");       
                RejectConnection();
                return;
            }

            this.lastClientNonce = String.Empty;
            this.currentClientNonce = r.NonceC;
            Common.Logger.Info($"{Common.PluginName}: Client ID {steamId} said hello and passed initial validation checks.");
            this.BeginChatter();
        }

        private void BeginChatter()
        {
            this.ConnectionState = CS_CLIENT_STATE.CHATTING;
            chatterRemaining = this.sessionParameters.chatterLength;
            Common.Logger.Debug($"{Common.PluginName}: Set chatter length to {chatterRemaining}. Initiating chatter.");
            SendChatter();
        }

        private Random _rng =  new Random();
        private int chatterRemaining = 0;
        private bool accepting_chatter = true;
        private bool payload_dispatched = false;
        private void SendChatter()
        {

            if (chatterRemaining <= 1 && !payload_dispatched)
            {                
                chatterRemaining = 0;                
                byte[] messageIV = new byte[16];
                _rng.NextBytes(messageIV);

                // The payload.
                string newNonce = GenerateNewNonce();
                PluginValidationRequest rs = new PluginValidationRequest()
                {                 
                    SenderId = Sync.MyId,
                    attestationChallenge = EncryptionUtil.EncryptBytesWithIV(this.clientHasherBytes, newNonce, this.clientSalt, messageIV).Concat(messageIV).ToArray(),
                    attestationSignature = this.hasherSignature,
                    NonceS = newNonce,
                    NonceC = currentClientNonce,
                    Target = steamId,
                    TargetType = MessageTarget.Client,
                    UnixTimestamp = DateTime.Now.ToUnixTimestamp()
                };

                byte[] chatterPayload = ProtoUtil.Serialize(rs);
                this.ConnectionState = CS_CLIENT_STATE.VALIDATION_REQUESTED;
                // This time, the nonce on the chatter packet is insignificant, instead of the nonce on the dummy payload.
                string insignificantNonce = TokenUtility.GenerateToken(Common.InstanceSecret, DateTime.UtcNow.AddSeconds(10)); ;
                CleanSpaceChatterPacket cleanSpaceChatterPacket = new CleanSpaceChatterPacket()
                {
                    chatterPayload = chatterPayload,
                    chatterParameters = this.chatterParameters,
                    NonceS = insignificantNonce,
                    NonceC = currentClientNonce,               
                    Target = steamId,
                    TargetType = MessageTarget.Client,
                    SenderId = Sync.MyId,
                    UnixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                Common.Logger.Info($"Chatter with real payload sent to client ID {steamId}. Scheduling echoes...");
                payload_dispatched = true;
                PacketRegistry.Send(cleanSpaceChatterPacket, new EndpointId(steamId), newNonce, currentClientNonce);               
                EventHub.OnServerCleanSpaceRequested(this, steamId, initialMsg);
                noiseRemaining = 3;                
                Task.Run(() => SendNoise());
            }
            else
            {
                // The standard dummy package.             
                chatterRemaining -= 1;
                SendDummyChatterPayload();              
            }
        }

        private void SendDummyChatterPayload(bool meaningfulNonce = true)
        {
            byte[] dummySignature;
            byte[] dummyAttestation = new byte[this.clientHasherBytes.Length];
            _rng.NextBytes(dummyAttestation);

            string dummyNonce = TokenUtility.GenerateToken(Common.InstanceSecret, DateTime.UtcNow.AddSeconds(10));
            using (var sig = new SHA256Managed())
                dummySignature = sig.ComputeHash(dummyAttestation);

            PluginValidationRequest rs = new PluginValidationRequest()
            {
                attestationChallenge = dummyAttestation,
                attestationSignature = dummySignature,
                NonceS = dummyNonce,
                Target = steamId,
                TargetType = MessageTarget.Client,
                UnixTimestamp = DateTime.Now.ToUnixTimestamp()
            };

            byte[] chatterPayload = ProtoUtil.Serialize(rs);

            this.chatterParameters = ChatterChallengeFactory.CreateSessionParameters(this.clientSalt);
            string newServerNonce = meaningfulNonce ? GenerateNewNonce() : TokenUtility.GenerateToken(Common.InstanceSecret, DateTime.UtcNow.AddSeconds(10));

            CleanSpaceChatterPacket cleanSpaceChatterPacket = new CleanSpaceChatterPacket()
            {
                chatterPayload = chatterPayload,
                chatterParameters = this.chatterParameters,
                NonceS = newServerNonce,
                NonceC = currentClientNonce,
                Target = steamId,
                TargetType = MessageTarget.Client,
                SenderId = MyGameService.OnlineUserId,
                UnixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            PacketRegistry.Send(cleanSpaceChatterPacket, new EndpointId(steamId), newServerNonce, currentClientNonce);
            Common.Logger.Info($"Chatter with dummy payload sent to client ID {steamId}");
        }

        private void ReceiveChatter(CleanSpaceChatterPacket r)
        {
            if (!accepting_chatter) return;
           
            if(ChatterChallengeValidater.ValidateResponse(this.chatterParameters, r.chatterParameters) <= 0)
            {
                Common.Logger.Info($"Client ID {steamId} failed a chatter validation. Rejecting connection.");
                accepting_chatter = false;
                RejectConnection();
            }

            if (this.ConnectionState != CS_CLIENT_STATE.CHATTING)
            {
                Common.Logger.Error($"{Common.PluginName} Client ID {steamId} sent us chatter, but their state was not chattering.");
                RejectConnection();
                return;
            }

            this.lastClientNonce = this.currentClientNonce;
            this.currentClientNonce = r.NonceC;

            if (chatterRemaining > 0)
            {
                SendChatter();
            }
            else
            {
                accepting_chatter = false;
            }
        }

        private int noiseRemaining = 0;
        private async Task SendNoise()
        {
            await Task.Delay(1000);
            noiseRemaining -= 1;
            SendDummyChatterPayload(false);
            if (noiseRemaining > 0)
            {
                await Task.Run(() => SendNoise());
            }
        }
        private async Task TryHelloTask()
        {
            await Task.Delay(Constants.CFG_HELLO_TASK_RETRY_DELAY);
            if (ConnectionState == CS_CLIENT_STATE.PENDING)
                SendHello();
        }

        private void RejectConnection()
        {

            if (this.validationResultData == null){
                this.validationResultData = new ValidationResultData { Code = ValidationResultCode.FAILED_COMMUNICATION, PluginList = null, Success = false };
            }            
        
            string newNonce = GenerateNewNonce();
            PluginValidationResult rs = new PluginValidationResult()
            {
                Code = validationResultData?.Code ?? ValidationResultCode.FAILED_COMMUNICATION,
                Success = false,
                PluginList = validationResultData?.PluginList ?? new List<string>(),
                SenderId = Sync.MyId,
                TargetType = MessageTarget.Client,
                Target = steamId,
                UnixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                NonceS = newNonce
            };

            EventHub.OnServerConnectionRejected(this, steamId, validationResultData);
            Common.Logger.Info($"Sending result packet containing information about rejection to ID {steamId} and scheduling a ticket cancellation for the request.");
            PacketRegistry.Send(rs, new EndpointId(steamId), newNonce);
            Task.Run(() => DelayedDisconnect());
        }
                
        private string GenerateNewNonce()
        {
            if(this.currentServerNonce != null)
                this.lastServerNonce = (string)this.currentServerNonce.Clone();
            return (this.currentServerNonce = ValidationManager.RegisterNonceForPlayer(steamId, true));
        }
        private void ResetState()
        {
            this.payload_dispatched = false;
            this.accepting_chatter = true;
            this._hasReceivedHello = false;
            this.ConnectionState = CS_CLIENT_STATE.PENDING;
            CycleSalt();
        }

        private void PrintP2PConnectionState(ulong steamid)
        {
            MyP2PSessionState state = new MyP2PSessionState();
            if (MyGameService.Peer2Peer.GetSessionState(steamid, ref state))
            {
                Common.Logger.Debug($"Connecting: {state.Connecting}");
                Common.Logger.Debug($"ConnectionActive: {state.ConnectionActive}");
                Common.Logger.Debug($"RemoteIP: {state.RemoteIP}");
                Common.Logger.Debug($"RemotePort: {state.RemotePort}");
            }
        }

        public async Task DelayedDisconnect()
        {
            await Task.Delay(1000);
            this.SessionEndTime = DateTimeOffset.Now.DateTime;
            Common.Logger.Info($"Ticket cancelled for ID {steamId}.");
            ConnectedClientSendJoinPatch.CallPrivateMethod((MyDedicatedServerBase)MyDedicatedServerBase.Instance, steamId, JoinResult.TicketCanceled);
            ClientSessionManager.Instance?.RequestDispose(steamId);
        }

        public async Task DelayedDisconnectWithGroupRedirect()
        {
            await Task.Delay(10000);
            if (((int)ConnectionState) < ((int)CS_CLIENT_STATE.VALIDATION_RESPONDED))
            {
                this.SessionEndTime = DateTimeOffset.Now.DateTime;
                Common.Logger.Info($"No response from ID {steamId} (held connection still present). Attempting to direct to information group.");
                ConnectedClientSendJoinPatch.CallPrivateMethod((MyDedicatedServerBase)MyDedicatedServerBase.Instance, steamId, JoinResult.NotInGroup);
                ClientSessionManager.Instance?.RequestDispose(steamId);
            }
        }

        public void Dispose()
        {
            EventHub.ClientCleanSpaceResponded -= EventHub_ClientCleanSpaceResponded;
            EventHub.CleanSpaceHelloReceived -= EventHub_CleanSpaceHelloReceived;
        }

        private bool session_initiated = false;
        internal void Initiate()
        {
            if (session_initiated) return;
            session_initiated = true;
            Common.Logger.Info($"Session for ID {steamId} initiated.");
            SendHello();
            Task.Run(() => DelayedDisconnectWithGroupRedirect());
        }
    }

    public class ClientSessionManager
    {
        public static ClientSessionManager Instance { get; private set; }
        public static string PluginName => Common.PluginName;
        public static IPluginConfig Config => Common.Plugin.Config;
        public static IPluginLogger Logger => Common.Logger;
        private static ConcurrentDictionary<ulong, ClientSession> _clientSessions = new ConcurrentDictionary<ulong, ClientSession>();

        bool events_initialized = false;
        public ClientSessionManager()
        {
            Instance = this;
            Logger.Info("Clean Space client manager initialized.");
        }

        internal void Init_Events()
        {
            if (events_initialized) return;
            events_initialized = true;
            EventHub.ClientConnected += EventHub_ClientConnected;
            EventHub.ServerCleanSpaceFinalized += EventHub_ServerCleanSpaceFinalized;
        }

        private void EventHub_ServerCleanSpaceFinalized(object sender, CleanSpaceTargetedEventArgs e)
        {
            ClientSession src = (ClientSession)sender;
            ValidationResultData d = (ValidationResultData)e.Args[1];
            string successString = (d.Success ? "SUCCESS" : "FAILURE");
            string pluginListString = (d.PluginList.Count() > 0 ? $" Hashes: {d.PluginList.Aggregate((a, b) => a + "," + b)}" : "No Conflicts");
            Common.Logger.Info($"{Common.PluginName}: Validation for {e.Target} resulted in {successString} with {d.Code.ToString()} {pluginListString}.");
            Common.Logger.Info($"{Common.PluginName}: Started at {src.SessionStartTime.ToShortTimeString()}, ended at {src.SessionEndTime.ToShortTimeString()}.");
        }

        internal bool RequestDispose(ulong steamId)
        {
            ClientSession attempt = GetClient(steamId);
            if (attempt != null)
            {        
                attempt.Dispose();
                return _clientSessions.TryRemove(steamId, out attempt);    
            }
            return false;
        }

        public ClientSession CloseClientSession(ulong steamId)
        {
            if (!_clientSessions.ContainsKey(steamId)) return null;
            _clientSessions[steamId].Dispose();
            ClientSession n;
            _clientSessions.TryRemove(steamId, out n);
            return n;
     
        }

        internal ClientSession GetClient(ulong steamId)
        {
            return _clientSessions.GetValueOrDefault(steamId, null);
        }

        public ClientSession CreateNewClientSession(ulong steamId, ConnectedClientDataMsg initialMsg)
        {
            if(!_clientSessions.TryAdd(steamId, new ClientSession(steamId, initialMsg)))
            {
                Logger.Error($"Failed to create a new session for {steamId}. Could not add to dictionary.");
                return null;
            }

            var newClient = GetClient(steamId);       
            return newClient;
        }

        private void EventHub_ClientConnected(object sender, CleanSpaceEventArgs e)
        {
            ulong target = (ulong)e.Args[0];
            ConnectedClientDataMsg msg = ((ConnectedClientDataMsg)e.Args[1]);
            if(target == 0 || msg.ClientId == null){
                Logger.Error("Client connected but msg had 0 for endpoint ID.");
                return;
            }


            CloseClientSession(target);
            var session = CreateNewClientSession(target, msg);
            session.Initiate();              
            
        }

    }
}
