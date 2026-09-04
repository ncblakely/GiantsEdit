using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GiantsEdit.Core.Rendering;

namespace GiantsEdit.App.Dialogs;

public partial class HeightBasedLightingDialog : Window
{
    private const int LevelCount = 7;
    private const double DirectionSize = 110;

    private readonly TextBox[] _heightInputs;
    private readonly TextBox[] _colorInputs;
    private readonly Border[] _colorPreviews;
    private readonly RadioButton[] _levelRadios;
    private readonly Action<HeightBasedLightingSettings> _settingsChanged;
    private readonly Action<HeightBasedLightingSettings> _applyRequested;
    private HeightBasedLightingSettings _settings;
    private bool _updating;

    public HeightBasedLightingSettings Settings => _settings.Clone();

    public HeightBasedLightingDialog()
        : this(HeightBasedLightingSettings.CreateDefault(), _ => { }, _ => { })
    {
    }

    public HeightBasedLightingDialog(
        HeightBasedLightingSettings settings,
        Action<HeightBasedLightingSettings> settingsChanged,
        Action<HeightBasedLightingSettings> applyRequested)
    {
        InitializeComponent();

        _settings = settings.Clone();
        _settingsChanged = settingsChanged;
        _applyRequested = applyRequested;
        _heightInputs =
        [
            HeightInput0, HeightInput1, HeightInput2, HeightInput3,
            HeightInput4, HeightInput5, HeightInput6
        ];
        _colorInputs =
        [
            ColorInput0, ColorInput1, ColorInput2, ColorInput3,
            ColorInput4, ColorInput5, ColorInput6
        ];
        _colorPreviews =
        [
            ColorPreview0, ColorPreview1, ColorPreview2, ColorPreview3,
            ColorPreview4, ColorPreview5, ColorPreview6
        ];
        _levelRadios =
        [
            LevelRadio0, LevelRadio1, LevelRadio2, LevelRadio3,
            LevelRadio4, LevelRadio5, LevelRadio6
        ];

        PopulateControls();

        for (int i = 0; i < LevelCount; i++)
        {
            int index = i;
            _heightInputs[i].TextChanged += (_, _) => OnTextChanged(index);
            _colorInputs[i].TextChanged += (_, _) => OnTextChanged(index);
            _levelRadios[i].IsCheckedChanged += (_, _) =>
            {
                if (!_updating && _levelRadios[index].IsChecked == true)
                {
                    _settings.LevelCount = index + 1;
                    UpdateEnabledRows();
                    NotifySettingsChanged();
                }
            };
        }

        ApplyAutomaticallyCheckBox.IsCheckedChanged += (_, _) =>
        {
            _settings.ApplyAutomatically = ApplyAutomaticallyCheckBox.IsChecked == true;
            NotifySettingsChanged(_settings.ApplyAutomatically);
        };
        EnableSunSkyCheckBox.IsCheckedChanged += (_, _) =>
        {
            _settings.EnableSunSky = EnableSunSkyCheckBox.IsChecked == true;
            UpdateDirectionControls();
            NotifySettingsChanged();
        };

        SunColorInput.TextChanged += (_, _) => OnLightingColorChanged(SunColorInput, value => _settings.SunColor = value);
        SkyColorInput.TextChanged += (_, _) => OnLightingColorChanged(SkyColorInput, value => _settings.SkyColor = value);
        TopColorInput.TextChanged += (_, _) => OnLightingColorChanged(TopColorInput, value => _settings.TopColor = value);

        SunTopCanvas.PointerPressed += OnSunTopPointer;
        SunTopCanvas.PointerMoved += OnSunTopPointer;
        SunSideCanvas.PointerPressed += OnSunSidePointer;
        SunSideCanvas.PointerMoved += OnSunSidePointer;
        BtnApply.Click += (_, _) => ApplyCurrentSettings();
        BtnClose.Click += (_, _) => Close();
    }

    private void PopulateControls()
    {
        _updating = true;
        for (int i = 0; i < LevelCount; i++)
        {
            _heightInputs[i].Text = _settings.HeightLevels[i].ToString("G9", CultureInfo.InvariantCulture);
            _colorInputs[i].Text = ToHex(_settings.HeightColors[i]);
            UpdatePreview(i, _settings.HeightColors[i]);
        }

        _levelRadios[_settings.LevelCount - 1].IsChecked = true;
        ApplyAutomaticallyCheckBox.IsChecked = _settings.ApplyAutomatically;
        EnableSunSkyCheckBox.IsChecked = _settings.EnableSunSky;
        SunColorInput.Text = ToHex(_settings.SunColor);
        SkyColorInput.Text = ToHex(_settings.SkyColor);
        TopColorInput.Text = ToHex(_settings.TopColor);
        _updating = false;

        UpdateEnabledRows();
        UpdateDirectionControls();
        UpdateValidation();
        UpdateSunMarkers();
    }

    private void OnTextChanged(int index)
    {
        if (_updating)
            return;

        if (float.TryParse(_heightInputs[index].Text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float height))
            _settings.HeightLevels[index] = height;

        if (TryParseColor(_colorInputs[index].Text, out LightingColor color))
        {
            _settings.HeightColors[index] = color;
            UpdatePreview(index, color);
        }

        NotifySettingsChanged();
    }

    private void OnLightingColorChanged(TextBox input, Action<LightingColor> setter)
    {
        if (_updating || !TryParseColor(input.Text, out LightingColor color))
            return;

        setter(color);
        NotifySettingsChanged();
    }

    private void NotifySettingsChanged(bool apply = false)
    {
        if (_updating)
            return;

        UpdateValidation();
        var settings = _settings.Clone();
        _settingsChanged(settings);
        if ((apply || _settings.ApplyAutomatically) && _settings.Validate() == null)
            _applyRequested(settings);
    }

    private void ApplyCurrentSettings()
    {
        UpdateValidation();
        if (_settings.Validate() == null)
            _applyRequested(_settings.Clone());
    }

    private void UpdateEnabledRows()
    {
        for (int i = 0; i < LevelCount; i++)
        {
            bool enabled = i < _settings.LevelCount;
            _heightInputs[i].IsEnabled = enabled;
            _colorInputs[i].IsEnabled = enabled;
            _colorPreviews[i].Opacity = enabled ? 1 : 0.45;
        }
    }

    private void UpdateDirectionControls()
    {
        bool enabled = _settings.EnableSunSky;
        SunTopCanvas.IsEnabled = enabled;
        SunSideCanvas.IsEnabled = enabled;
        SunColorInput.IsEnabled = enabled;
        SkyColorInput.IsEnabled = enabled;
        TopColorInput.IsEnabled = enabled;
    }

    private void UpdateValidation()
    {
        string? validation = _settings.Validate();
        ValidationText.Text = validation ?? string.Empty;
        ValidationText.IsVisible = validation != null;
    }

    private void UpdatePreview(int index, LightingColor color)
    {
        _colorPreviews[index].Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
    }

    private void OnSunTopPointer(object? sender, PointerEventArgs e)
    {
        if (!_settings.EnableSunSky ||
            !e.GetCurrentPoint(SunTopCanvas).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetCurrentPoint(SunTopCanvas).Position;
        float x = (float)Math.Clamp(point.X, 0, DirectionSize - 1);
        float y = (float)Math.Clamp(point.Y, 0, DirectionSize - 1);
        float sunX = x / ((float)(DirectionSize - 1) / 2f) - 1f;
        float sunY = y / ((float)(DirectionSize - 1) / 2f) - 1f;
        float radius = MathF.Sqrt(sunX * sunX + sunY * sunY);
        if (radius > 1f)
        {
            sunX /= radius;
            sunY /= radius;
        }

        _settings.SunDirection = new(sunX, sunY, MathF.Sqrt(MathF.Max(0, 1 - sunX * sunX - sunY * sunY)));
        UpdateSunMarkers();
        NotifySettingsChanged();
    }

    private void OnSunSidePointer(object? sender, PointerEventArgs e)
    {
        if (!_settings.EnableSunSky ||
            !e.GetCurrentPoint(SunSideCanvas).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetCurrentPoint(SunSideCanvas).Position;
        float x = (float)Math.Clamp(point.X, 0, DirectionSize - 1);
        float y = (float)Math.Clamp(point.Y, 0, DirectionSize - 1);
        float horizontal = 1.0002f - x / (float)(DirectionSize - 1);
        float vertical = 1.0001f - y / (float)(DirectionSize - 1);
        float radius = MathF.Sqrt(horizontal * horizontal + vertical * vertical);
        if (radius < 0.0001f)
            return;

        horizontal /= radius;
        vertical /= radius;
        float planarLength = MathF.Sqrt(
            _settings.SunDirection.X * _settings.SunDirection.X
            + _settings.SunDirection.Y * _settings.SunDirection.Y);
        if (planarLength < 0.0001f)
            _settings.SunDirection = new(0, 0, vertical);
        else
            _settings.SunDirection = new(
                _settings.SunDirection.X / planarLength * horizontal,
                _settings.SunDirection.Y / planarLength * horizontal,
                vertical);

        UpdateSunMarkers();
        NotifySettingsChanged();
    }

    private void UpdateSunMarkers()
    {
        float topX = (_settings.SunDirection.X + 1) * 0.5f * (float)(DirectionSize - 1) - 5;
        float topY = (_settings.SunDirection.Y + 1) * 0.5f * (float)(DirectionSize - 1) - 5;
        Canvas.SetLeft(SunTopMarker, topX);
        Canvas.SetTop(SunTopMarker, topY);

        float planarLength = MathF.Sqrt(
            _settings.SunDirection.X * _settings.SunDirection.X
            + _settings.SunDirection.Y * _settings.SunDirection.Y);
        Canvas.SetLeft(SunSideMarker, (1 - planarLength) * (float)(DirectionSize - 1) - 5);
        Canvas.SetTop(SunSideMarker, (1 - _settings.SunDirection.Z) * (float)(DirectionSize - 1) - 5);
    }

    private static string ToHex(LightingColor color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseColor(string? text, out LightingColor color)
    {
        color = default;
        string value = (text ?? string.Empty).Trim();
        if (value.StartsWith('#'))
            value = value[1..];
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return false;

        color = new(
            (byte)(rgb >> 16),
            (byte)(rgb >> 8),
            (byte)rgb);
        return true;
    }
}
