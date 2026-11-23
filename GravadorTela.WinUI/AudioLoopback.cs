using System.Runtime.InteropServices;

namespace GravadorTela;

/// <summary>
/// Captura el audio que suena en el dispositivo de salida por defecto
/// (WASAPI loopback) y entrega bloques PCM. Sin dependencias externas:
/// usa las APIs COM nativas de Windows Core Audio.
/// </summary>
public sealed class AudioLoopback : IDisposable
{
    private IAudioClient _client = null!;
    private IAudioCaptureClient _capture = null!;
    private Thread? _thread;
    private volatile bool _running;

    public int SampleRate { get; private set; }
    public int Channels { get; private set; }
    public int BitsPerSample { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Se invoca con cada bloque PCM capturado (formato float o PCM segun el mezclador).</summary>
    public event Action<byte[]>? DataAvailable;

    private const int REFTIMES_PER_SEC = 10_000_000;
    private const uint AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;

    public void Start()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var device);

        var iidAudioClient = typeof(IAudioClient).GUID;
        device.Activate(ref iidAudioClient, 0, IntPtr.Zero, out var clientObj);
        _client = (IAudioClient)clientObj;

        _client.GetMixFormat(out var pFormat);
        var wf = Marshal.PtrToStructure<WAVEFORMATEX>(pFormat);
        SampleRate = (int)wf.nSamplesPerSec;
        Channels = wf.nChannels;
        BitsPerSample = wf.wBitsPerSample;

        _client.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK,
            REFTIMES_PER_SEC, 0, pFormat, IntPtr.Zero);

        var iidCapture = typeof(IAudioCaptureClient).GUID;
        _client.GetService(ref iidCapture, out var captureObj);
        _capture = (IAudioCaptureClient)captureObj;

        Marshal.FreeCoTaskMem(pFormat);

        _client.Start();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "AudioLoopback" };
        _thread.Start();
    }

    private void Loop()
    {
        int frameBytes = Channels * (BitsPerSample / 8);
        try
        {
            while (_running)
            {
                _capture.GetNextPacketSize(out uint packetFrames);
                if (packetFrames == 0) { Thread.Sleep(5); continue; }

                while (packetFrames != 0)
                {
                    _capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                    int bytes = (int)frames * frameBytes;
                    var buf = new byte[bytes];

                    // flag SILENT (0x2): bloque en silencio -> emitimos ceros para mantener sincronia.
                    if ((flags & 0x2) == 0 && data != IntPtr.Zero)
                        Marshal.Copy(data, buf, 0, bytes);

                    _capture.ReleaseBuffer(frames);
                    DataAvailable?.Invoke(buf);

                    _capture.GetNextPacketSize(out packetFrames);
                }
            }
        }
        catch (Exception ex)
        {
            LastError = "audio: " + ex.Message;
        }
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(2000);
        try { _client?.Stop(); } catch { }
        if (_capture != null) { try { Marshal.ReleaseComObject(_capture); } catch { } }
        if (_client != null) { try { Marshal.ReleaseComObject(_client); } catch { } }
    }

    public void Dispose() => Stop();
}

// ----------------- Interop Core Audio (WASAPI) -----------------

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator { }

internal enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    // resto del vtable no usado
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    // resto del vtable no usado
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(uint shareMode, uint streamFlags, long hnsBufferDuration,
        long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint numBufferFrames);
    [PreserveSig] int GetStreamLatency(out long phnsLatency);
    [PreserveSig] int GetCurrentPadding(out uint numPaddingFrames);
    [PreserveSig] int IsFormatSupported(uint shareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
    [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead,
        out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
    [PreserveSig] int ReleaseBuffer(uint numFramesRead);
    [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WAVEFORMATEX
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}
