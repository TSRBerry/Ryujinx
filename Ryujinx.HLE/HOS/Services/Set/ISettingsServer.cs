using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.SystemState;
using System;

using static Ryujinx.HLE.HOS.ErrorCode;

namespace Ryujinx.HLE.HOS.Services.Set
{
    [Service("set")]
    class ISettingsServer : IpcService
    {
        public ISettingsServer(ServiceCtx context) { }

        [Command(0)]
        // GetLanguageCode() -> nn::settings::LanguageCode
        public static long GetLanguageCode(ServiceCtx context)
        {
            context.ResponseData.Write(context.Device.System.State.DesiredLanguageCode);

            return 0;
        }

        [Command(1)]
        // GetAvailableLanguageCodes() -> (u32, buffer<nn::settings::LanguageCode, 0xa>)
        public static long GetAvailableLanguageCodes(ServiceCtx context)
        {
            return GetAvailableLanguagesCodesImpl(
                    context,
                    context.Request.RecvListBuff[0].Position,
                    context.Request.RecvListBuff[0].Size,
                    0xF);
        }

        [Command(2)] // 4.0.0+
        // MakeLanguageCode(nn::settings::Language language_index) -> nn::settings::LanguageCode
        public static long MakeLanguageCode(ServiceCtx context)
        {
            int languageIndex = context.RequestData.ReadInt32();

            if ((uint)languageIndex >= (uint)SystemStateMgr.LanguageCodes.Length)
            {
                return MakeError(ErrorModule.Settings, SettingsError.LanguageOutOfRange);
            }

            context.ResponseData.Write(SystemStateMgr.GetLanguageCode(languageIndex));

            return 0;
        }

        [Command(3)]
        // GetAvailableLanguageCodeCount() -> u32
        public static long GetAvailableLanguageCodeCount(ServiceCtx context)
        {
            context.ResponseData.Write(Math.Min(SystemStateMgr.LanguageCodes.Length, 0xF));

            return 0;
        }

        [Command(5)]
        // GetAvailableLanguageCodes2() -> (u32, buffer<nn::settings::LanguageCode, 6>)
        public static long GetAvailableLanguageCodes2(ServiceCtx context)
        {
            return GetAvailableLanguagesCodesImpl(
                    context,
                    context.Request.ReceiveBuff[0].Position,
                    context.Request.ReceiveBuff[0].Size,
                    SystemStateMgr.LanguageCodes.Length);
        }

        [Command(6)]
        // GetAvailableLanguageCodeCount2() -> u32
        public static long GetAvailableLanguageCodeCount2(ServiceCtx context)
        {
            context.ResponseData.Write(SystemStateMgr.LanguageCodes.Length);

            return 0;
        }

        [Command(8)] // 5.0.0+
        // GetQuestFlag() -> bool
        public static long GetQuestFlag(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            Logger.PrintStub(LogClass.ServiceSet);

            return 0;
        }

        public static long GetAvailableLanguagesCodesImpl(ServiceCtx context, long position, long size, int maxSize)
        {
            int count = (int)(size / 8);

            if (count > maxSize)
            {
                count = maxSize;
            }

            for (int index = 0; index < count; index++)
            {
                context.Memory.WriteInt64(position, SystemStateMgr.GetLanguageCode(index));

                position += 8;
            }

            context.ResponseData.Write(count);

            return 0;
        }
    }
}