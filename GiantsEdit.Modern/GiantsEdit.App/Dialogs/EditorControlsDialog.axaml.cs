using Avalonia.Controls;
using GiantsEdit.Core.Services;

namespace GiantsEdit.App.Dialogs;

public partial class EditorControlsDialog : Window
{
    public EditorControlsDialog()
    {
        InitializeComponent();
    }

    public EditorControlsDialog(ControlScheme scheme) : this()
    {
        Title = scheme == ControlScheme.Default
            ? "Editor Controls (Default)"
            : "Editor Controls (Classic)";
        ControlsText.Text = scheme == ControlScheme.Default
            ? GetDefaultControls()
            : GetClassicControls();
    }

    private static string GetDefaultControls() => """
        CAMERA CONTROLS (UE5-style)
        Right drag           Rotate camera (mouse look)
        Middle drag          Pan camera (strafe)
        Left + Right drag    Dolly / strafe
        Scroll wheel         Zoom (dolly forward/back)
        RMB + W/A/S/D        Fly forward/left/back/right
        RMB + Q/E            Fly down/up

        HEIGHT EDITING MODE
        Left drag            Paint terrain to target height
        Shift + Left click   Pick height under cursor

        LIGHT EDITING MODE
        Left drag            Paint terrain light color
        Shift + Left click   Pick color under cursor

        TRIANGLE EDITING MODE
        Left drag            Paint/set triangles
        Shift + Left drag    Erase/toggle triangles

        OBJECT EDITING MODE
        Left click           Select object
        Left drag            Move selected object on terrain
        Shift + Left drag    Adjust object Z height
        Right click          Object context menu (create/delete)
        """;

    private static string GetClassicControls() => """
        CAMERA CONTROLS (Classic)
        Left drag            Rotate camera (yaw/pitch)
        Right drag           Pan camera (strafe)
        Left + Right drag    Zoom (dolly forward/back)
        Scroll wheel         Zoom (dolly forward/back)
        Ctrl + drag          Camera controls in any editing mode

        HEIGHT EDITING MODE
        Left drag            Paint terrain to target height
        Right click          Pick height under cursor

        LIGHT EDITING MODE
        Left drag            Paint terrain light color
        Right click          Pick color under cursor

        TRIANGLE EDITING MODE
        Left drag            Paint/set triangles
        Right drag           Erase/toggle triangles

        OBJECT EDITING MODE
        Left click           Select object
        Left drag            Move selected object on terrain
        Shift + Left drag    Adjust object Z height
        Right click          Object context menu (create/delete)
        """;
}
