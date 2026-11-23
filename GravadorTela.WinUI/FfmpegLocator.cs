using System.Diagnostics;
using System.IO;

namespace GravadorTela;

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
    /// Elige el encoder de FFmpeg: GPU si existe (AMD AMF / NVIDIA NVENC / Intel QSV),
    /// con fallback a CPU (libx264). Devuelve (codec, nombreLegible).
    /// </summary>
    public static (string codec, string label) PickEncoder(string ffmpeg, string gpuName)
    {
        var available = ListEncoders(ffmpeg);

        bool Has(string c) => available.Contains(c);

        if ((gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
             gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) && Has("h264_amf"))
            return ("h264_amf", "AMD AMF (GPU)");

        if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && Has("h264_nvenc"))
            return ("h264_nvenc", "NVIDIA NVENC (GPU)");

        if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) && Has("h264_qsv"))
            return ("h264_qsv", "Intel QSV (GPU)");

        // Cualquier GPU que exponga AMF aunque el nombre no cuadre
        if (Has("h264_amf")) return ("h264_amf", "AMD AMF (GPU)");
        if (Has("h264_nvenc")) return ("h264_nvenc", "NVIDIA NVENC (GPU)");
        if (Has("h264_qsv")) return ("h264_qsv", "Intel QSV (GPU)");

        return ("libx264", "Software (CPU)");
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
