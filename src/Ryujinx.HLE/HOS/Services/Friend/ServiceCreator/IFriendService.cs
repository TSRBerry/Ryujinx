using LibHac.Ns;
using Ryujinx.Common.Extensions;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Account.Acc.Types;
using Ryujinx.HLE.HOS.Services.Friend.ServiceCreator.FriendService.Types;
using Ryujinx.HLE.HOS.Services.Friend.ServiceCreator.Types;
using Ryujinx.Horizon.Common;
using System;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Friend.ServiceCreator
{
    class IFriendService : IpcService
    {
#pragma warning disable IDE0052 // Remove unread private member
        private readonly FriendServicePermissionLevel _permissionLevel;
#pragma warning restore IDE0052
        private KEvent                       _completionEvent;

        public IFriendService(FriendServicePermissionLevel permissionLevel)
        {
            _permissionLevel = permissionLevel;
        }

        [CommandCmif(0)]
        // GetCompletionEvent() -> handle<copy>
        public ResultCode GetCompletionEvent(ServiceCtx context)
        {
            _completionEvent ??= new KEvent(context.Device.System.KernelContext);

            if (context.Process.HandleTable.GenerateHandle(_completionEvent.ReadableEvent, out int completionEventHandle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(completionEventHandle);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // nn::friends::Cancel()
#pragma warning disable IDE0060 // Remove unused parameter
        public static ResultCode Cancel(ServiceCtx context)
        {
            // TODO: Original service sets an internal field to 1 here. Determine usage.
            Logger.Stub?.PrintStub(LogClass.ServiceFriend);

            return ResultCode.Success;
        }
#pragma warning restore IDE0060

        [CommandCmif(10100)]
        // nn::friends::GetFriendListIds(int offset, nn::account::Uid userId, nn::friends::detail::ipc::SizedFriendFilter friendFilter, ulong pidPlaceHolder, pid)
        // -> int outCount, array<nn::account::NetworkServiceAccountId, 0xa>
        public static ResultCode GetFriendListIds(ServiceCtx context)
        {
            int offset = context.RequestData.ReadInt32();

            // Padding
            context.RequestData.ReadInt32();

            UserId       userId = context.RequestData.ReadStruct<UserId>();
            FriendFilter filter = context.RequestData.ReadStruct<FriendFilter>();

            // Pid placeholder
            context.RequestData.ReadInt64();

            if (userId.IsNull)
            {
                return ResultCode.InvalidArgument;
            }

            // There are no friends online, so we return 0 because the nn::account::NetworkServiceAccountId array is empty.
            context.ResponseData.Write(0);

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new
            {
                UserId = userId.ToString(),
                offset,
                filter.PresenceStatus,
                filter.IsFavoriteOnly,
                filter.IsSameAppPresenceOnly,
                filter.IsSameAppPlayedOnly,
                filter.IsArbitraryAppPlayedOnly,
                filter.PresenceGroupId
            });

            return ResultCode.Success;
        }

        [CommandCmif(10101)]
        // nn::friends::GetFriendList(int offset, nn::account::Uid userId, nn::friends::detail::ipc::SizedFriendFilter friendFilter, ulong pidPlaceHolder, pid)
        // -> int outCount, array<nn::friends::detail::FriendImpl, 0x6>
        public static ResultCode GetFriendList(ServiceCtx context)
        {
            int offset = context.RequestData.ReadInt32();

            // Padding
            context.RequestData.ReadInt32();

            UserId       userId = context.RequestData.ReadStruct<UserId>();
            FriendFilter filter = context.RequestData.ReadStruct<FriendFilter>();

            // Pid placeholder
            context.RequestData.ReadInt64();

            if (userId.IsNull)
            {
                return ResultCode.InvalidArgument;
            }

            // There are no friends online, so we return 0 because the nn::account::NetworkServiceAccountId array is empty.
            context.ResponseData.Write(0);

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new
            {
                UserId = userId.ToString(),
                offset,
                filter.PresenceStatus,
                filter.IsFavoriteOnly,
                filter.IsSameAppPresenceOnly,
                filter.IsSameAppPlayedOnly,
                filter.IsArbitraryAppPlayedOnly,
                filter.PresenceGroupId
            });

            return ResultCode.Success;
        }

        [CommandCmif(10120)] // 10.0.0+
        // nn::friends::IsFriendListCacheAvailable(nn::account::Uid userId) -> bool
        public static ResultCode IsFriendListCacheAvailable(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (userId.IsNull)
            {
                return ResultCode.InvalidArgument;
            }

            // TODO: Service mount the friends:/ system savedata and try to load friend.cache file, returns true if exists, false otherwise.
            // NOTE: If no cache is available, guest then calls nn::friends::EnsureFriendListAvailable, we can avoid that by faking the cache check.
            context.ResponseData.Write(true);

            // TODO: Since we don't support friend features, it's fine to stub it for now.
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { UserId = userId.ToString() });

            return ResultCode.Success;
        }

        [CommandCmif(10121)] // 10.0.0+
        // nn::friends::EnsureFriendListAvailable(nn::account::Uid userId)
        public static ResultCode EnsureFriendListAvailable(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (userId.IsNull)
            {
                return ResultCode.InvalidArgument;
            }

            // TODO: Service mount the friends:/ system savedata and create a friend.cache file for the given user id.
            //       Since we don't support friend features, it's fine to stub it for now.
            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { UserId = userId.ToString() });

            return ResultCode.Success;
        }

        [CommandCmif(10400)]
        // nn::friends::GetBlockedUserListIds(int offset, nn::account::Uid userId) -> (u32, buffer<nn::account::NetworkServiceAccountId, 0xa>)
        public static ResultCode GetBlockedUserListIds(ServiceCtx context)
        {
            int offset = context.RequestData.ReadInt32();

            // Padding
            context.RequestData.ReadInt32();

            UserId userId = context.RequestData.ReadStruct<UserId>();

            // There are no friends blocked, so we return 0 because the nn::account::NetworkServiceAccountId array is empty.
            context.ResponseData.Write(0);

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { offset, UserId = userId.ToString() });

            return ResultCode.Success;
        }

        [CommandCmif(10600)]
        // nn::friends::DeclareOpenOnlinePlaySession(nn::account::Uid userId)
        public static ResultCode DeclareOpenOnlinePlaySession(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (userId.IsNull)
            {
                return ResultCode.InvalidArgument;
            }

            context.Device.System.AccountManager.OpenUserOnlinePlay(userId);

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { UserId = userId.ToString() });

            return ResultCode.Success;
        }

        [CommandCmif(10601)]
        // nn::friends::DeclareCloseOnlinePlaySession(nn::account::Uid userId)
        public static ResultCode DeclareCloseOnlinePlaySession(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (userId.IsNull)
            {
                return ResultCode.InvalidArgument;
            }

            context.Device.System.AccountManager.CloseUserOnlinePlay(userId);

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { UserId = userId.ToString() });

            return ResultCode.Success;
        }

        [CommandCmif(10610)]
        // nn::friends::UpdateUserPresence(nn::account::Uid, u64, pid, buffer<nn::friends::detail::UserPresenceImpl, 0x19>)
        public static ResultCode UpdateUserPresence(ServiceCtx context)
        {
            UserId uuid = context.RequestData.ReadStruct<UserId>();

            // Pid placeholder
            context.RequestData.ReadInt64();

            ulong position = context.Request.PtrBuff[0].Position;
            ulong size     = context.Request.PtrBuff[0].Size;

            ReadOnlySpan<UserPresence> userPresenceInputArray = MemoryMarshal.Cast<byte, UserPresence>(context.Memory.GetSpan(position, (int)size));

            if (uuid.IsNull)
            {
                return ResultCode.InvalidArgument;
            }

            Logger.Stub?.PrintStub(LogClass.ServiceFriend, new { UserId = uuid.ToString(), userPresenceInputArray = userPresenceInputArray.ToArray() });

            return ResultCode.Success;
        }

        [CommandCmif(10700)]
        // nn::friends::GetPlayHistoryRegistrationKey(b8 unknown, nn::account::Uid) -> buffer<nn::friends::PlayHistoryRegistrationKey, 0x1a>
        public static ResultCode GetPlayHistoryRegistrationKey(ServiceCtx context)
        {
            bool   unknownBool = context.RequestData.ReadBoolean();
            UserId userId      = context.RequestData.ReadStruct<UserId>();

            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize(0x40UL);

            ulong bufferPosition = context.Request.RecvListBuff[0].Position;

            if (userId.IsNull)
            {
                return ResultCode.InvalidArgument;
            }

            // NOTE: Calls nn::friends::detail::service::core::PlayHistoryManager::GetInstance and stores the instance.

            byte[] randomBytes = new byte[8];

            Random.Shared.NextBytes(randomBytes);

            // NOTE: Calls nn::friends::detail::service::core::UuidManager::GetInstance and stores the instance.
            //       Then call nn::friends::detail::service::core::AccountStorageManager::GetInstance and store the instance.
            //       Then it checks if an Uuid is already stored for the UserId, if not it generates a random Uuid.
            //       And store it in the savedata 8000000000000080 in the friends:/uid.bin file.

            Array16<byte> randomGuid = new();

            Guid.NewGuid().ToByteArray().AsSpan().CopyTo(randomGuid.AsSpan());

            PlayHistoryRegistrationKey playHistoryRegistrationKey = new()
            {
                Type        = 0x101,
                KeyIndex    = (byte)(randomBytes[0] & 7),
                UserIdBool  = 0, // TODO: Find it.
                UnknownBool = (byte)(unknownBool ? 1 : 0), // TODO: Find it.
                Reserved    = new Array11<byte>(),
                Uuid        = randomGuid
            };

            ReadOnlySpan<byte> playHistoryRegistrationKeyBuffer = SpanHelpers.AsByteSpan(ref playHistoryRegistrationKey);

            /*

            NOTE: The service uses the KeyIndex to get a random key from a keys buffer (since the key index is stored in the returned buffer).
                  We currently don't support play history and online services so we can use a blank key for now.
                  Code for reference:

            byte[] hmacKey = new byte[0x20];

            HMACSHA256 hmacSha256 = new HMACSHA256(hmacKey);
            byte[]     hmacHash   = hmacSha256.ComputeHash(playHistoryRegistrationKeyBuffer);

            */

            context.Memory.Write(bufferPosition, playHistoryRegistrationKeyBuffer);
            context.Memory.Write(bufferPosition + 0x20, new byte[0x20]); // HmacHash

            return ResultCode.Success;
        }

        [CommandCmif(10702)]
        // nn::friends::AddPlayHistory(nn::account::Uid, u64, pid, buffer<nn::friends::PlayHistoryRegistrationKey, 0x19>, buffer<nn::friends::InAppScreenName, 0x19>, buffer<nn::friends::InAppScreenName, 0x19>)
        public static ResultCode AddPlayHistory(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            // Pid placeholder
            context.RequestData.ReadInt64();
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            ulong pid = context.Request.HandleDesc.PId;

            ulong playHistoryRegistrationKeyPosition = context.Request.PtrBuff[0].Position;
            ulong PlayHistoryRegistrationKeySize     = context.Request.PtrBuff[0].Size;

            ulong inAppScreenName1Position = context.Request.PtrBuff[1].Position;
#pragma warning restore IDE0059
            ulong inAppScreenName1Size     = context.Request.PtrBuff[1].Size;

#pragma warning disable IDE0059 // Remove unnecessary value assignment
            ulong inAppScreenName2Position = context.Request.PtrBuff[2].Position;
#pragma warning restore IDE0059
            ulong inAppScreenName2Size     = context.Request.PtrBuff[2].Size;

            if (userId.IsNull || inAppScreenName1Size > 0x48 || inAppScreenName2Size > 0x48)
            {
                return ResultCode.InvalidArgument;
            }

            // TODO: Call nn::arp::GetApplicationControlProperty here when implemented.
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            ApplicationControlProperty controlProperty = context.Device.Processes.ActiveApplication.ApplicationControlProperties;
#pragma warning restore IDE0059

            /*

            NOTE: The service calls nn::friends::detail::service::core::PlayHistoryManager to store informations using the registration key computed in GetPlayHistoryRegistrationKey.
                  Then calls nn::friends::detail::service::core::FriendListManager to update informations on the friend list.
                  We currently don't support play history and online services so it's fine to do nothing.

            */

            Logger.Stub?.PrintStub(LogClass.ServiceFriend);

            return ResultCode.Success;
        }
    }
}