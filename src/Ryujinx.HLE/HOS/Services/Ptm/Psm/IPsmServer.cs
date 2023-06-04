﻿using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Ptm.Psm.Types;

namespace Ryujinx.HLE.HOS.Services.Ptm.Psm
{
    [Service("psm")]
    class IPsmServer : IpcService
    {
#pragma warning disable IDE0060 // Remove unused parameter
        public IPsmServer(ServiceCtx context) { }
#pragma warning restore IDE0060

        [CommandCmif(0)]
        // GetBatteryChargePercentage() -> u32
        public static ResultCode GetBatteryChargePercentage(ServiceCtx context)
        {
            const int chargePercentage = 100;

            context.ResponseData.Write(chargePercentage);

            Logger.Stub?.PrintStub(LogClass.ServicePsm, new { chargePercentage });

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // GetChargerType() -> u32
        public static ResultCode GetChargerType(ServiceCtx context)
        {
            const ChargerType chargerType = ChargerType.ChargerOrDock;

            context.ResponseData.Write((int)chargerType);

            Logger.Stub?.PrintStub(LogClass.ServicePsm, new { chargerType });

            return ResultCode.Success;
        }

        [CommandCmif(7)]
        // OpenSession() -> IPsmSession
        public ResultCode OpenSession(ServiceCtx context)
        {
            MakeObject(context, new IPsmSession(context.Device.System));

            return ResultCode.Success;
        }
    }
}