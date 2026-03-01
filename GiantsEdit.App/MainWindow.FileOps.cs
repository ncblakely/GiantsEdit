using System.Diagnostics;
using Avalonia.Platform.Storage;
using GiantsEdit.App.Dialogs;
using GiantsEdit.Core.Formats;

namespace GiantsEdit.App;

public partial class MainWindow
{
    // BMP format constants
    private const int BmpHeaderSize = 54;
    private const int BmpDibHeaderSize = 40;
    private const short BmpPlanes = 1;
    private const short BmpBitsPerPixel = 32;
    private const int BmpDpi72 = 2835;

    #region File menu handlers

    private async Task NewWorldAsync()
    {
        var dlg = new NewMapDialog();
        await dlg.ShowDialog(this);

        if (dlg.Confirmed)
        {
            CloseOwnedWindows();
            _vm.Document.NewWorld(dlg.MapWidth, dlg.MapHeight, dlg.TextureName);
            StatusText.Text = $"New map: {dlg.MapWidth}x{dlg.MapHeight}";
        }
    }

    private async Task OpenWorldAsync()
    {
        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(_prefs.LastOpenFolder) && Directory.Exists(_prefs.LastOpenFolder))
            startFolder = await StorageProvider.TryGetFolderFromPathAsync(_prefs.LastOpenFolder);

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Map",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter =
            [
                new FilePickerFileType("Map Files") { Patterns = ["*.gck", "*.bin"] },
                new FilePickerFileType("GCK Archives") { Patterns = ["*.gck"] },
                new FilePickerFileType("BIN Files") { Patterns = ["*.bin"] }
            ]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                _prefs.LastOpenFolder = System.IO.Path.GetDirectoryName(path) ?? "";
                _prefs.Save();

                try
                {
                    CloseOwnedWindows();
                    if (path.EndsWith(".gck", StringComparison.OrdinalIgnoreCase))
                        _vm.Document.LoadGck(path);
                    else
                        _vm.Document.LoadWorld(path);
                    StatusText.Text = $"Loaded: {System.IO.Path.GetFileName(path)}";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OpenWorld] Error: {ex}");
                    StatusText.Text = $"Error: {ex.Message}";
                }
            }
        }
    }

    private async Task SaveWorldAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Map As",
            DefaultExtension = "gck",
            FileTypeChoices =
            [
                new FilePickerFileType("GCK Map Archive") { Patterns = ["*.gck"] }
            ]
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                try
                {
                    _vm.Document.SaveWorld(path);
                    StatusText.Text = $"Saved: {System.IO.Path.GetFileName(path)}";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SaveWorld] Error: {ex}");
                    StatusText.Text = $"Save failed: {ex.Message}";
                }
            }
        }
    }

    private void CloseMap()
    {
        _vm.Document.NewWorld(2, 2);
        _vm.TreeRoots.Clear();
        InvalidateViewport();
        StatusText.Text = "Map closed";
    }

    private void ClearTerrain()
    {
        if (_vm.Document.Terrain == null) return;
        int w = _vm.Document.Terrain.Width;
        int h = _vm.Document.Terrain.Height;
        _vm.Document.NewWorld(w, h);
        StatusText.Text = "Terrain cleared";
    }

    private void ClearObjects()
    {
        var root = _vm.Document.WorldRoot;
        if (root == null) return;

        root.RemoveNodesByName(BinFormatConstants.NodeObject);
        InvalidateViewport();
        StatusText.Text = "Objects cleared";
    }

    private async Task ImportTerrainAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Terrain (GTI)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("GTI Files") { Patterns = ["*.gti"] }]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                try
                {
                    _vm.Document.LoadTerrain(path);
                    UploadTerrainToGpu();
                    InvalidateViewport();
                    var t = _vm.Document.Terrain;
                    StatusText.Text = $"Imported terrain: {System.IO.Path.GetFileName(path)} ({t?.Width}x{t?.Height})";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImportTerrain] Error: {ex}");
                    StatusText.Text = $"Import failed: {ex.Message}";
                }
            }
        }
    }

    private async Task ImportObjectsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Objects (BIN)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("BIN Files") { Patterns = ["*.bin"] }]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                try
                {
                    CloseOwnedWindows();
                    _vm.Document.LoadWorld(path);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ImportObjects] Error: {ex}");
                    StatusText.Text = $"Import failed: {ex.Message}";
                }
            }
        }
    }

    private async Task ExportTerrainAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Terrain",
            DefaultExtension = "gti",
            FileTypeChoices = [new FilePickerFileType("GTI Files") { Patterns = ["*.gti"] }]
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                try
                {
                    _vm.Document.SaveTerrain(path);
                    StatusText.Text = $"Terrain exported: {System.IO.Path.GetFileName(path)}";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ExportTerrain] Error: {ex}");
                    StatusText.Text = $"Export failed: {ex.Message}";
                }
            }
        }
    }

    private async Task ExportObjectsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Objects",
            DefaultExtension = "bin",
            FileTypeChoices = [new FilePickerFileType("BIN Files") { Patterns = ["*.bin"] }]
        });

        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                try
                {
                    _vm.Document.SaveWorld(path);

                    // Show export report in console (matches Delphi SaveBinW → ScanDone behavior)
                    var console = new ConsoleWindow();
                    var report = _vm.Document.GetExportReport();
                    if (!string.IsNullOrEmpty(report))
                    {
                        console.AppendLine(report);
                        console.AppendLine("These items were not included in the saved map file; all other items should be alright.");
                    }
                    else
                    {
                        console.AppendLine($"Objects exported successfully to {System.IO.Path.GetFileName(path)}");
                    }
                    console.Show(this);
                    StatusText.Text = $"Objects exported: {System.IO.Path.GetFileName(path)}";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ExportObjects] Error: {ex}");
                    StatusText.Text = $"Export failed: {ex.Message}";
                }
            }
        }
    }

    private async Task ExportBitmapAsync(string kind)
    {
        var terrain = _vm.Document.Terrain;
        if (terrain == null) { StatusText.Text = "No terrain loaded"; return; }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {kind}",
            DefaultExtension = "bmp",
            FileTypeChoices = [new FilePickerFileType("Bitmap") { Patterns = ["*.bmp"] }]
        });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        int w = terrain.Width;
        int h = terrain.Height;

        // Build raw RGBA pixel data
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int srcIdx = y * w + x;
                int dstIdx = ((h - 1 - y) * w + x) * 4; // flip Y for image

                byte r, g, b;
                switch (kind)
                {
                    case "lightmap":
                        r = terrain.LightMap[srcIdx * 3 + 0];
                        g = terrain.LightMap[srcIdx * 3 + 1];
                        b = terrain.LightMap[srcIdx * 3 + 2];
                        break;
                    case "heightmap":
                        float ht = terrain.Heights[srcIdx];
                        byte v = (byte)Math.Clamp((int)(ht + 128), 0, 255);
                        r = g = b = v;
                        break;
                    case "trianglemap":
                        byte tri = terrain.Triangles[srcIdx];
                        r = (byte)((tri & 1) != 0 ? 255 : 0);
                        g = (byte)((tri & 2) != 0 ? 255 : 0);
                        b = (byte)((tri & 4) != 0 ? 255 : 0);
                        break;
                    default:
                        r = g = b = 0;
                        break;
                }
                pixels[dstIdx + 0] = r;
                pixels[dstIdx + 1] = g;
                pixels[dstIdx + 2] = b;
                pixels[dstIdx + 3] = 255;
            }
        }

        // Write as simple 32-bit BMP
        WriteBmp32(path, w, h, pixels);
        StatusText.Text = $"Exported {kind}: {System.IO.Path.GetFileName(path)}";
    }

    private static void WriteBmp32(string path, int w, int h, byte[] rgba)
    {
        int rowBytes = w * 4;
        int imageSize = rowBytes * h;
        int fileSize = BmpHeaderSize + imageSize;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // BMP header
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(fileSize);
        bw.Write(0); // reserved
        bw.Write(BmpHeaderSize); // pixel data offset
        // DIB header (BITMAPINFOHEADER)
        bw.Write(BmpDibHeaderSize); // header size
        bw.Write(w);
        bw.Write(h);
        bw.Write(BmpPlanes); // planes
        bw.Write(BmpBitsPerPixel); // bpp
        bw.Write(0); // compression
        bw.Write(imageSize);
        bw.Write(BmpDpi72); bw.Write(BmpDpi72); // 72 DPI
        bw.Write(0); bw.Write(0);

        // Pixel data (BMP stores bottom-up, BGRA)
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bw.Write(rgba[i + 2]); // B
                bw.Write(rgba[i + 1]); // G
                bw.Write(rgba[i + 0]); // R
                bw.Write(rgba[i + 3]); // A
            }
        }
    }

    #endregion

    #region Editor menu handlers

    private async Task GoToLocationAsync()
    {
        var dlg = new GoToLocationDialog();
        await dlg.ShowDialog(this);
        if (dlg.Confirmed)
        {
            Viewport.Camera.LookAt(dlg.Target);
            InvalidateViewport();
            StatusText.Text = $"Camera moved to ({dlg.Target.X:F0}, {dlg.Target.Y:F0}, {dlg.Target.Z:F0})";
        }
    }

    private async Task PickColorAsync(string target)
    {
        // Simple placeholder — Avalonia doesn't have a built-in color picker dialog
        StatusText.Text = $"{target} color picker: not yet implemented";
        await Task.CompletedTask;
    }

    #endregion

    #region Terrain menu handlers

    private void SoftenTerrain(bool includeEdges)
    {
        var terrain = _vm.Document.Terrain;
        if (terrain == null) return;

        int w = terrain.Width;
        int h = terrain.Height;
        var src = (float[])terrain.Heights.Clone();
        int startX = includeEdges ? 0 : 1;
        int startY = includeEdges ? 0 : 1;
        int endX = includeEdges ? w : w - 1;
        int endY = includeEdges ? h : h - 1;

        for (int y = startY; y < endY; y++)
        {
            for (int x = startX; x < endX; x++)
            {
                float sum = 0;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                        {
                            sum += src[ny * w + nx];
                            count++;
                        }
                    }
                }
                terrain.Heights[y * w + x] = sum / count;
            }
        }

        _vm.Document.NotifyTerrainChanged();
    }

    private async Task SubdivideTerrainAsync()
    {
        var terrain = _vm.Document.Terrain;
        if (terrain == null)
        {
            StatusText.Text = "No terrain loaded";
            return;
        }

        var dialog = new SubdivideTerrainDialog(terrain.Width, terrain.Height);
        var result = await dialog.ShowDialog<int?>(this);
        if (result is not { } factor)
            return;

        StatusText.Text = $"Subdividing terrain by {factor}x...";

        var subdivided = await Task.Run(() => TerrainSubdivider.Subdivide(terrain, factor));
        _vm.Document.ReplaceTerrain(subdivided);

        StatusText.Text = $"Terrain subdivided {factor}x → {subdivided.Width}×{subdivided.Height}";
    }

    #endregion
}
