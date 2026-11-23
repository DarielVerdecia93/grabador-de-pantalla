using System.Diagnostics;
using System.IO;

namespace GravadorTela;

/// <summary>Modo de codificacion elegido por el usuario.</summary>
public enum EncoderMode
{
    Auto,       // GPU si existe, si no CPU
    ForceGpu,   // forzar GPU (si no hay, cae a CPU con aviso)
    ForceCpu    // forzar CPU (libx264)
}

/// <summary>Encuentra ffmpeg.exe y decide el encoder segun la GPU disponible.</summary>
internal static class FfmpegLocator
{
    /// <summary>Ruta a ffmpeg.exe, o null si no se encuentra.</summary>
    public static string? FindFfmpeg()
    {
        // 1) En el PATH
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        // 2) Carpeta estandar de winget (Gyan.FFmpeg)
        var wingetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetRoot))
        {
            foreach (var f in Directory.EnumerateFiles(wingetRoot, "ffmpeg.exe", SearchOption.AllDirectories))
                return f;
        }

        return null;
    }

    /// <summary>
    /// Elige el encoder de FFmpeg segun el modo pedido.
    /// Auto/ForceGpu: GPU si existe (AMD AMF / NVIDIA NVENC / Intel QSV);
    /// ForceCpu o sin GPU: CPU (libx264). Devuelve (codec, nombreLegible).
    /// </summary>
    public static (string codec, string label) PickEncoder(string ffmpeg, string gpuName, EncoderMode mode = EncoderMode.Auto)
    {
        var available = ListEncoders(ffmpeg);
        bool Has(string c) => available.Contains(c);

        if (mode == EncoderMode.ForceCpu)
            return ("libx264", "Software (CPU) — forzado");

        // Elegir la GPU segun el fabricante.
        (string codec, string label)? gpu = null;
        if ((gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
             gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) && Has("h264_amf"))
            gpu = ("h264_amf", "AMD AMF (GPU)");
        else if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && Has("h264_nvenc"))
            gpu = ("h264_nvenc", "NVIDIA NVENC (GPU)");
        else if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) && Has("h264_qsv"))
            gpu = ("h264_qsv", "Intel QSV (GPU)");
        else if (Has("h264_amf")) gpu = ("h264_amf", "AMD AMF (GPU)");
        else if (Has("h264_nvenc")) gpu = ("h264_nvenc", "NVIDIA NVENC (GPU)");
        else if (Has("h264_qsv")) gpu = ("h264_qsv", "Intel QSV (GPU)");

        if (gpu is { } g)
            return mode == EncoderMode.ForceGpu ? (g.codec, g.label + " — forzado") : g;

        // No hay GPU disponible.
        return mode == EncoderMode.ForceGpu
            ? ("libx264", "Software (CPU) — sin GPU compatible")
            : ("libx264", "Software (CPU)");
    }

    private static HashSet<string> ListEncoders(string ffmpeg)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo(ffmpeg, "-hide_banner -encoders")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            foreach (var token in new[] { "h264_amf", "h264_nvenc", "h264_qsv", "libx264" })
                if (outp.Contains(token)) set.Add(token);
        }
        catch { }
        return set;
    }
}
