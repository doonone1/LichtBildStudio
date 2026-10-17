using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpRgbaImage = SixLabors.ImageSharp.Image<Rgba32>;
using SixLabors.ImageSharp.Processing;

namespace LichtbildStudio;

public partial class MainWindow : Window
{
    private readonly List<string> _allFiles = new();
    private ImageSharpRgbaImage? _original;
    private ImageSharpRgbaImage? _working;
    private string? _currentFile;
    private AutoProposal _proposal = new();
    private bool _showOriginal;

    public MainWindow()
    {
        InitializeComponent();
        UpdateSliderLabels();
    }

    private async void OpenFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "JPEG-Ordner auswählen",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is null)
            return;

        var path = folder.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        _allFiles.Clear();
        _allFiles.AddRange(Directory.EnumerateFiles(path)
            .Where(IsJpeg)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        FolderLabel.Text = $"{Path.GetFileName(path)}  •  {_allFiles.Count} JPEGs";
        StatusText.Text = $"{_allFiles.Count} JPEGs gefunden";
        RefreshFileList();

        if (_allFiles.Count > 0)
            ImageList.SelectedIndex = 0;
        else
            ClearPreview();
    }

    private void RefreshFileList()
    {
        var filter = SearchBox.Text?.Trim() ?? string.Empty;
        var files = string.IsNullOrWhiteSpace(filter)
            ? _allFiles
            : _allFiles.Where(x => Path.GetFileName(x).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        ImageList.ItemsSource = files.Select(Path.GetFileName).ToList();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var oldIndex = ImageList.SelectedIndex;
        RefreshFileList();
        if (oldIndex >= 0 && ImageList.ItemCount > 0)
            ImageList.SelectedIndex = Math.Min(oldIndex, ImageList.ItemCount - 1);
    }

    private async void ImageList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ImageList.SelectedItem is not string name)
            return;

        var path = _allFiles.FirstOrDefault(x => string.Equals(Path.GetFileName(x), name, StringComparison.OrdinalIgnoreCase));
        if (path is null)
            return;

        await LoadImageAsync(path);
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            StatusText.Text = "Bild wird geladen…";
            _currentFile = path;

            _original?.Dispose();
            _working?.Dispose();

            await using var stream = File.OpenRead(path);
            _original = await ImageSharpImage.LoadAsync<Rgba32>(stream);
            _working = _original.Clone();

            ResetSlidersWithoutPreview();
            _proposal = new AutoProposal();
            AutoSummary.Text = "Noch keine Analyse durchgeführt.";
            _showOriginal = false;

            UpdatePreview();
            ImageInfo.Text = $"{Path.GetFileName(path)}  •  {_original.Width} × {_original.Height}";
            StatusText.Text = "Bild geladen";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler beim Laden";
            AutoSummary.Text = ex.Message;
        }
    }

    private void AnalyzeAuto_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_original is null)
        {
            StatusText.Text = "Bitte zuerst ein JPEG auswählen.";
            return;
        }

        _proposal = Analyze(_original);

        var parts = new List<string>();
        if (AutoExposureCheck.IsChecked == true)
            parts.Add($"Belichtung {_proposal.Exposure:+0.00;-0.00;0.00} EV");
        if (AutoWhiteBalanceCheck.IsChecked == true)
            parts.Add($"Wärme {_proposal.Warmth:+0;-0;0}");
        if (AutoHighlightsCheck.IsChecked == true)
            parts.Add($"Lichter {_proposal.Highlights:+0;-0;0}");
        if (AutoShadowsCheck.IsChecked == true)
            parts.Add($"Schatten {_proposal.Shadows:+0;-0;0}");
        if (AutoContrastCheck.IsChecked == true)
            parts.Add($"Kontrast {_proposal.Contrast:0.00}");
        if (AutoColorCheck.IsChecked == true)
            parts.Add($"Dynamik {_proposal.Saturation:0.00}");

        AutoSummary.Text = parts.Count == 0
            ? "Keine automatische Korrektur ausgewählt."
            : "Vorschlag: " + string.Join("  •  ", parts);

        StatusText.Text = "Vorschlag berechnet";
    }

    private void ApplyAuto_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_original is null)
        {
            StatusText.Text = "Bitte zuerst ein JPEG auswählen.";
            return;
        }

        if (string.IsNullOrWhiteSpace(AutoSummary.Text) || AutoSummary.Text.StartsWith("Noch keine"))
            AnalyzeAuto_Click(sender, e);

        ApplyAdjustments(
            AutoExposureCheck.IsChecked == true ? _proposal.Exposure : 0,
            AutoWhiteBalanceCheck.IsChecked == true ? _proposal.Warmth : 0,
            AutoHighlightsCheck.IsChecked == true ? _proposal.Highlights : 0,
            AutoShadowsCheck.IsChecked == true ? _proposal.Shadows : 0,
            AutoContrastCheck.IsChecked == true ? _proposal.Contrast : 1,
            AutoColorCheck.IsChecked == true ? _proposal.Saturation : 1,
            AutoContrastCheck.IsChecked == true ? _proposal.BlackPoint : 0);

        StatusText.Text = "Vorschlag angewendet";
    }

    private void Adjustment_Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        UpdateSliderLabels();

        if (_original is null)
            return;

        ApplyAdjustments(
            ExposureSlider.Value,
            WarmthSlider.Value,
            HighlightsSlider.Value,
            ShadowsSlider.Value,
            ContrastSlider.Value,
            SaturationSlider.Value,
            BlackPointSlider.Value);
    }

    private void ApplyAdjustments(
        double exposure,
        double warmth,
        double highlights,
        double shadows,
        double contrast,
        double saturation,
        double blackPoint)
    {
        if (_original is null)
            return;

        _working?.Dispose();
        _working = _original.Clone();

        var exposureFactor = Math.Pow(2.0, exposure * 0.75);

        _working.Mutate(ctx =>
        {
            ctx.Brightness((float)exposureFactor);
            ctx.Contrast((float)contrast);
            ctx.Saturate((float)Math.Max(0, saturation));

            if (Math.Abs(warmth) > 0.01)
                ctx.Hue((float)Math.Clamp(warmth * 0.25, -8, 8));
        });

        var black = Math.Clamp((float)blackPoint / 100f, -0.4f, 0.4f);
        _working.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];

                    var r = pixel.R / 255f;
                    var g = pixel.G / 255f;
                    var b = pixel.B / 255f;
                    var lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;

                    if (Math.Abs(black) > 0.001f)
                    {
                        var mapped = Math.Clamp((lum - black) / Math.Max(0.001f, 1f - black), 0f, 1f);
                        var ratio = lum <= 0.5f ? mapped / Math.Max(lum, 0.001f) : 1f;
                        pixel.R = ClampByte(pixel.R * ratio);
                        pixel.G = ClampByte(pixel.G * ratio);
                        pixel.B = ClampByte(pixel.B * ratio);
                        lum = 0.2126f * (pixel.R / 255f) + 0.7152f * (pixel.G / 255f) + 0.0722f * (pixel.B / 255f);
                    }

                    var h = Math.Clamp((float)highlights / 100f, -1f, 1f);
                    var s = Math.Clamp((float)shadows / 100f, -1f, 1f);

                    if (lum > 0.55f && h != 0)
                    {
                        var factor = 1f - h * Math.Clamp((lum - 0.55f) / 0.45f, 0f, 1f) * 0.35f;
                        pixel.R = ClampByte(pixel.R * factor);
                        pixel.G = ClampByte(pixel.G * factor);
                        pixel.B = ClampByte(pixel.B * factor);
                    }

                    if (lum < 0.45f && s != 0)
                    {
                        var factor = 1f + s * Math.Clamp((0.45f - lum) / 0.45f, 0f, 1f) * 0.35f;
                        pixel.R = ClampByte(pixel.R * factor);
                        pixel.G = ClampByte(pixel.G * factor);
                        pixel.B = ClampByte(pixel.B * factor);
                    }
                }
            }
        });

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var image = _showOriginal ? _original : _working;
        if (image is null)
        {
            ClearPreview();
            return;
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        ms.Position = 0;

        PreviewImage.Source = new Bitmap(ms);
        EmptyPreviewText.IsVisible = false;
    }

    private void ToggleOriginal_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_original is null || _working is null)
            return;

        _showOriginal = !_showOriginal;
        UpdatePreview();
        StatusText.Text = _showOriginal ? "Original angezeigt" : "Bearbeitung angezeigt";
    }

    private async void Export_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_working is null || _currentFile is null)
        {
            StatusText.Text = "Bitte zuerst ein JPEG bearbeiten.";
            return;
        }

        var suggested = Path.GetFileNameWithoutExtension(_currentFile) + "_Lichtbild.jpg";

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "JPEG exportieren",
            SuggestedFileName = suggested,
            DefaultExtension = "jpg",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JPEG Bild")
                {
                    Patterns = new[] { "*.jpg", "*.jpeg" }
                }
            }
        });

        if (file is null)
            return;

        var outputPath = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        try
        {
            using var output = _working.Clone();
            output.Metadata.HorizontalResolution = 300;
            output.Metadata.VerticalResolution = 300;

            await using var stream = File.Create(outputPath);
            await output.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 100 });

            StatusText.Text = "JPEG exportiert";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Exportfehler";
            AutoSummary.Text = ex.Message;
        }
    }

    private void Reset_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_original is null)
            return;

        ResetSlidersWithoutPreview();
        _proposal = new AutoProposal();
        AutoSummary.Text = "Bearbeitung zurückgesetzt.";
        _showOriginal = false;
        UpdatePreview();
        StatusText.Text = "Zurückgesetzt";
    }

    private void ResetSlidersWithoutPreview()
    {
        ExposureSlider.Value = 0;
        ContrastSlider.Value = 1;
        SaturationSlider.Value = 1;
        WarmthSlider.Value = 0;
        HighlightsSlider.Value = 0;
        ShadowsSlider.Value = 0;
        BlackPointSlider.Value = 0;
        UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        ExposureValue.Text = $"{ExposureSlider.Value:0.00} EV";
        ContrastValue.Text = ContrastSlider.Value.ToString("0.00", CultureInfo.GetCultureInfo("de-DE"));
        SaturationValue.Text = SaturationSlider.Value.ToString("0.00", CultureInfo.GetCultureInfo("de-DE"));
        WarmthValue.Text = WarmthSlider.Value.ToString("0", CultureInfo.GetCultureInfo("de-DE"));
        HighlightsValue.Text = HighlightsSlider.Value.ToString("0", CultureInfo.GetCultureInfo("de-DE"));
        ShadowsValue.Text = ShadowsSlider.Value.ToString("0", CultureInfo.GetCultureInfo("de-DE"));
        BlackPointValue.Text = BlackPointSlider.Value.ToString("0", CultureInfo.GetCultureInfo("de-DE"));
    }

    private void Theme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        ThemeButton.Content = RequestedThemeVariant == ThemeVariant.Dark
            ? "Light Mode"
            : "Dark Mode";
    }

    private void Auto_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => AnalyzeAuto_Click(sender, e);

    private void ClearPreview()
    {
        _original?.Dispose();
        _working?.Dispose();
        _original = null;
        _working = null;
        _currentFile = null;
        PreviewImage.Source = null;
        EmptyPreviewText.IsVisible = true;
        ImageInfo.Text = "Kein Bild ausgewählt";
        AutoSummary.Text = "Noch keine Analyse durchgeführt.";
    }

    private static bool IsJpeg(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static byte ClampByte(float value)
        => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static AutoProposal Analyze(ImageSharpRgbaImage image)
    {
        long count = 0;
        double luminance = 0;
        double red = 0;
        double green = 0;
        double blue = 0;
        double highlights = 0;
        double shadows = 0;

        const int maxSamples = 120_000;
        var step = Math.Max(1, (int)Math.Sqrt((double)image.Width * image.Height / maxSamples));

        for (var y = 0; y < image.Height; y += step)
        {
            for (var x = 0; x < image.Width; x += step)
            {
                var p = image[x, y];
                var r = p.R / 255.0;
                var g = p.G / 255.0;
                var b = p.B / 255.0;
                var l = 0.2126 * r + 0.7152 * g + 0.0722 * b;

                red += r;
                green += g;
                blue += b;
                luminance += l;
                highlights += l > 0.82 ? 1 : 0;
                shadows += l < 0.18 ? 1 : 0;
                count++;
            }
        }

        if (count == 0)
            return new AutoProposal();

        var avgL = luminance / count;
        var avgR = red / count;
        var avgG = green / count;
        var avgB = blue / count;

        var exposure = Math.Clamp(Math.Log(0.48 / Math.Max(avgL, 0.02), 2) * 0.75, -0.75, 0.75);
        var warmth = Math.Clamp((avgR - avgB) * 45.0, -18, 18);

        var highlightRatio = highlights / count;
        var shadowRatio = shadows / count;

        var highlightCorrection = Math.Clamp(-highlightRatio * 80.0, -35, 0);
        var shadowCorrection = Math.Clamp(shadowRatio * 90.0, 0, 35);

        var contrast = Math.Clamp(1.0 + (0.48 - avgL) * 0.45, 0.88, 1.15);
        var saturation = Math.Clamp(1.0 + (0.18 - Math.Abs(avgR - avgB)) * 0.25, 0.98, 1.12);

        return new AutoProposal
        {
            Exposure = exposure,
            Warmth = warmth,
            Highlights = highlightCorrection,
            Shadows = shadowCorrection,
            Contrast = contrast,
            Saturation = saturation,
            BlackPoint = Math.Clamp((0.48 - avgL) * 20.0, -8, 8)
        };
    }

    private sealed class AutoProposal
    {
        public double Exposure { get; init; }
        public double Warmth { get; init; }
        public double Highlights { get; init; }
        public double Shadows { get; init; }
        public double Contrast { get; init; } = 1;
        public double Saturation { get; init; } = 1;
        public double BlackPoint { get; init; }
    }
}
