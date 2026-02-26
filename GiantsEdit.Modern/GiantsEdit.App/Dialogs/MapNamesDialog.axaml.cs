using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GiantsEdit.App.Dialogs;

public partial class MapNamesDialog : Window
{
    public bool Confirmed { get; private set; }

    // Results
    public string MapName { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public int MapType { get; private set; }
    public bool Shareable { get; private set; }
    public string BinFileName { get; private set; } = string.Empty;

    public MapNamesDialog()
    {
        InitializeComponent();

        BtnOk.Click += OnOk;
        BtnCancel.Click += (_, _) => Close();
        BtnHelp.Click += OnHelp;

        RbSingle.IsCheckedChanged += (_, _) => { if (RbSingle.IsChecked == true) UpdateFlags(); };
        Rb3Way.IsCheckedChanged += (_, _) => { if (Rb3Way.IsChecked == true) UpdateFlags(); };
        RbMecc.IsCheckedChanged += (_, _) => { if (RbMecc.IsChecked == true) UpdateFlags(); };
        RbReaper.IsCheckedChanged += (_, _) => { if (RbReaper.IsChecked == true) UpdateFlags(); };
        RbCustom.IsCheckedChanged += (_, _) =>
        {
            TxtCustomType.IsEnabled = RbCustom.IsChecked == true;
            if (RbCustom.IsChecked == true) UpdateFlags();
        };
        TxtCustomType.TextChanged += (_, _) => { if (RbCustom.IsChecked == true) UpdateFlags(); };
        TxtMapName.TextChanged += (_, _) => UpdateFileName();
    }

    /// <summary>
    /// Initialize the dialog with existing values from the document.
    /// </summary>
    public void SetValues(string binName, string message, int mapType, bool shareable)
    {
        // Extract the map name from the bin filename
        string name = binName;
        string upper = name.ToUpperInvariant();
        if (upper.StartsWith("W_M_REAPER_"))
            name = name[11..^4];
        else if (upper.StartsWith("W_M_MECC_"))
            name = name[9..^4];
        else if (upper.StartsWith("W_M_3WAY_"))
            name = name[9..^4];
        else if (upper.StartsWith("W_"))
            name = name[2..^4];
        else if (name.Length > 4)
            name = name[..^4];

        TxtMapName.Text = name;

        // Decode \n escapes in message to actual newlines
        Message = message;
        string displayMsg = message.Replace("\\n", "\n").Replace("\\\\", "\\");
        TxtMessage.Text = displayMsg;

        ChkShareable.IsChecked = shareable;

        switch (mapType)
        {
            case 0: RbSingle.IsChecked = true; break;
            case 0x31: Rb3Way.IsChecked = true; break;
            case 0x0D: RbMecc.IsChecked = true; break;
            case 0x0B: RbReaper.IsChecked = true; break;
            default:
                RbCustom.IsChecked = true;
                TxtCustomType.Text = mapType.ToString();
                break;
        }

        UpdateFlags();
    }

    private int GetSelectedMapType()
    {
        if (RbSingle.IsChecked == true) return 0;
        if (Rb3Way.IsChecked == true) return 0x31;
        if (RbMecc.IsChecked == true) return 0x0D;
        if (RbReaper.IsChecked == true) return 0x0B;
        if (RbCustom.IsChecked == true && int.TryParse(TxtCustomType.Text, out int v)) return v;
        return 0;
    }

    private int GetTypeIndex()
    {
        if (RbSingle.IsChecked == true) return 0;
        if (Rb3Way.IsChecked == true) return 1;
        if (RbMecc.IsChecked == true) return 2;
        if (RbReaper.IsChecked == true) return 3;
        if (RbCustom.IsChecked == true) return 4;
        return 0;
    }

    private void UpdateFlags()
    {
        // Match Delphi: set the custom text box for predefined types,
        // then always read the numeric value from it
        int idx = GetTypeIndex();
        switch (idx)
        {
            case 0: TxtCustomType.Text = "0"; break;
            case 1: TxtCustomType.Text = "49"; break;
            case 2: TxtCustomType.Text = "13"; break;
            case 3: TxtCustomType.Text = "11"; break;
        }

        if (!int.TryParse(TxtCustomType.Text, out int i))
        {
            i = 0;
            TxtCustomType.Text = "";
        }

        string s = (i & 1) != 0 ? "Multiplayer" : "Singleplayer";

        s += (i & 18) switch
        {
            0 => "\n2 Mecc bases",
            2 => "\n0 Mecc bases",
            16 => "\n1 Mecc base",
            _ => "\n? Mecc bases"
        };

        s += (i & 36) switch
        {
            0 => "\n2 Reaper bases",
            4 => "\n0 Reaper bases",
            32 => "\n1 Reaper base",
            _ => "\n? Reaper bases"
        };

        s += (i & 8) != 0 ? "\n0 Kabutos" : "\n1 Kabuto";
        s += (i & 64) != 0 ? "\nNo hosting allowed" : "\nHosting allowed";

        TxtFlags.Text = s;
        UpdateFileName();
    }

    private void UpdateFileName()
    {
        string name = TxtMapName.Text ?? "";
        int idx = GetTypeIndex();
        TxtFileName.Text = idx switch
        {
            1 => $"w_M_3Way_{name}.bin",
            2 => $"w_M_Mecc_{name}.bin",
            3 => $"w_M_Reaper_{name}.bin",
            _ => $"w_{name}.bin"
        };
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        string name = TxtMapName.Text ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return;

        MapName = name;
        MapType = GetSelectedMapType();
        Shareable = ChkShareable.IsChecked == true;

        int idx = GetTypeIndex();
        BinFileName = idx switch
        {
            1 => $"w_M_3Way_{name}.bin",
            2 => $"w_M_Mecc_{name}.bin",
            3 => $"w_M_Reaper_{name}.bin",
            _ => $"w_{name}.bin"
        };

        // Encode newlines back to \n escapes
        string raw = TxtMessage.Text ?? "";
        Message = raw.Replace("\\", "\\\\").Replace("\r\n", "\\n").Replace("\n", "\\n");

        Confirmed = true;
        Close();
    }

    private async void OnHelp(object? sender, RoutedEventArgs e)
    {
        var msgBox = new Window
        {
            Title = "Help",
            Width = 450,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = """
                        In-game map name:
                        This is the name that will show up in Giants' map selection dialog and in Gamespy.

                        Message:
                        This is the text GMM shows when launching a map. Place your credits here.

                        Map type:
                        This is a number Giants uses to recognize the type of the map.
                        It's a sum of the following numbers:
                         1 => multiplayer map
                         2 => no meccs allowed
                         4 => no reaper allowed
                         8 => no kabuto allowed
                        16 => no meccs vs meccs allowed
                        32 => no reaper vs reaper allowed
                        64 => no hosting, joining only

                        Map shareable:
                        When not checked this prevents other users from downloading the map.
                        """,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(16)
                }
            }
        };
        await msgBox.ShowDialog(this);
    }
}
