﻿using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;
using System.Buffers;

namespace Ryujinx.HLE.HOS.Services.Audio.AudioRenderer
{
    class AudioRendererServer : DisposableIpcService
    {
        private readonly IAudioRenderer _impl;

        public AudioRendererServer(IAudioRenderer impl)
        {
            _impl = impl;
        }

        [CommandHipc(0)]
        // GetSampleRate() -> u32
        public ResultCode GetSampleRate(ServiceCtx context)
        {
            context.ResponseData.Write(_impl.GetSampleRate());

            return ResultCode.Success;
        }

        [CommandHipc(1)]
        // GetSampleCount() -> u32
        public ResultCode GetSampleCount(ServiceCtx context)
        {
            context.ResponseData.Write(_impl.GetSampleCount());

            return ResultCode.Success;
        }

        [CommandHipc(2)]
        // GetMixBufferCount() -> u32
        public ResultCode GetMixBufferCount(ServiceCtx context)
        {
            context.ResponseData.Write(_impl.GetMixBufferCount());

            return ResultCode.Success;
        }

        [CommandHipc(3)]
        // GetState() -> u32
        public ResultCode GetState(ServiceCtx context)
        {
            context.ResponseData.Write(_impl.GetState());

            return ResultCode.Success;
        }

        [CommandHipc(4)]
        // RequestUpdate(buffer<nn::audio::detail::AudioRendererUpdateDataHeader, 5> input)
        // -> (buffer<nn::audio::detail::AudioRendererUpdateDataHeader, 6> output, buffer<nn::audio::detail::AudioRendererUpdateDataHeader, 6> performanceOutput)
        public ResultCode RequestUpdate(ServiceCtx context)
        {
            ulong inputPosition = context.Request.SendBuff[0].Position;
            ulong inputSize = context.Request.SendBuff[0].Size;

            ulong outputPosition = context.Request.ReceiveBuff[0].Position;
            ulong outputSize = context.Request.ReceiveBuff[0].Size;

            ulong performanceOutputPosition = context.Request.ReceiveBuff[1].Position;
            ulong performanceOutputSize = context.Request.ReceiveBuff[1].Size;

            ReadOnlyMemory<byte> input = context.Memory.GetSpan(inputPosition, (int)inputSize).ToArray();

            Memory<byte> output = new byte[outputSize];
            Memory<byte> performanceOutput = new byte[performanceOutputSize];

            using MemoryHandle outputHandle = output.Pin();
            using MemoryHandle performanceOutputHandle = performanceOutput.Pin();

            ResultCode result = _impl.RequestUpdate(output, performanceOutput, input);

            if (result == ResultCode.Success)
            {
                context.Memory.Write(outputPosition, output.Span);
                context.Memory.Write(performanceOutputPosition, performanceOutput.Span);
            }
            else
            {
                Logger.Error?.Print(LogClass.ServiceAudio, $"Error while processing renderer update: 0x{(int)result:X}");
            }

            return result;
        }

        [CommandHipc(5)]
        // Start()
#pragma warning disable IDE0060
        public ResultCode Start(ServiceCtx context)
        {
            return _impl.Start();
        }
#pragma warning restore IDE0060

        [CommandHipc(6)]
        // Stop()
#pragma warning disable IDE0060
        public ResultCode Stop(ServiceCtx context)
        {
            return _impl.Stop();
        }
#pragma warning restore IDE0060

        [CommandHipc(7)]
        // QuerySystemEvent() -> handle<copy, event>
        public ResultCode QuerySystemEvent(ServiceCtx context)
        {
            ResultCode result = _impl.QuerySystemEvent(out KEvent systemEvent);

            if (result == ResultCode.Success)
            {
                if (context.Process.HandleTable.GenerateHandle(systemEvent.ReadableEvent, out int handle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }

                context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);
            }

            return result;
        }

        [CommandHipc(8)]
        // SetAudioRendererRenderingTimeLimit(u32 limit)
        public ResultCode SetAudioRendererRenderingTimeLimit(ServiceCtx context)
        {
            uint limit = context.RequestData.ReadUInt32();

            _impl.SetRenderingTimeLimit(limit);

            return ResultCode.Success;
        }

        [CommandHipc(9)]
        // GetAudioRendererRenderingTimeLimit() -> u32 limit
        public ResultCode GetAudioRendererRenderingTimeLimit(ServiceCtx context)
        {
            uint limit = _impl.GetRenderingTimeLimit();

            context.ResponseData.Write(limit);

            return ResultCode.Success;
        }

        [CommandHipc(10)] // 3.0.0+
        //  RequestUpdateAuto(buffer<nn::audio::detail::AudioRendererUpdateDataHeader, 0x21> input)
        // -> (buffer<nn::audio::detail::AudioRendererUpdateDataHeader, 0x22> output, buffer<nn::audio::detail::AudioRendererUpdateDataHeader, 0x22> performanceOutput)
        public ResultCode RequestUpdateAuto(ServiceCtx context)
        {
            (ulong inputPosition, ulong inputSize) = context.Request.GetBufferType0x21();
            (ulong outputPosition, ulong outputSize) = context.Request.GetBufferType0x22(0);
            (ulong performanceOutputPosition, ulong performanceOutputSize) = context.Request.GetBufferType0x22(1);

            ReadOnlyMemory<byte> input = context.Memory.GetSpan(inputPosition, (int)inputSize).ToArray();

            Memory<byte> output = new byte[outputSize];
            Memory<byte> performanceOutput = new byte[performanceOutputSize];

            using MemoryHandle outputHandle = output.Pin();
            using MemoryHandle performanceOutputHandle = performanceOutput.Pin();

            ResultCode result = _impl.RequestUpdate(output, performanceOutput, input);

            if (result == ResultCode.Success)
            {
                context.Memory.Write(outputPosition, output.Span);
                context.Memory.Write(performanceOutputPosition, performanceOutput.Span);
            }

            return result;
        }

        [CommandHipc(11)] // 3.0.0+
        // ExecuteAudioRendererRendering()
#pragma warning disable IDE0060
        public ResultCode ExecuteAudioRendererRendering(ServiceCtx context)
        {
            return _impl.ExecuteAudioRendererRendering();
        }
#pragma warning restore IDE0060

        [CommandHipc(12)] // 15.0.0+
        // SetVoiceDropParameter(f32 voiceDropParameter)
        public ResultCode SetVoiceDropParameter(ServiceCtx context)
        {
            float voiceDropParameter = context.RequestData.ReadSingle();

            _impl.SetVoiceDropParameter(voiceDropParameter);

            return ResultCode.Success;
        }

        [CommandHipc(13)] // 15.0.0+
        // GetVoiceDropParameter() -> f32 voiceDropParameter
        public ResultCode GetVoiceDropParameter(ServiceCtx context)
        {
            float voiceDropParameter = _impl.GetVoiceDropParameter();

            context.ResponseData.Write(voiceDropParameter);

            return ResultCode.Success;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                _impl.Dispose();
            }
        }
    }
}
