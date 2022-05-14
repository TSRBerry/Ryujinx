﻿using Ryujinx.Cpu;
using Ryujinx.Memory;
using System;

namespace Ryujinx.HLE.HOS.Kernel.Process
{
    class ProcessContext : IProcessContext
    {
        public IVirtualMemoryManager AddressSpace { get; }
        public ulong ReservedSize => 0UL;

        public ProcessContext(IVirtualMemoryManager asManager)
        {
            AddressSpace = asManager;
        }

        public IExecutionContext CreateExecutionContext(ExceptionCallbacks exceptionCallbacks)
        {
            return new ProcessExecutionContext();
        }

        public void Execute(IExecutionContext context, ulong codeAddress)
        {
            throw new NotSupportedException();
        }

        public void InvalidateCacheRegion(ulong address, ulong size)
        {
        }

        public void PatchCodeForNce(ulong textAddress, ulong textSize, ulong patchRegionAddress, ulong patchRegionSize)
        {
        }

        public void Dispose()
        {
        }
    }
}
