using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Nifm.StaticService
{
    class IRequest : IpcService
    {
        private enum RequestState
        {
            Error = 1,
            OnHold = 2,
            Available = 3
        }

        private readonly KEvent _event0;
        private readonly KEvent _event1;

        private int _event0Handle;
        private int _event1Handle;

        private readonly uint _version;

        public IRequest(Horizon system, uint version)
        {
            _event0 = new KEvent(system.KernelContext);
            _event1 = new KEvent(system.KernelContext);

            _version = version;
        }

        [CommandCmif(0)]
        // GetRequestState() -> u32
        public static ResultCode GetRequestState(ServiceCtx context)
        {
            RequestState requestState = context.Device.Configuration.EnableInternetAccess
                ? RequestState.Available
                : RequestState.Error;

            context.ResponseData.Write((int)requestState);

            Logger.Stub?.PrintStub(LogClass.ServiceNifm);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // GetResult()
        public ResultCode GetResult(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceNifm);

            return GetResultImpl();
        }

        private static ResultCode GetResultImpl()
        {
            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // GetSystemEventReadableHandles() -> (handle<copy>, handle<copy>)
        public ResultCode GetSystemEventReadableHandles(ServiceCtx context)
        {
            if (_event0Handle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_event0.ReadableEvent, out _event0Handle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            if (_event1Handle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_event1.ReadableEvent, out _event1Handle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_event0Handle, _event1Handle);

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // Cancel()
        public static ResultCode Cancel(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceNifm);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // Submit()
        public static ResultCode Submit(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceNifm);

            return ResultCode.Success;
        }

        [CommandCmif(11)]
        // SetConnectionConfirmationOption(i8)
        public static ResultCode SetConnectionConfirmationOption(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceNifm);

            return ResultCode.Success;
        }

        [CommandCmif(21)]
        // GetAppletInfo(u32) -> (u32, u32, u32, buffer<bytes, 6>)
        public ResultCode GetAppletInfo(ServiceCtx context)
        {
#pragma warning disable IDE0059
            uint themeColor = context.RequestData.ReadUInt32();
#pragma warning restore IDE0059

            Logger.Stub?.PrintStub(LogClass.ServiceNifm);

            ResultCode result = GetResultImpl();

            if (result == ResultCode.Success || (ResultCode)((int)result & 0x3fffff) == ResultCode.Unknown112)
            {
                return ResultCode.Unknown180;
            }

            // Returns appletId, libraryAppletMode, outSize and a buffer.
            // Returned applet ids- (0x19, 0xf, 0xe)
            // libraryAppletMode seems to be 0 for all applets supported.

            // TODO: check order
            context.ResponseData.Write(0xe); // Use error applet as default for now
            context.ResponseData.Write(0); // libraryAppletMode
            context.ResponseData.Write(0); // outSize

            return ResultCode.Success;
        }
    }
}
