using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GravadorTela;

public sealed partial class MainWindow : Window
{
    private ScreenRecorder? _recorder;
    private bool _isRecording;
    private string _currentFile = "";
    private string _outputFolder;
    private List<DisplayInfo> _displays = new();
    private Settings _settings = new();

    public MainWindow()
    {
        InitializeComponent();

        _settings = Settings.Load();

        SetupModernTitleBar();
        RestoreWindowPosition();
        SetWindowIcon();

        _outputFolder = _settings.OutputFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        FolderBox.Text = _outputFolder;

        LoadMonitors();
        ApplySavedOptions();

        // Guarda al cerrar la ventana.
        AppWindow.Closing += (_, _) => SaveSettings();
    }

    private Microsoft.UI.Windowing.AppWindow AppWin =>
        Microsoft.UI.Windowing.AppWindow.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));

    private void RestoreWindowPosition()
    {
        var w = _settings.WinWidth ?? 440;
        var h = _settings.WinHeight ?? 420;
        AppWin.Resize(new Windows.Graphics.SizeInt32(w, h));
        if (_settings.WinX is int x && _settings.WinY is int y)
            AppWin.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void SetWindowIcon()
    {
        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(ico)) AppWin.SetIcon(ico);
        }
        catch { }
    }

    private void ApplySavedOptions()
    {
        if (_settings.MonitorIndex >= 0 && _settings.MonitorIndex < MonitorBox.Items.Count)
            MonitorBox.SelectedIndex = _settings.MonitorIndex;

        int fpsIdx = _settings.Fps switch { 24 => 0, 60 => 2, _ => 1 };
        FpsBox.SelectedIndex = fpsIdx;

        AudioCheck.IsChecked = _settings.CaptureAudio;
    }

    private void SaveSettings()
    {
        try
        {
            var pos = AppWin.Position;
            var size = AppWin.Size;
            _settings.WinX = pos.X;
            _settings.WinY = pos.Y;
            _settings.WinWidth = size.Width;
            _settings.WinHeight = size.Height;
            _settings.MonitorIndex = MonitorBox.SelectedIndex;
            _settings.Fps = int.Parse((string)((ComboBoxItem)FpsBox.SelectedItem).Content);
            _settings.CaptureAudio = AudioCheck.IsChecked == true;
            _settings.OutputFolder = _outputFolder;
            _settings.Save();
        }
        catch { }
    }

    /// <summary>Fondo Mica + barra de titulo extendida, estilo Windows 11.</summary>
    private void SetupModernTitleBar()
    {
        // Fondo Mica (toma el color del escritorio y el tema).
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop();

        // El contenido sube hasta la barra; usamos nuestra franja como barra de titulo.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void LoadMonitors()
    {
        _displays = DisplayEnumerator.List();
        MonitorBox.Items.Clear();
        foreach (var d in _displays)
            MonitorBox.Items.Add(d.Display);
        // La seleccion la decide ApplySavedOptions (o queda en 0 por defecto).
        if (MonitorBox.Items.Count > 0 && MonitorBox.SelectedIndex < 0)
            MonitorBox.SelectedIndex = 0;
    }

    private async void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");

        // En WinUI 3 unpackaged hay que asociar el picker al HWND de la ventana.
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _outputFolder = folder.Path;
            FolderBox.Text = _outputFolder;
        }
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (!_isRecording) StartRecording();
        else StopRecording();
    }

    private void StartRecording()
    {
        try
        {
            int fps = int.Parse((string)((ComboBoxItem)FpsBox.SelectedItem).Content);
            int monitor = MonitorBox.SelectedIndex >= 0 ? _displays[MonitorBox.SelectedIndex].Index : 0;
            bool audio = AudioCheck.IsChecked == true;

            Directory.CreateDirectory(_outputFolder);
            _currentFile = Path.Combine(_outputFolder, $"Gravacao_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            _recorder = new ScreenRecorder();
            _recorder.Start(_currentFile, fps, monitor, audio);

            _isRecording = true;
            RecordButton.Content = "■ Parar";
            EncoderLabel.Text = $"Encoder: {_recorder.EncoderName}";
            StatusLabel.Text = "Grabando...";
            SetOptionsEnabled(false);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
            CleanupAfterError();
        }
    }

    private void StopRecording()
    {
        try
        {
            _recorder?.Stop();
            var err = _recorder?.LastError;
            var frames = _recorder?.FramesWritten ?? 0;
            _recorder = null;
            StatusLabel.Text = err != null
                ? $"Guardado con avisos ({frames} frames): {err}"
                : $"Guardado: {Path.GetFileName(_currentFile)} ({frames} frames)";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error al guardar: {ex.Message}";
        }
        finally
        {
            _isRecording = false;
            RecordButton.Content = "● Grabar";
            SetOptionsEnabled(true);
        }
    }

    private void SetOptionsEnabled(bool on)
    {
        FpsBox.IsEnabled = on;
        MonitorBox.IsEnabled = on;
        FolderButton.IsEnabled = on;
        AudioCheck.IsEnabled = on;
    }

    private void CleanupAfterError()
    {
        _recorder?.Dispose();
        _recorder = null;
        _isRecording = false;
        RecordButton.Content = "● Grabar";
        SetOptionsEnabled(true);
    }
}
