﻿#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using NebulaAPI.Interfaces;
using NebulaAPI.Packets;
using NebulaModel.Logger;

#endregion

namespace NebulaModel.Networking.Serialization;

public class NetPacketProcessor
{
    private readonly Dictionary<ulong, SubscribeDelegate> _callbacks = [];
    private readonly Dictionary<ulong, Type> _callbacksDebugInfo = [];
    private readonly NetDataWriter _netDataWriter = new();
    private readonly NetSerializer _netSerializer;
    private readonly List<DelayedPacket> delayedPackets = [];
    private readonly Queue<PendingPacket> pendingPackets = new();

    private readonly Random simulationRandom = new();
    private readonly int SimulatedMaxLatency = 50;
    private readonly int SimulatedMinLatency = 20;

    public bool SimulateLatency = false;

    public NetPacketProcessor()
    {
        _netSerializer = new NetSerializer();
    }

    public NetPacketProcessor(int maxStringLength)
    {
        _netSerializer = new NetSerializer(maxStringLength);
    }

    public bool Enable { get; set; } = true;

    public void EnqueuePacketForProcessing(byte[] rawData, object userData)
    {
#if DEBUG
        if (SimulateLatency)
        {
            lock (delayedPackets)
            {
                var packet = new PendingPacket(rawData, userData);
                var dueTime = DateTime.UtcNow.AddMilliseconds(simulationRandom.Next(SimulatedMinLatency, SimulatedMaxLatency));
                delayedPackets.Add(new DelayedPacket(packet, dueTime));
            }
        }
        else
        {
            lock (pendingPackets)
            {
                pendingPackets.Enqueue(new PendingPacket(rawData, userData));
            }
        }
#else
        lock (pendingPackets)
        {
            pendingPackets.Enqueue(new PendingPacket(rawData, userData));
        }
#endif
    }

    public void ProcessPacketQueue()
    {
        lock (pendingPackets)
        {
            ProcessDelayedPackets();

            while (pendingPackets.Count > 0 && Enable)
            {
                var packet = pendingPackets.Dequeue();
                ReadPacket(new NetDataReader(packet.Data), packet.UserData);
            }
        }
    }

    [Conditional("DEBUG")]
    private void ProcessDelayedPackets()
    {
        lock (delayedPackets)
        {
            var now = DateTime.UtcNow;
            var deleteCount = 0;

            for (var i = 0; i < delayedPackets.Count; ++i)
            {
                if (now >= delayedPackets[i].DueTime)
                {
                    pendingPackets.Enqueue(delayedPackets[i].Packet);
                    deleteCount = i + 1;
                }
                else
                {
                    // We need to break to avoid messing up the order of the packets.
                    break;
                }
            }

            if (deleteCount > 0)
            {
                delayedPackets.RemoveRange(0, deleteCount);
            }
        }
    }

    //FNV-1 64 bit hash
    protected virtual ulong GetHash<T>()
    {
        if (HashCache<T>.Initialized)
        {
            return HashCache<T>.Id;
        }

        var hash = 14695981039346656037UL; //offset
        var typeName = typeof(T).FullName;
        if (typeName != null)
        {
            foreach (var t in typeName)
            {
                hash ^= t;
                hash *= 1099511628211UL; //prime
            }
        }
        HashCache<T>.Initialized = true;
        HashCache<T>.Id = hash;
        return hash;
    }

    protected virtual SubscribeDelegate GetCallbackFromData(NetDataReader reader)
    {
        var hash = reader.GetULong();
        if (!_callbacks.TryGetValue(hash, out var action))
        {
            Log.Warn($"Unknown packet hash: {hash}");
            throw new Exception("Undefined packet in NetDataReader");
        }

#if DEBUG
        if (_callbacksDebugInfo.TryGetValue(hash, out var packetType))
        {
            if (!packetType.IsDefined(typeof(HidePacketInDebugLogsAttribute), false))
            {
                Log.Debug($"Packet Received: {packetType.Name}");
            }
        }
        else
        {
            Log.Warn($"Packet not registered: {hash}");
        }
#endif

        return action;
    }

    protected virtual void WriteHash<T>(NetDataWriter writer)
    {
        writer.Put(GetHash<T>());
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Produces error when static")]
    public T CreateNestedClassInstance<T>() where T : class, INetSerializable, new()
    {
        return new T();
    }

    /// <summary>
    ///     Register nested property type
    /// </summary>
    /// <typeparam name="T">INetSerializable structure</typeparam>
    public void RegisterNestedType<T>() where T : struct, INetSerializable
    {
        _netSerializer.RegisterNestedType<T>();
    }

    /// <summary>
    ///     Register nested property type
    /// </summary>
    /// <param name="writeDelegate"></param>
    /// <param name="readDelegate"></param>
    public void RegisterNestedType<T>(Action<NetDataWriter, T> writeDelegate, Func<NetDataReader, T> readDelegate)
    {
        _netSerializer.RegisterNestedType(writeDelegate, readDelegate);
    }

    /// <summary>
    ///     Register nested property type
    /// </summary>
    /// <typeparam name="T">INetSerializable class</typeparam>
    public void RegisterNestedType<T>(Func<T> constructor) where T : class, INetSerializable
    {
        _netSerializer.RegisterNestedType(constructor);
    }

    /// <summary>
    ///     Reads all available data from NetDataReader and calls OnReceive delegates
    /// </summary>
    /// <param name="reader">NetDataReader with packets data</param>
    public void ReadAllPackets(NetDataReader reader)
    {
        while (reader.AvailableBytes > 0)
        {
            ReadPacket(reader);
        }
    }

    /// <summary>
    ///     Reads all available data from NetDataReader and calls OnReceive delegates
    /// </summary>
    /// <param name="reader">NetDataReader with packets data</param>
    /// <param name="userData">Argument that passed to OnReceivedEvent</param>
    /// <exception cref="ParseException">Malformed packet</exception>
    public void ReadAllPackets(NetDataReader reader, object userData)
    {
        while (reader.AvailableBytes > 0)
        {
            ReadPacket(reader, userData);
        }
    }

    /*        public void Send<T>(NetPeer peer, T packet, DeliveryMethod options) where T : class, new()
            {
                _netDataWriter.Reset();
                Write(_netDataWriter, packet);
                peer.Send(_netDataWriter, options);
            }

            public void SendNetSerializable<T>(NetPeer peer, T packet, DeliveryMethod options) where T : INetSerializable
            {
                _netDataWriter.Reset();
                WriteNetSerializable(_netDataWriter, packet);
                peer.Send(_netDataWriter, options);
            }

            public void Send<T>(NetManager manager, T packet, DeliveryMethod options) where T : class, new()
            {
                _netDataWriter.Reset();
                Write(_netDataWriter, packet);
                manager.SendToAll(_netDataWriter, options);
            }

            public void SendNetSerializable<T>(NetManager manager, T packet, DeliveryMethod options) where T : INetSerializable
            {
                _netDataWriter.Reset();
                WriteNetSerializable(_netDataWriter, packet);
                manager.SendToAll(_netDataWriter, options);
            }*/

    public void Write<T>(NetDataWriter writer, T packet) where T : class, new()
    {
        WriteHash<T>(writer);
        _netSerializer.Serialize(writer, packet);
    }

    public void WriteNetSerializable<T>(NetDataWriter writer, T packet) where T : INetSerializable
    {
        WriteHash<T>(writer);
        packet.Serialize(writer);
    }

    public byte[] Write<T>(T packet) where T : class, new()
    {
#if DEBUG
        if (!typeof(T).IsDefined(typeof(HidePacketInDebugLogsAttribute), false))
        {
            Log.Debug($"Packet Sent: {packet.GetType().Name}");
        }
#endif
        _netDataWriter.Reset();
        WriteHash<T>(_netDataWriter);
        _netSerializer.Serialize(_netDataWriter, packet);
        return _netDataWriter.CopyData();
    }

    public byte[] WriteNetSerializable<T>(T packet) where T : INetSerializable
    {
        _netDataWriter.Reset();
        WriteHash<T>(_netDataWriter);
        packet.Serialize(_netDataWriter);
        return _netDataWriter.CopyData();
    }

    /// <summary>
    ///     Reads one packet from NetDataReader and calls OnReceive delegate
    /// </summary>
    /// <param name="reader">NetDataReader with packet</param>
    /// <param name="userData">Argument that passed to OnReceivedEvent</param>
    /// <exception cref="ParseException">Malformed packet</exception>
    private void ReadPacket(NetDataReader reader, object userData = null)
    {
        GetCallbackFromData(reader)(reader, userData);
    }

    /// <summary>
    ///     Register and subscribe to packet receive event
    /// </summary>
    /// <param name="onReceive">event that will be called when packet deserialized with ReadPacket method</param>
    /// <param name="packetConstructor">Method that constructs packet instead of slow Activator.CreateInstance</param>
    /// <exception cref="InvalidTypeException"><typeparamref name="T" />'s fields are not supported, or it has no fields</exception>
    public void Subscribe<T>(Action<T> onReceive, Func<T> packetConstructor) where T : class, new()
    {
        _netSerializer.Register<T>();
        _callbacks[GetHash<T>()] = (reader, userData) =>
        {
            var reference = packetConstructor();
            _netSerializer.Deserialize(reader, reference);
            onReceive(reference);
        };
    }

    /// <summary>
    ///     Register and subscribe to packet receive event (with userData)
    /// </summary>
    /// <param name="onReceive">event that will be called when packet deserialized with ReadPacket method</param>
    /// <param name="packetConstructor">Method that constructs packet instead of slow Activator.CreateInstance</param>
    /// <exception cref="InvalidTypeException"><typeparamref name="T" />'s fields are not supported, or it has no fields</exception>
    public void Subscribe<T, TUserData>(Action<T, TUserData> onReceive, Func<T> packetConstructor) where T : class, new()
    {
        _netSerializer.Register<T>();
        _callbacks[GetHash<T>()] = (reader, userData) =>
        {
            var reference = packetConstructor();
            _netSerializer.Deserialize(reader, reference);
            onReceive(reference, (TUserData)userData);
        };
    }

    /// <summary>
    ///     Register and subscribe to packet receive event
    ///     This method will overwrite last received packet class on receive (less garbage)
    /// </summary>
    /// <param name="onReceive">event that will be called when packet deserialized with ReadPacket method</param>
    /// <exception cref="InvalidTypeException"><typeparamref name="T" />'s fields are not supported, or it has no fields</exception>
    public void SubscribeReusable<T>(Action<T> onReceive) where T : class, new()
    {
        _netSerializer.Register<T>();
        var reference = new T();
        _callbacks[GetHash<T>()] = (reader, userData) =>
        {
            _netSerializer.Deserialize(reader, reference);
            onReceive(reference);
        };
    }

    /// <summary>
    ///     Register and subscribe to packet receive event
    ///     This method will overwrite last received packet class on receive (less garbage)
    /// </summary>
    /// <param name="onReceive">event that will be called when packet deserialized with ReadPacket method</param>
    /// <exception cref="InvalidTypeException"><typeparamref name="T" />'s fields are not supported, or it has no fields</exception>
    public void SubscribeReusable<T, TUserData>(Action<T, TUserData> onReceive) where T : class, new()
    {
        _netSerializer.Register<T>();
        var reference = new T();
        _callbacks[GetHash<T>()] = (reader, userData) =>
        {
            _netSerializer.Deserialize(reader, reference);
            onReceive(reference, (TUserData)userData);
        };

#if DEBUG
        _callbacksDebugInfo[GetHash<T>()] = typeof(T);
#endif
    }

    public void SubscribeNetSerializable<T, TUserData>(
        Action<T, TUserData> onReceive,
        Func<T> packetConstructor) where T : INetSerializable
    {
        _callbacks[GetHash<T>()] = (reader, userData) =>
        {
            var pkt = packetConstructor();
            pkt.Deserialize(reader);
            onReceive(pkt, (TUserData)userData);
        };
    }

    public void SubscribeNetSerializable<T>(
        Action<T> onReceive,
        Func<T> packetConstructor) where T : INetSerializable
    {
        _callbacks[GetHash<T>()] = (reader, userData) =>
        {
            var pkt = packetConstructor();
            pkt.Deserialize(reader);
            onReceive(pkt);
        };
    }

    public void SubscribeNetSerializable<T, TUserData>(
        Action<T, TUserData> onReceive) where T : INetSerializable, new()
    {
        var reference = new T();
        _callbacks[GetHash<T>()] = (reader, userData) =>
        {
            reference.Deserialize(reader);
            onReceive(reference, (TUserData)userData);
        };
    }

    public void SubscribeNetSerializable<T>(
        Action<T> onReceive) where T : INetSerializable, new()
    {
        var reference = new T();
        _callbacks[GetHash<T>()] = (reader, userData) =>
        {
            reference.Deserialize(reader);
            onReceive(reference);
        };
    }

    /// <summary>
    ///     Remove any subscriptions by type
    /// </summary>
    /// <typeparam name="T">Packet type</typeparam>
    /// <returns>true if remove is success</returns>
    public bool RemoveSubscription<T>()
    {
        return _callbacks.Remove(GetHash<T>());
    }

    private static class HashCache<T>
    {
        public static bool Initialized;
        public static ulong Id;
    }

    protected delegate void SubscribeDelegate(NetDataReader reader, object userData);
}
