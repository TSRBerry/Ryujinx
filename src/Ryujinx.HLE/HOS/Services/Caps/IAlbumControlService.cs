namespace Ryujinx.HLE.HOS.Services.Caps
{
    [Service("caps:c")]
    class IAlbumControlService : IpcService
    {
#pragma warning disable IDE0060
        public IAlbumControlService(ServiceCtx context) { }
#pragma warning restore IDE0060

        [CommandHipc(33)] // 7.0.0+
        // SetShimLibraryVersion(pid, u64, nn::applet::AppletResourceUserId)
        public static ResultCode SetShimLibraryVersion(ServiceCtx context)
        {
            return context.Device.System.CaptureManager.SetShimLibraryVersion(context);
        }
    }
}
