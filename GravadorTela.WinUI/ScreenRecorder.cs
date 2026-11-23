using System.Diagnostics;
using System.IO;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace GravadorTela;

/// <summary>
/// Graba el monitor principal con DXGI Desktop Duplication (captura en GPU) y
/// canaliza los frames crudos a FFmpeg, que codifica con la GPU (AMD AMF /
/// NVIDIA NVENC / Intel QSV) si existe, con fallback automatico a CPU (libx264).
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGIOutputDuplication _duplication = null!;
    private ID3D11Texture2D _staging = null!;

    private Process _ffmpeg = null!;
    private Stream _ffmpegIn = null!;
    private byte[] _frameBuffer = Array.Empty<byte>();

    private AudioLoopback? _audio;
    private System.IO.Pipes.NamedPipeServerStream? _audioPipe;
    private string _audioPipeName = "";
    private bool _captureAudio;

    private int _width;
    private int _height;
    private int _fps;
    private bool _haveFrame;

    private Thread? _thread;
    private volatile bool _running;
    private long _frameIndex;

    public bool UsingHardwareEncoder { get; private set; }
    public string EncoderName { get; private set; } = "desconocido";
    public long FramesWritten => _frameIndex;
    public string? LastError { get; private set; }

    private const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);

    public void Start(string outputPath, int fps = 30, int monitorIndex = 0, bool captureAudio = false)
    {
        _fps = fps;
        _captureAudio = captureAudio;
        CreateCapture(monitorIndex);
        if (_captureAudio) SetupAudio();
        StartFfmpeg(outputPath);
        if (_captureAudio) StartAudioFeed();

        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Captura" };
        _thread.Start();
    }

    // Formato de audio detectado del mezclador WASAPI (para pasarlo a FFmpeg).
    private int _audioRate, _audioChannels, _audioBits;
    private bool _audioIsFloat;

    private void SetupAudio()
    {
        try
        {
            _audio = new AudioLoopback();
            _audio.Start();
            _audioRate = _audio.SampleRate;
            _audioChannels = _audio.Channels;
            _audioBits = _audio.BitsPerSample;
            _audioIsFloat = _audioBits == 32; // el mix de WASAPI suele ser float32
            _audioPipeName = "gravador_audio_" + Guid.NewGuid().ToString("N");
            _audioPipe = new System.IO.Pipes.NamedPipeServerStream(
                _audioPipeName, System.IO.Pipes.PipeDirection.Out, 1,
                System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        }
        catch (Exception ex)
        {
            LastError = "audio setup: " + ex.Message;
            _captureAudio = false;
            _audio?.Dispose();
            _audio = null;
        }
    }

    private void StartAudioFeed()
    {
        if (_audio == null || _audioPipe == null) return;

        // FFmpeg (como cliente) se conectara al pipe; esperamos esa conexion en segundo plano.
        var waitConnect = _audioPipe.WaitForConnectionAsync();

        _audio.DataAvailable += block =>
        {
            try
            {
                if (_audioPipe is { IsConnected: true })
                    _audioPipe.Write(block, 0, block.Length);
            }
            catch { /* FFmpeg cerro el pipe al finalizar */ }
        };
    }

    private void CreateCapture(int monitorIndex)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();

        D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out _device!,
            out _context!).CheckError();

        GpuName = adapter.Description1.Description;

        if (adapter.EnumOutputs((uint)monitorIndex, out var output).Failure)
            adapter.EnumOutputs(0, out output).CheckError(); // fallback al primero
        using var _outputRef = output;
        using var output1 = output.QueryInterface<IDXGIOutput1>();

        var od = output.Description;
        _width = od.DesktopCoordinates.Right - od.DesktopCoordinates.Left;
        _height = od.DesktopCoordinates.Bottom - od.DesktopCoordinates.Top;

        _duplication = output1.DuplicateOutput(_device);

        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        });

        _frameBuffer = new byte[_width * 4 * _height];
    }

    private string GpuName { get; set; } = "";

    private void StartFfmpeg(string outputPath)
    {
        var ffmpeg = FfmpegLocator.FindFfmpeg()
            ?? throw new InvalidOperationException(
                "No se encontro ffmpeg.exe. Instalalo con: winget install Gyan.FFmpeg");

        var (codec, label) = FfmpegLocator.PickEncoder(ffmpeg, GpuName);
        EncoderName = label;
        UsingHardwareEncoder = codec != "libx264";

        // Parametros de calidad segun el encoder.
        string encParams = codec switch
        {
            "h264_amf" => "-usage transcoding -quality quality -rc cqp -qp_i 22 -qp_p 22",
            "h264_nvenc" => "-preset p5 -rc vbr -cq 22",
            "h264_qsv" => "-global_quality 22",
            _ => "-preset veryfast -crf 22"
        };

        // Entrada: rawvideo BGRA desde stdin. Salida: MP4 H.264 yuv420p.
        string audioInput = "";
        string audioMap = "";
        string audioCodec = "";
        if (_captureAudio && _audioPipe != null)
        {
            string sampleFmt = _audioIsFloat ? "f32le" : (_audioBits == 16 ? "s16le" : "s32le");
            audioInput =
                $"-f {sampleFmt} -ar {_audioRate} -ac {_audioChannels} -i \\\\.\\pipe\\{_audioPipeName} ";
            audioMap = "-map 0:v:0 -map 1:a:0 ";
            audioCodec = "-c:a aac -b:a 192k ";
        }

        string args =
            $"-y -hide_banner -loglevel error " +
            $"-f rawvideo -pixel_format bgra -video_size {_width}x{_height} -framerate {_fps} -i - " +
            audioInput +
            audioMap +
            $"-c:v {codec} {encParams} -pix_fmt yuv420p {audioCodec}-movflags +faststart " +
            $"\"{outputPath}\"";

        var psi = new ProcessStartInfo(ffmpeg, args)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _ffmpeg = Process.Start(psi)!;
        _ffmpegIn = _ffmpeg.StandardInput.BaseStream;

        // Capturamos stderr por si FFmpeg aborta (resolucion no soportada, etc.).
        _ffmpeg.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LastError = "ffmpeg: " + e.Data;
        };
        _ffmpeg.BeginErrorReadLine();
    }

    private void CaptureLoop()
    {
        double msPerFrame = 1000.0 / _fps;
        var sw = Stopwatch.StartNew();
        double nextFrameMs = 0;

        try
        {
            PrimeFirstFrame();

            while (_running)
            {
                var res = _duplication.AcquireNextFrame(50, out _, out var resource);
                if (res.Success)
                {
                    using (resource)
                    using (var tex = resource.QueryInterface<ID3D11Texture2D>())
                    {
                        _context.CopyResource(_staging, tex);
                        _haveFrame = true;
                    }
                    _duplication.ReleaseFrame();
                }
                else if (res.Code != DXGI_ERROR_WAIT_TIMEOUT)
                {
                    LastError = $"AcquireNextFrame 0x{res.Code:X8}";
                    break;
                }

                double now = sw.Elapsed.TotalMilliseconds;
                if (_haveFrame && now >= nextFrameMs)
                {
                    PushFrame();
                    nextFrameMs += msPerFrame;
                    if (nextFrameMs < now) nextFrameMs = now + msPerFrame;
                }
            }
        }
        catch (Exception ex)
        {
            if (LastError == null) LastError = "loop: " + ex.Message;
        }
    }

    private void PrimeFirstFrame()
    {
        for (int i = 0; i < 40 && _running && !_haveFrame; i++)
        {
            var res = _duplication.AcquireNextFrame(100, out _, out var resource);
            if (res.Success)
            {
                using (resource)
                using (var tex = resource.QueryInterface<ID3D11Texture2D>())
                {
                    _context.CopyResource(_staging, tex);
                    _haveFrame = true;
                }
                _duplication.ReleaseFrame();
            }
        }
    }

    private unsafe void PushFrame()
    {
        _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
        try
        {
            int stride = _width * 4;
            fixed (byte* dst = _frameBuffer)
            {
                byte* src = (byte*)mapped.DataPointer;
                for (int y = 0; y < _height; y++)
                    Buffer.MemoryCopy(src + y * (long)mapped.RowPitch, dst + y * (long)stride, stride, stride);
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }

        // Escribe el frame crudo a FFmpeg (fuera del Map para no bloquear la GPU).
        _ffmpegIn.Write(_frameBuffer, 0, _frameBuffer.Length);
        _frameIndex++;
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(5000);

        // Para el audio y cierra su pipe para que FFmpeg vea fin de ambas entradas.
        try { _audio?.Stop(); } catch { }
        if (_audio?.LastError != null && LastError == null) LastError = _audio.LastError;
        try { _audioPipe?.Dispose(); } catch { }

        // Cierra stdin para que FFmpeg finalice y escriba el MP4.
        try { _ffmpegIn?.Flush(); _ffmpegIn?.Close(); } catch { }
        try { _ffmpeg?.WaitForExit(10000); } catch { }

        _duplication?.Dispose();
        _staging?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(); } catch { }
        _ffmpeg?.Dispose();
        _audio?.Dispose();
    }

    public void Dispose() => Stop();
}
