﻿using System;
using System.Threading;
using System.Threading.Tasks;
using BundtBot.Discord.Models;
using BundtBot.Discord.Models.Events;
using BundtBot.Discord.Models.Gateway;
using BundtBot.Extensions;
using DiscordApiWrapper.Gateway;
using DiscordApiWrapper.Gateway.Models;
using DiscordApiWrapper.Models;
using DiscordApiWrapper.WebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BundtBot.Discord.Gateway
{
    public class DiscordGatewayClient
    {
        public delegate void OperationHandler(string eventJsonData);
        public event Action<GatewayEvent, string> DispatchReceived;
        public event OperationHandler HeartbackAckReceived;
        public event OperationHandler HelloReceived;
        /// <summary>All state info that is set in the Ready and GuildCreated events must be cleared 
        /// when an InvalidSession opcode is received. Once that is done, call SendGatewayIdentify.</summary>
        public event OperationHandler InvalidSessionReceived;

        public delegate void GatewayEventHandler<T>(T eventData);
        /// <summary>This event is sent after Identify, when a Guild becomes available again to the client, 
        /// and when the current user joins a new Guild.</summary>
        public event GatewayEventHandler<DiscordGuild> GuildCreated;
        public event GatewayEventHandler<DiscordMessage> MessageCreated;
        public event GatewayEventHandler<Ready> Ready;
        public event GatewayEventHandler<TypingStart> TypingStart;
        public event GatewayEventHandler<VoiceState> VoiceStateUpdate;
        public event GatewayEventHandler<VoiceServerInfo> VoiceServerUpdate;
        public event GatewayEventHandler<DmChannel> DmChannelCreated;
        public event GatewayEventHandler<GuildChannel> GuildChannelCreated;

        static readonly MyLogger _logger = new MyLogger(nameof(DiscordGatewayClient), ConsoleColor.Cyan);

        readonly WebSocketClient _webSocketClient;
        readonly string _authToken;

        int _lastSequenceReceived;
        bool _readyEventHasNotBeenProcessed = true;
		string _sessionId;
        Timer _heartbeatTimer;

        public DiscordGatewayClient(string authToken, Uri gatewayUri)
        {
            _authToken = authToken;

            var modifiedGatewayUrl = gatewayUri.AddParameter("v", "5").AddParameter("encoding", "'json'");

            _webSocketClient = new WebSocketClient(modifiedGatewayUrl, "Gateway-", ConsoleColor.Cyan);

            HelloReceived += OnHelloReceivedAsync;
            HeartbackAckReceived += (d) => _logger.LogInfo(new LogMessage("HeartbackAck Received ← "), new LogMessage("♥", ConsoleColor.Red));
            Ready += OnReady;
            DispatchReceived += OnDispatchReceived;
            
            _webSocketClient.MessageReceived += OnMessageReceived;
        }

        public async Task ConnectAsync()
        {
            await _webSocketClient.ConnectAsync();
            _logger.LogInfo($"Connected to Gateway", ConsoleColor.Green);
        }

        #region Handlers
        async void OnHelloReceivedAsync(string eventData)
        {
            _logger.LogInfo("Received Hello from Gateway", ConsoleColor.Green);
            var hello = eventData.Deserialize<GatewayHello>();

            if (_readyEventHasNotBeenProcessed)
            {
                StartHeartBeatLoop(hello.HeartbeatInterval);
                await SendIdentifyAsync();
            }
            else
            {
                StartHeartBeatLoop(hello.HeartbeatInterval);
                await SendResumeAsync();
            }
        }

        void StartHeartBeatLoop(TimeSpan heartbeatInterval)
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = new Timer(async (o) => await SendHeartbeatAsync(), null, TimeSpan.Zero, heartbeatInterval);
            _logger.LogInfo($"Heartbeat loop started with interval of {heartbeatInterval.TotalSeconds} seconds", ConsoleColor.Green);
        }

        void OnReady(Ready readyInfo)
        {
            _readyEventHasNotBeenProcessed = false;
            _sessionId = readyInfo.SessionId;
        }

        void OnMessageReceived(string message)
        {
            var payload = message.Deserialize<GatewayPayload>();

            StoreSequenceNumberForHeartbeat(payload);

            LogMessageReceived(message, payload);

            switch (payload.GatewayOpCode)
            {
                case GatewayOpCode.Dispatch: DispatchReceived?.Invoke(payload.EventName.Value, payload.EventData?.ToString()); break;
                case GatewayOpCode.HeartbeatAck: InvokeEvent(HeartbackAckReceived, payload); break;
                case GatewayOpCode.Hello: InvokeEvent(HelloReceived, payload); break;
                case GatewayOpCode.InvalidSession: InvokeEvent(InvalidSessionReceived, payload); break;
                default:
                    _logger.LogWarning($"Received an OpCode with no handler: {payload.GatewayOpCode}");
                    break;
            }
        }

        void LogMessageReceived(string message, GatewayPayload payload)
        {
            _logger.LogDebug($"Message received from gateway (opcode: {payload.GatewayOpCode}, sequence: {payload.SequenceNumber})");
            _logger.LogTrace(message.Prettify());
        }

        void StoreSequenceNumberForHeartbeat(GatewayPayload receivedGatewayDispatch)
        {
            if (receivedGatewayDispatch.SequenceNumber.HasValue)
            {
                _lastSequenceReceived = receivedGatewayDispatch.SequenceNumber.Value;
            }
        }

        void InvokeEvent(OperationHandler handler, GatewayPayload payload)
        {
            handler?.Invoke(payload.EventData?.ToString());
        }

        void OnDispatchReceived(GatewayEvent eventName, string eventJsonData)
        {
            _logger.LogDebug("Processing Gateway Event " + eventName);

            switch (eventName)
            {
                case GatewayEvent.Channel_Create:
                    if (eventJsonData.Deserialize<Channel>().IsPrivate)
                    {
                        HandleEvent<DmChannel>(eventJsonData, "CHANNEL_CREATE", DmChannelCreated);
                    }
                    else
                    {
                        HandleEvent<GuildChannel>(eventJsonData, "CHANNEL_CREATE", GuildChannelCreated);
                    }
                    break;
                case GatewayEvent.Message_Create:
                    HandleEvent<DiscordMessage>(eventJsonData, "MESSAGE_CREATE", MessageCreated);
                    break;
                case GatewayEvent.Guild_Create:
                    HandleEvent<DiscordGuild>(eventJsonData, "GUILD_CREATE", GuildCreated);
                    break;
                case GatewayEvent.Ready:
                    HandleEvent<Ready>(eventJsonData, "READY", Ready);
                    break;
                case GatewayEvent.Typing_Start:
                    HandleEvent<TypingStart>(eventJsonData, "TYPING_START", TypingStart);
                    break;
                case GatewayEvent.Voice_State_Update:
                    HandleEvent<VoiceState>(eventJsonData, "VOICE_STATE_UPDATE", VoiceStateUpdate);
                    break;
                case GatewayEvent.Voice_Server_Update:
                    HandleEvent<VoiceServerInfo>(eventJsonData, "VOICE_SERVER_UPDATE", VoiceServerUpdate);
                    break;
                default:
                    _logger.LogWarning($"Received an Event with no handler: {eventName}");
                    break;
            }
        }

        void LogReceivedEvent(string eventName, string eventDataSummary)
        {
            _logger.LogInfo(
                new LogMessage("Received Event: "),
                new LogMessage(eventName + " ", ConsoleColor.Cyan),
                new LogMessage(eventDataSummary, ConsoleColor.DarkCyan));
        }

        void LogReceivedEvent(string eventName)
        {
            _logger.LogInfo(
                new LogMessage("Received Event: "),
                new LogMessage(eventName + " ", ConsoleColor.Cyan));
        }

        void HandleEvent<T>(string eventJsonData, string eventName, GatewayEventHandler<T> handler)
        {
            var eventObject = eventJsonData.Deserialize<T>();
            LogReceivedEvent(eventName);
            handler?.Invoke(eventObject);
        }
        #endregion

        #region Senders
        public async Task SendHeartbeatAsync()
        {
            _logger.LogInfo(
                new LogMessage("Sending Heartbeat "),
                new LogMessage("♥", ConsoleColor.Red),
                new LogMessage(" →"));
            await SendOpCodeAsync(GatewayOpCode.Heartbeat, _lastSequenceReceived);
        }

        public async Task SendIdentifyAsync()
        {
            _logger.LogInfo("Sending GatewayIdentify to Gateway", ConsoleColor.Green);
            await SendOpCodeAsync(GatewayOpCode.Identify, new GatewayIdentify
            {
                AuthenticationToken = _authToken,
                ConnectionProperties = new ConnectionProperties
                {
                    OperatingSystem = "windows",
                    Browser = "bundtbot",
                    Device = "bundtbot",
                    Referrer = "",
                    ReferringDomain = "",
                },
                SupportsCompression = false,
                LargeThreshold = Threshold.Maximum,
                Shard = new int[] {0, 1}
            });
        }

        async Task SendResumeAsync()
        {
            _logger.LogInfo($"Sending GatewayResume to Gateway (session_id: {_sessionId})", ConsoleColor.Green);
            await SendOpCodeAsync(GatewayOpCode.Resume, new GatewayResume
            {
                SessionToken = _authToken,
                SessionId = _sessionId,
                LastSequenceNumberReceived = _lastSequenceReceived
            });
        }

        public async Task SendStatusUpdateAsync(StatusUpdate statusUpdate)
        {
            _logger.LogInfo("Sending StatusUpdate to Gateway " +
                            $"(idle since: {statusUpdate.IdleSince}, " +
                            $"game: {statusUpdate.Game.Name})",
                            ConsoleColor.Green);
            await SendOpCodeAsync(GatewayOpCode.StatusUpdate, statusUpdate);
        }

        public async Task SendVoiceStateUpdateAsync(GatewayVoiceStateUpdate gatewayVoiceStateUpdate)
        {
            _logger.LogInfo("Sending VoiceStateUpdate to Gateway " +
                            $"(guild: {gatewayVoiceStateUpdate.GuildId}, " +
                            $"channel: {gatewayVoiceStateUpdate.VoiceChannelId})",
                            ConsoleColor.Green);
            await SendOpCodeAsync(GatewayOpCode.VoiceStateUpdate, gatewayVoiceStateUpdate);
        }

        async Task SendOpCodeAsync(GatewayOpCode opCode, object eventData)
        {
            try
            {
                var gatewayPayload = new GatewayPayload(opCode, eventData);

                _logger.LogDebug($"Sending opcode {gatewayPayload.GatewayOpCode} to gateway...");
                _logger.LogTrace("" + JObject.FromObject(gatewayPayload));

                var jsonGatewayPayload = JsonConvert.SerializeObject(gatewayPayload);
                await _webSocketClient.SendMessageUsingQueueAsync(jsonGatewayPayload);

                _logger.LogDebug($"Sent {gatewayPayload.GatewayOpCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex);
                throw;
            }
        }
        #endregion

        // TODO Implement these gateway client requests:
        //case OpCode.VoiceServerPing: break;
        //case OpCode.RequestGuildMembers: break;
    }
}
