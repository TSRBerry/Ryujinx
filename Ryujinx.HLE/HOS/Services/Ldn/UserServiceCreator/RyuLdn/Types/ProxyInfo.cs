﻿using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.RyuLdn.Types
{
    /// <summary>
    /// Information included in all proxied communication.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 0x10, Pack = 1)]
    class ProxyInfo
    {
        public uint   SourceIpV4;
        public ushort SourcePort;

        public uint   DestIpV4;
        public ushort DestPort;

        public ProtocolType Protocol;
    }
}