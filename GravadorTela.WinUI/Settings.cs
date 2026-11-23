using System.IO;
using System.Text.Json;

namespace GravadorTela;

/// <summary>
/// Preferencias de la app: posicion/tamano de ventana y ultimas opciones.
/// Se guardan en %LOCALAPPDATA%\GravadorTela\settings.json.
/// </summary>
public sealed class Settings
{
    // Ventana
    public int? WinX { get; set; }
    public int? WinY { get; set; }
    public int? WinWidth { get; set; }
    public int? WinHeight { get; set; }

    // Opciones
    public int MonitorIndex { get; set; }
    public int Fps { get; set; } = 30;
    public bool CaptureAudio { get; set; }
    public string? OutputFolder { get; set; }
    public EncoderMode EncoderMode { get; set; } = EncoderMode.Auto;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GravadorTela");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* json corrupto -> usar valores por defecto */ }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { /* no bloquear el cierre si falla el guardado */ }
    }
}
