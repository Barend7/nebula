﻿using HarmonyLib;
using NebulaModel;
using NebulaModel.DataStructures;
using NebulaModel.Logger;
using NebulaModel.Networking;
using NebulaModel.Packets.Routers;
using NebulaModel.Packets.Session;
using NebulaModel.Utils;
using NebulaWorld;
using System.Net;
using System.Net.Sockets;
using WebSocketSharp;

namespace NebulaNetwork
{
    public class Client : NetworkProvider
    {
        private readonly IPEndPoint serverEndpoint;
        private WebSocket clientSocket;
        private NebulaConnection serverConnection;

        public Client(string url, int port)
            : this(new IPEndPoint(Dns.GetHostEntry(url).AddressList[0], port))
        {
        }

        public Client(IPEndPoint endpoint)
        {
            serverEndpoint = endpoint;

            PacketUtils.RegisterAllPacketNestedTypes(PacketProcessor);
            PacketUtils.RegisterAllPacketProcessorsInCallingAssembly(PacketProcessor, false);
#if DEBUG
            PacketProcessor.SimulateLatency = true;
#endif
        }

        public override void Start()
        {
            clientSocket = new WebSocket($"ws://{serverEndpoint}/socket");
            clientSocket.OnOpen += ClientSocket_OnOpen;
            clientSocket.OnClose += ClientSocket_OnClose;
            clientSocket.OnMessage += ClientSocket_OnMessage;
        }

        public override void Stop()
        {
            clientSocket?.Close((ushort)DisconnectionReason.ClientRequestedDisconnect, "Player left the game");
        }

        public override void Dispose()
        {
            Stop();
        }

        public override void SendPacket<T>(T packet)
        {
            serverConnection?.SendPacket(packet);
        }

        public override void SendPacketToLocalStar<T>(T packet)
        {
            serverConnection?.SendPacket(new StarBroadcastPacket(PacketProcessor.Write(packet), GameMain.data.localStar?.id ?? -1));
        }

        public override void SendPacketToLocalPlanet<T>(T packet)
        {
            serverConnection?.SendPacket(new PlanetBroadcastPacket(PacketProcessor.Write(packet), GameMain.mainPlayer.planetId));
        }

        public override void SendPacketToPlanet<T>(T packet, int planetId)
        {
            // Only possible from host
            throw new System.NotImplementedException();
        }

        public override void SendPacketToStar<T>(T packet, int starId)
        {
            // Only possible from host
            throw new System.NotImplementedException();
        }

        public override void SendPacketToStarExclude<T>(T packet, int starId, NebulaConnection exclude)
        {
            // Only possible from host
            throw new System.NotImplementedException();
        }

        private void ClientSocket_OnMessage(object sender, MessageEventArgs e)
        {
            PacketProcessor.EnqueuePacketForProcessing(e.RawData, new NebulaConnection(clientSocket, serverEndpoint, PacketProcessor));
        }

        private void ClientSocket_OnOpen(object sender, System.EventArgs e)
        {
            DisableNagleAlgorithm(clientSocket);

            Log.Info($"Server connection established: {clientSocket.Url}");
            serverConnection = new NebulaConnection(clientSocket, serverEndpoint, PacketProcessor);

            //TODO: Maybe some challenge-response authentication mechanism?

            SendPacket(new HandshakeRequest(
                CryptoUtils.GetPublicKey(CryptoUtils.GetOrCreateUserCert()),
                !string.IsNullOrWhiteSpace(Config.Options.Nickname) ? Config.Options.Nickname : GameMain.data.account.userName,
                new Float3(Config.Options.MechaColorR / 255, Config.Options.MechaColorG / 255, Config.Options.MechaColorB / 255),
                LocalPlayer.GS2_GSSettings != null));
        }

        private void ClientSocket_OnClose(object sender, CloseEventArgs e)
        {
            serverConnection = null;
        }

        static void DisableNagleAlgorithm(WebSocket socket)
        {
            var tcpClient = AccessTools.FieldRefAccess<WebSocket, TcpClient>("_tcpClient")(socket);
            tcpClient.NoDelay = true;
        }
    }
}