﻿using Ryujinx.Common.Logging;
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
        private const int    DEFAULT_PORT        = 11452;
        private const ushort COMMON_CHANNEL      = 6;
        private const byte   COMMON_LINK_LEVEL   = 3;
        private const byte   COMMON_NETWORK_TYPE = 2;

        private const int FailureTimeout = 4000;

        private Spacemeowx2LdnClient     _parent;
        private LanProtocol              _protocol;
        private bool                     _initialized;
        private readonly Ssid            _fakeSsid;
        private ILdnTcpSocket            _tcp;
        private LdnProxyUdpServer        _udp, _udp2;
        private List<LdnProxyTcpSession> _stations = new();
        private object                   _lock = new();

        private AutoResetEvent _apConnected = new(false);

        internal readonly IPAddress      LocalAddr;
        internal readonly IPAddress      LocalBroadcastAddr;
        internal NetworkInfo             NetworkInfo;

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
                    SessionId = new byte[16]
                },
                Common = new()
                {
                    MacAddress = new byte[6],
                    Ssid = new()
                    {
                        Name = new byte[LdnConst.SsidLengthMax + 1]
                    }
                },
                Ldn = new()
                {
                    NodeCountMax      = (byte)LdnConst.NodeCountMax,
                    SecurityParameter = new byte[16],
                    Nodes             = new NodeInfo[LdnConst.NodeCountMax],
                    AdvertiseData     = new byte[LdnConst.AdvertiseDataSizeMax],
                    Unknown2          = new byte[140]
                }
            };

            for (int i = 0; i < LdnConst.NodeCountMax; i++)
            {
                networkInfo.Ldn.Nodes[i] = new()
                {
                    MacAddress = new byte[6],
                    UserName   = new byte[LdnConst.UserNameBytesMax + 1],
                    Reserved2  = new byte[16]
                };
            }

            return networkInfo;
        }

        public LanDiscovery(Spacemeowx2LdnClient parent, IPAddress ipAddress, IPAddress ipv4mask)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"Initialize LanDiscovery using IP: {ipAddress}");

            _parent            = parent;
            LocalAddr          = ipAddress;
            LocalBroadcastAddr = GetBroadcastAddress(ipAddress, ipv4mask);

            _fakeSsid = new()
            {
                Length = (byte)LdnConst.SsidLengthMax,
                Name   = new byte[32]
            };

            _random.NextBytes(_fakeSsid.Name);
            Array.Resize(ref _fakeSsid.Name, (int)(LdnConst.SsidLengthMax + 1));

            _protocol                   = new LanProtocol(this);
            _protocol.Accept            += OnConnect;
            _protocol.SyncNetwork       += OnSyncNetwork;
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

                    Logger.Debug?.PrintMsg(LogClass.ServiceLdn, $"Received NetworkInfo:\n{JsonHelper.Serialize(info, true)}");
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

                if (_stations.Count > LdnConst.StationCountMax || station.NodeId == -1)
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
                        MacAddress = new byte[6],
                        UserName = new byte[LdnConst.UserNameBytesMax + 1],
                        Reserved2 = new byte[16]
                    };

                    UpdateNodes();
                }
            }
        }

        public bool SetAdvertiseData(byte[] data)
        {
            if (data.Length > (int)LdnConst.AdvertiseDataSizeMax)
            {
                Logger.Error?.PrintMsg(LogClass.ServiceLdn, "AdvertiseData exceeds size limit.");

                return false;
            }

            NetworkInfo.Ldn.AdvertiseData     = data;
            NetworkInfo.Ldn.AdvertiseDataSize = (ushort)data.Length;

            Array.Resize(ref NetworkInfo.Ldn.AdvertiseData, (int)LdnConst.AdvertiseDataSizeMax);

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
                NetworkInfo.Common.MacAddress  = GetFakeMac();
                NetworkInfo.Common.Channel     = COMMON_CHANNEL;
                NetworkInfo.Common.LinkLevel   = COMMON_LINK_LEVEL;
                NetworkInfo.Common.NetworkType = COMMON_NETWORK_TYPE;
                NetworkInfo.Common.Ssid        = _fakeSsid;

                NetworkInfo.Ldn.Nodes = new NodeInfo[LdnConst.NodeCountMax];

                for (int i = 0; i < LdnConst.NodeCountMax; i++)
                {
                    NetworkInfo.Ldn.Nodes[i].NodeId = (byte)i;
                    NetworkInfo.Ldn.Nodes[i].IsConnected = 0;
                }
            }
        }

        protected byte[] GetFakeMac(IPAddress address = null)
        {
            if (address == null)
            {
                address = LocalAddr;
            }

            byte[] ip  = address.GetAddressBytes();

            return new byte[] { 0x02, 0x00, ip[3], ip[2], ip[1], ip[0] };
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
            _udp2?.Stop();

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
                    copy &= filter.NetworkId.SessionId.SequenceEqual(item.Value.NetworkId.SessionId);
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.NetworkType))
                {
                    copy &= filter.NetworkType == (NetworkType)item.Value.Common.NetworkType;
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.Ssid))
                {
                    IEnumerable<byte> gameSsid = item.Value.Common.Ssid.Name.Take(item.Value.Common.Ssid.Length);
                    IEnumerable<byte> scanSsid = filter.Ssid.Name.Take(filter.Ssid.Length);
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
            NodeInfo[] nodes = NetworkInfo.Ldn.Nodes;

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

            node.MacAddress                = GetFakeMac();
            node.IsConnected               = 1;
            node.UserName                  = userConfig.UserName;
            node.LocalCommunicationVersion = localCommunicationVersion;
            node.Ipv4Address               = ipAddress;

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

            NetworkInfo.NetworkId.SessionId = new byte[16];
            NetworkInfo.NetworkId.IntentId  = networkConfig.IntentId;
            _random.NextBytes(NetworkInfo.NetworkId.SessionId);

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

            return _apConnected.WaitOne(FailureTimeout) ? NetworkError.None : NetworkError.ConnectTimeout;
        }

        public void Dispose()
        {
            if (_initialized)
            {
                DisconnectAndStop();
                ResetStations();
                _initialized = false;
            }

            _protocol.Accept            -= OnConnect;
            _protocol.SyncNetwork       -= OnSyncNetwork;
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