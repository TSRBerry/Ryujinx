﻿using Ryujinx.Common;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.LibraryAppletProxy
{
    class IProcessWindingController : IpcService
    {
        public IProcessWindingController() { }

        [CommandHipc(0)]
        // GetLaunchReason() -> nn::am::service::AppletProcessLaunchReason
        public static ResultCode GetLaunchReason(ServiceCtx context)
        {
            // NOTE: Flag is set by using an internal field.
            AppletProcessLaunchReason appletProcessLaunchReason = new()
            {
                Flag = 0
            };

            context.ResponseData.WriteStruct(appletProcessLaunchReason);

            return ResultCode.Success;
        }
    }
}