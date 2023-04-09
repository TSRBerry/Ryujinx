using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Services.Sdb.Pl.Types;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Sdb.Pl
{
    [Service("pl:u")]
    [Service("pl:s")] // 9.0.0+
    class ISharedFontManager : IpcService
    {
        private int _fontSharedMemHandle;

#pragma warning disable IDE0060
        public ISharedFontManager(ServiceCtx context) { }
#pragma warning restore IDE0060

        [CommandHipc(0)]
        // RequestLoad(u32)
        public static ResultCode RequestLoad(ServiceCtx context)
        {
#pragma warning disable IDE0059
            SharedFontType fontType = (SharedFontType)context.RequestData.ReadInt32();
#pragma warning restore IDE0059

            // We don't need to do anything here because we do lazy initialization
            // on SharedFontManager (the font is loaded when necessary).
            return ResultCode.Success;
        }

        [CommandHipc(1)]
        // GetLoadState(u32) -> u32
        public static ResultCode GetLoadState(ServiceCtx context)
        {
#pragma warning disable IDE0059
            SharedFontType fontType = (SharedFontType)context.RequestData.ReadInt32();
#pragma warning restore IDE0059

            // 1 (true) indicates that the font is already loaded.
            // All fonts are already loaded.
            context.ResponseData.Write(1);

            return ResultCode.Success;
        }

        [CommandHipc(2)]
        // GetFontSize(u32) -> u32
        public static ResultCode GetFontSize(ServiceCtx context)
        {
            SharedFontType fontType = (SharedFontType)context.RequestData.ReadInt32();

            context.ResponseData.Write(context.Device.System.SharedFontManager.GetFontSize(fontType));

            return ResultCode.Success;
        }

        [CommandHipc(3)]
        // GetSharedMemoryAddressOffset(u32) -> u32
        public static ResultCode GetSharedMemoryAddressOffset(ServiceCtx context)
        {
            SharedFontType fontType = (SharedFontType)context.RequestData.ReadInt32();

            context.ResponseData.Write(context.Device.System.SharedFontManager.GetSharedMemoryAddressOffset(fontType));

            return ResultCode.Success;
        }

        [CommandHipc(4)]
        // GetSharedMemoryNativeHandle() -> handle<copy>
        public ResultCode GetSharedMemoryNativeHandle(ServiceCtx context)
        {
            context.Device.System.SharedFontManager.EnsureInitialized(context.Device.System.ContentManager);

            if (_fontSharedMemHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(context.Device.System.FontSharedMem, out _fontSharedMemHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_fontSharedMemHandle);

            return ResultCode.Success;
        }

        [CommandHipc(5)]
        // GetSharedFontInOrderOfPriority(bytes<8, 1>) -> (u8, u32, buffer<unknown, 6>, buffer<unknown, 6>, buffer<unknown, 6>)
        public ResultCode GetSharedFontInOrderOfPriority(ServiceCtx context)
        {
#pragma warning disable IDE0059
            long languageCode = context.RequestData.ReadInt64();
#pragma warning restore IDE0059
            int  loadedCount  = 0;

            for (SharedFontType type = 0; type < SharedFontType.Count; type++)
            {
                uint offset = (uint)type * 4;

                if (!AddFontToOrderOfPriorityList(context, type, offset))
                {
                    break;
                }

                loadedCount++;
            }

            context.ResponseData.Write(loadedCount);
            context.ResponseData.Write((int)SharedFontType.Count);

            return ResultCode.Success;
        }

        [CommandHipc(6)] // 4.0.0+
        // GetSharedFontInOrderOfPriorityForSystem(bytes<8, 1>) -> (u8, u32, buffer<unknown, 6>, buffer<unknown, 6>, buffer<unknown, 6>)
        public ResultCode GetSharedFontInOrderOfPriorityForSystem(ServiceCtx context)
        {
            // TODO: Check the differencies with GetSharedFontInOrderOfPriority.

            return GetSharedFontInOrderOfPriority(context);
        }

        private static bool AddFontToOrderOfPriorityList(ServiceCtx context, SharedFontType fontType, uint offset)
        {
            ulong typesPosition = context.Request.ReceiveBuff[0].Position;
            ulong typesSize     = context.Request.ReceiveBuff[0].Size;

            ulong offsetsPosition = context.Request.ReceiveBuff[1].Position;
            ulong offsetsSize     = context.Request.ReceiveBuff[1].Size;

            ulong fontSizeBufferPosition = context.Request.ReceiveBuff[2].Position;
            ulong fontSizeBufferSize     = context.Request.ReceiveBuff[2].Size;

            if (offset + 4 > (uint)typesSize   ||
                offset + 4 > (uint)offsetsSize ||
                offset + 4 > (uint)fontSizeBufferSize)
            {
                return false;
            }

            context.Memory.Write(typesPosition + offset, (int)fontType);
            context.Memory.Write(offsetsPosition + offset, context.Device.System.SharedFontManager.GetSharedMemoryAddressOffset(fontType));
            context.Memory.Write(fontSizeBufferPosition + offset, context.Device.System.SharedFontManager.GetFontSize(fontType));

            return true;
        }
    }
}
