﻿using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.RyuLdn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Spacemeowx2Ldn.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Spacemeowx2Ldn.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Spacemeowx2Ldn
{
    internal class LanDiscovery : IDisposable
    {
        private const int DEFAULT_PORT = 11452;
        private const ushort COMMON_CHANNEL = 6;
        private const byte COMMON_LINK_LEVEL = 3;
        private const byte COMMON_NETWORK_TYPE = 2;

        private const int FailureTimeout = 4000;

        private Spacemeowx2LdnClient _parent;
        private LanProtocol _protocol;
        private bool _initialized;
        private readonly Ssid _fakeSsid;
        private ILdnTcpSocket _tcp;
        private LdnProxyUdpServer _udp, _udp2;
        private List<LdnProxyTcpSession> _stations = new();
        private object _lock = new();

        private AutoResetEvent _apConnected = new(false);

        internal readonly IPAddress LocalAddr;
        internal readonly IPAddress LocalBroadcastAddr;
        internal NetworkInfo NetworkInfo;

        public bool IsHost => _tcp is LdnProxyTcpServer;

        private Random _random = new();

        // NOTE: Credit to https://stackoverflow.com/a/39338188
        private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress mask)
        {
            uint ipAddress = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
            uint ipMaskV4 = BitConverter.ToUInt32(mask.GetAddressBytes(), 0);
            uint broadCastIpAddress = ipAddress | ~ipMaskV4;

            return new IPAddress(BitConverter.GetBytes(broadCastIpAddress));
        }

        private static NetworkInfo GetEmptyNetworkInfo()
        {
            NetworkInfo networkInfo = new()
            {
                NetworkId = new()
                {
                    SessionId = new Array16<byte>()
                },
                Common = new()
                {
                    MacAddress = new Array6<byte>(),
                    Ssid = new()
                    {
                        Name = new Array33<byte>()
                    }
                },
                Ldn = new()
                {
                    NodeCountMax = LanProtocol.NodeCountMax,
                    SecurityParameter = new Array16<byte>(),
                    Nodes = new Array8<NodeInfo>(),
                    AdvertiseData = new Array384<byte>(),
                    Reserved4 = new Array140<byte>()
                }
            };

            for (int i = 0; i < LanProtocol.NodeCountMax; i++)
            {
                networkInfo.Ldn.Nodes[i] = new()
                {
                    MacAddress = new Array6<byte>(),
                    UserName = new Array33<byte>(),
                    Reserved2 = new Array16<byte>()
                };
            }

            return networkInfo;
        }

        public LanDiscovery(Spacemeowx2LdnClient parent, IPAddress ipAddress, IPAddress ipv4mask)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"Initialize LanDiscovery using IP: {ipAddress}");

            _parent = parent;
            LocalAddr = ipAddress;
            LocalBroadcastAddr = GetBroadcastAddress(ipAddress, ipv4mask);

            _fakeSsid = new()
            {
                Length = (byte)LanProtocol.SsidLengthMax,
            };
            Encoding.ASCII.GetBytes("12345678123456781234567812345678").CopyTo(_fakeSsid.Name.AsSpan());

            _protocol = new LanProtocol(this);
            _protocol.Accept += OnConnect;
            _protocol.SyncNetwork += OnSyncNetwork;
            _protocol.DisconnectStation += DisconnectStation;

            NetworkInfo = GetEmptyNetworkInfo();

            ResetStations();

            if (!InitUdp())
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "LanDiscovery Initialize: InitUdp failed.");

                return;
            }

            _initialized = true;
        }

        protected void OnSyncNetwork(NetworkInfo info)
        {
            bool updated = false;

            lock (_lock)
            {
                if (!NetworkInfo.Equals(info))
                {
                    NetworkInfo = info;
                    updated = true;

                    //Logger.Debug?.PrintMsg(LogClass.ServiceLdn, $"Received NetworkInfo:\n{JsonHelper.Serialize(info, true)}");
                    Logger.Debug?.PrintMsg(LogClass.ServiceLdn, $"Host IP: {NetworkHelpers.ConvertUint(info.Ldn.Nodes[0].Ipv4Address)}");
                }
            }

            if (updated)
            {
                _parent.InvokeNetworkChange(info, true);
            }

            _apConnected.Set();
        }

        protected void OnConnect(LdnProxyTcpSession station)
        {
            lock (_lock)
            {
                station.NodeId = LocateEmptyNode();

                if (_stations.Count > LanProtocol.StationCountMax || station.NodeId == -1)
                {
                    station.Disconnect();
                    station.Dispose();

                    return;
                }

                _stations.Add(station);

                UpdateNodes();
            }
        }

        public void DisconnectStation(LdnProxyTcpSession station)
        {
            if (!station.IsDisposed)
            {
                if (station.IsConnected)
                {
                    station.Disconnect();
                }

                station.Dispose();
            }

            lock (_lock)
            {
                if (_stations.Remove(station))
                {
                    NetworkInfo.Ldn.Nodes[station.NodeId] = new NodeInfo()
                    {
                        MacAddress = new Array6<byte>(),
                        UserName = new Array33<byte>(),
                        Reserved2 = new Array16<byte>()
                    };

                    UpdateNodes();
                }
            }
        }

        public bool SetAdvertiseData(byte[] data)
        {
            if (data.Length > (int)LanProtocol.AdvertiseDataSizeMax)
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "AdvertiseData exceeds size limit.");

                return false;
            }

            data.CopyTo(NetworkInfo.Ldn.AdvertiseData.AsSpan());
            NetworkInfo.Ldn.AdvertiseDataSize = (ushort)data.Length;

            // NOTE: Otherwise this results in SessionKeepFailed or MasterDisconnected
            lock (_lock)
            {
                if (NetworkInfo.Ldn.Nodes[0].IsConnected == 1)
                {
                    UpdateNodes(true);
                }
            }

            return true;
        }

        public void InitNetworkInfo()
        {
            lock (_lock)
            {
                NetworkInfo.Common.MacAddress = GetFakeMac();
                NetworkInfo.Common.Channel = COMMON_CHANNEL;
                NetworkInfo.Common.LinkLevel = COMMON_LINK_LEVEL;
                NetworkInfo.Common.NetworkType = COMMON_NETWORK_TYPE;
                NetworkInfo.Common.Ssid = _fakeSsid;

                NetworkInfo.Ldn.Nodes = new Array8<NodeInfo>();

                for (int i = 0; i < LanProtocol.NodeCountMax; i++)
                {
                    NetworkInfo.Ldn.Nodes[i].NodeId = (byte)i;
                    NetworkInfo.Ldn.Nodes[i].IsConnected = 0;
                }
            }
        }

        protected Array6<byte> GetFakeMac(IPAddress address = null)
        {
            if (address == null)
            {
                address = LocalAddr;
            }

            byte[] ip = address.GetAddressBytes();

            var macAddress = new Array6<byte>();
            new byte[] { 0x02, 0x00, ip[0], ip[1], ip[2], ip[3] }.CopyTo(macAddress.AsSpan());

            return macAddress;
        }

        public bool InitTcp(bool listening, IPAddress address = null, int port = DEFAULT_PORT)
        {
            Logger.Debug?.PrintMsg(LogClass.ServiceLdn, $"LanDiscovery InitTcp: IP: {address}, listening: {listening}");

            if (_tcp != null)
            {
                _tcp.DisconnectAndStop();
                _tcp.Dispose();
                _tcp = null;
            }

            ILdnTcpSocket tcpSocket;

            if (listening)
            {
                try
                {
                    if (address == null)
                    {
                        address = LocalAddr;
                    }

                    tcpSocket = new LdnProxyTcpServer(_protocol, address, port);
                }
                catch (Exception ex)
                {
                    Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"Failed to create LdnProxyTcpServer: {ex}");

                    return false;
                }

                if (!tcpSocket.Start())
                {
                    return false;
                }
            }
            else
            {
                if (address == null)
                {
                    return false;
                }

                try
                {
                    tcpSocket = new LdnProxyTcpClient(_protocol, address, port);
                }
                catch (Exception ex)
                {
                    Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"Failed to create LdnProxyTcpClient: {ex}");

                    return false;
                }
            }

            _tcp = tcpSocket;

            return true;
        }

        public bool InitUdp()
        {
            _udp?.Stop();

            try
            {
                // NOTE: Linux won't receive any broadcast packets if the socket is not bound to the broadcast address.
                //       Windows only works if bound to localhost or the local address.
                //       See this discussion: https://stackoverflow.com/questions/13666789/receiving-udp-broadcast-packets-on-linux
                if (OperatingSystem.IsLinux())
                {
                    _udp2 = new LdnProxyUdpServer(_protocol, LocalBroadcastAddr, DEFAULT_PORT);
                }

                _udp = new LdnProxyUdpServer(_protocol, LocalAddr, DEFAULT_PORT);
            }
            catch (Exception ex)
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"Failed to create LdnProxyUdpServer: {ex}");

                return false;
            }

            return true;
        }

        public NetworkInfo[] Scan(ushort channel, ScanFilter filter)
        {
            _udp.ClearScanResults();

            if (_protocol.SendBroadcast(_udp, LanPacketType.Scan, DEFAULT_PORT) < 0)
            {
                return Array.Empty<NetworkInfo>();
            }

            List<NetworkInfo> outNetworkInfo = new();

            foreach (KeyValuePair<ulong, NetworkInfo> item in _udp.GetScanResults())
            {
                bool copy = true;

                if (filter.Flag.HasFlag(ScanFilterFlag.LocalCommunicationId))
                {
                    copy &= filter.NetworkId.IntentId.LocalCommunicationId == item.Value.NetworkId.IntentId.LocalCommunicationId;
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.SessionId))
                {
                    copy &= filter.NetworkId.SessionId.AsSpan().SequenceEqual(item.Value.NetworkId.SessionId.AsSpan());
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.NetworkType))
                {
                    copy &= filter.NetworkType == (NetworkType)item.Value.Common.NetworkType;
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.Ssid))
                {
                    Span<byte> gameSsid = item.Value.Common.Ssid.Name.AsSpan()[item.Value.Common.Ssid.Length..];
                    Span<byte> scanSsid = filter.Ssid.Name.AsSpan()[filter.Ssid.Length..];
                    copy &= gameSsid.SequenceEqual(scanSsid);
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.SceneId))
                {
                    copy &= filter.NetworkId.IntentId.SceneId == item.Value.NetworkId.IntentId.SceneId;
                }

                if (copy)
                {
                    if (item.Value.Ldn.Nodes[0].UserName[0] != 0)
                    {
                        outNetworkInfo.Add(item.Value);
                    }
                    else
                    {
                        Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "LanDiscovery Scan: Got empty Username. There might be a timing issue somewhere...");
                    }
                }
            }

            return outNetworkInfo.ToArray();
        }

        protected void ResetStations()
        {
            lock (_lock)
            {
                foreach (LdnProxyTcpSession station in _stations)
                {
                    station.Disconnect();
                    station.Dispose();
                }

                _stations.Clear();
            }
        }

        private int LocateEmptyNode()
        {
            Array8<NodeInfo> nodes = NetworkInfo.Ldn.Nodes;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].IsConnected == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        protected void UpdateNodes(bool forceUpdate = false)
        {
            int countConnected = 1;

            foreach (LdnProxyTcpSession station in _stations.Where(station => station.IsConnected))
            {
                countConnected++;
                station.OverrideInfo();
                // NOTE: This is not part of the original implementation.
                NetworkInfo.Ldn.Nodes[station.NodeId] = station.NodeInfo;
            }

            byte nodeCount = (byte)countConnected;

            bool networkInfoChanged = forceUpdate || NetworkInfo.Ldn.NodeCount != nodeCount;

            NetworkInfo.Ldn.NodeCount = nodeCount;

            foreach (LdnProxyTcpSession station in _stations)
            {
                if (station.IsConnected)
                {
                    if (_protocol.SendPacket(station, LanPacketType.SyncNetwork, LdnHelper.StructureToByteArray(NetworkInfo)) < 0)
                    {
                        Logger.Error?.PrintMsg(LogClass.ServiceLdn, $"Failed to send {LanPacketType.SyncNetwork} to station {station.NodeId}");
                    }
                }
            }

            if (networkInfoChanged)
            {
                _parent.InvokeNetworkChange(NetworkInfo, true);
            }
        }

        protected NodeInfo GetNodeInfo(NodeInfo node, UserConfig userConfig, ushort localCommunicationVersion)
        {
            uint ipAddress = NetworkHelpers.ConvertIpv4Address(LocalAddr);

            node.MacAddress = GetFakeMac();
            node.IsConnected = 1;
            node.UserName = userConfig.UserName;
            node.LocalCommunicationVersion = localCommunicationVersion;
            node.Ipv4Address = ipAddress;

            return node;
        }

        public bool CreateNetwork(SecurityConfig securityConfig, UserConfig userConfig, NetworkConfig networkConfig)
        {
            if (!InitTcp(true))
            {
                return false;
            }

            InitNetworkInfo();

            NetworkInfo.Ldn.NodeCountMax = networkConfig.NodeCountMax;
            NetworkInfo.Ldn.SecurityMode = (ushort)securityConfig.SecurityMode;

            NetworkInfo.Common.Channel = networkConfig.Channel == 0 ? (ushort)6 : networkConfig.Channel;

            NetworkInfo.NetworkId.SessionId = new Array16<byte>();
            _random.NextBytes(NetworkInfo.NetworkId.SessionId.AsSpan());
            NetworkInfo.NetworkId.IntentId = networkConfig.IntentId;

            NetworkInfo.Ldn.Nodes[0] = GetNodeInfo(NetworkInfo.Ldn.Nodes[0], userConfig, networkConfig.LocalCommunicationVersion);
            NetworkInfo.Ldn.Nodes[0].IsConnected = 1;
            NetworkInfo.Ldn.NodeCount++;

            _parent.InvokeNetworkChange(NetworkInfo, true);

            return true;
        }

        public void DestroyNetwork()
        {
            if (_tcp != null)
            {
                try
                {
                    _tcp.DisconnectAndStop();
                }
                finally
                {
                    _tcp.Dispose();
                    _tcp = null;
                }
            }

            ResetStations();
        }

        public NetworkError Connect(NetworkInfo networkInfo, UserConfig userConfig, uint localCommunicationVersion)
        {
            _apConnected.Reset();

            if (networkInfo.Ldn.NodeCount == 0)
            {
                return NetworkError.Unknown;
            }

            IPAddress address = NetworkHelpers.ConvertUint(networkInfo.Ldn.Nodes[0].Ipv4Address);

            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"Connecting to host: {address}");

            if (!InitTcp(false, address))
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "Could not initialize TCPClient");

                return NetworkError.ConnectNotFound;
            }

            if (!_tcp.Connect())
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "Failed to connect.");

                return NetworkError.ConnectFailure;
            }

            NodeInfo myNode = GetNodeInfo(new NodeInfo(), userConfig, (ushort)localCommunicationVersion);
            if (_protocol.SendPacket(_tcp, LanPacketType.Connect, LdnHelper.StructureToByteArray(myNode)) < 0)
            {
                return NetworkError.Unknown;
            }

            bool signalled = _apConnected.WaitOne(FailureTimeout);

            return signalled ? NetworkError.None : NetworkError.ConnectTimeout;
        }

        public void Dispose()
        {
            if (_initialized)
            {
                DisconnectAndStop();
                ResetStations();
                _initialized = false;
            }

            _protocol.Accept -= OnConnect;
            _protocol.SyncNetwork -= OnSyncNetwork;
            _protocol.DisconnectStation -= DisconnectStation;
        }

        public void DisconnectAndStop()
        {
            if (_udp != null)
            {
                try
                {
                    _udp.Stop();
                }
                finally
                {
                    _udp.Dispose();
                    _udp = null;
                }
            }

            if (_udp2 != null)
            {
                try
                {
                    _udp2.Stop();
                }
                finally
                {
                    _udp2.Dispose();
                    _udp2 = null;
                }
            }

            if (_tcp != null)
            {
                try
                {
                    _tcp.DisconnectAndStop();
                }
                finally
                {
                    _tcp.Dispose();
                    _tcp = null;
                }
            }
        }
    }
}
