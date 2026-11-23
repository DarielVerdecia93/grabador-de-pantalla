using Vortice.DXGI;

namespace GravadorTela;

/// <summary>Un monitor detectado, listo para mostrar en la UI.</summary>
public sealed record DisplayInfo(int Index, string Name, int Width, int Height)
{
    public string Display => $"{Name} ({Width}x{Height})";
}

/// <summary>Enumera los monitores conectados via DXGI.</summary>
public static class DisplayEnumerator
{
    public static List<DisplayInfo> List()
    {
        var result = new List<DisplayInfo>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        if (factory.EnumAdapters1(0, out var adapter).Failure) return result;
        using (adapter)
        {
            int i = 0;
            while (adapter.EnumOutputs((uint)i, out var output).Success)
            {
                using (output)
                {
                    var d = output.Description;
                    int w = d.DesktopCoordinates.Right - d.DesktopCoordinates.Left;
                    int h = d.DesktopCoordinates.Bottom - d.DesktopCoordinates.Top;
                    string name = string.IsNullOrWhiteSpace(d.DeviceName) ? $"Monitor {i + 1}" : d.DeviceName;
                    result.Add(new DisplayInfo(i, name, w, h));
                }
                i++;
            }
        }
        return result;
    }
}
