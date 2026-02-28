using Avalonia.Controls;
using GiantsEdit.Core.DataModel;

namespace GiantsEdit.App;

public partial class MainWindow
{
    private bool _suppressOptionalLeafToggle;

    #region Property panel

    private void ApplyObjectProperties()
    {
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        // Apply object type (accepts name, ID, or "Name (ID)" format)
        var parsedType = ObjectNames.ParseInput(PropObjType.Text ?? "");
        bool typeChanged = false;
        if (parsedType.HasValue)
        {
            var typeLeaf = obj.FindChildLeaf("Type");
            if (typeLeaf != null && typeLeaf.Int32Value != parsedType.Value)
            {
                typeLeaf.Int32Value = parsedType.Value;
                typeChanged = true;
            }
            PropObjType.Text = ObjectNames.GetDisplayName(parsedType.Value);
            PropHeader.Text = $"Object: {PropObjType.Text}";
        }

        if (float.TryParse(PropObjX.Text, out float x))
            obj.FindChildLeaf("X")?.SetSingle(x);
        if (float.TryParse(PropObjY.Text, out float y))
            obj.FindChildLeaf("Y")?.SetSingle(y);
        if (float.TryParse(PropObjZ.Text, out float z))
            obj.FindChildLeaf("Z")?.SetSingle(z);
        if (float.TryParse(PropObjAngle.Text, out float angle))
            obj.FindChildLeaf("DirFacing")?.SetSingle(angle);
        if (ChkObjTilt.IsChecked == true)
        {
            if (float.TryParse(PropObjTiltFwd.Text, out float tf))
                obj.FindChildLeaf("TiltForward")?.SetSingle(tf);
            if (float.TryParse(PropObjTiltLeft.Text, out float tl))
                obj.FindChildLeaf("TiltLeft")?.SetSingle(tl);
        }

        // Apply optional fields only if their checkbox is checked
        if (ChkObjScale.IsChecked == true && float.TryParse(PropObjScale.Text, out float scale))
            obj.FindChildLeaf("Scale")?.SetSingle(scale);
        if (ChkObjAIMode.IsChecked == true && byte.TryParse(PropObjAIMode.Text, out byte aiMode))
        {
            var leaf = obj.FindChildLeaf("AIMode");
            if (leaf != null) leaf.ByteValue = aiMode;
        }
        if (ChkObjTeamID.IsChecked == true && int.TryParse(PropObjTeamID.Text, out int teamId))
        {
            var leaf = obj.FindChildLeaf("TeamID");
            if (leaf != null) leaf.Int32Value = teamId;
        }

        // Apply Light Color
        if (ChkObjLightColor.IsChecked == true)
        {
            if (float.TryParse(PropLightR.Text, out float lr))
                obj.FindChildLeaf("LightColorR")?.SetSingle(lr);
            if (float.TryParse(PropLightG.Text, out float lg))
                obj.FindChildLeaf("LightColorG")?.SetSingle(lg);
            if (float.TryParse(PropLightB.Text, out float lb))
                obj.FindChildLeaf("LightColorB")?.SetSingle(lb);
        }

        // Apply OData
        if (ChkObjOData.IsChecked == true)
        {
            if (float.TryParse(PropOData1.Text, out float o1))
                obj.FindChildLeaf("OData1")?.SetSingle(o1);
            if (float.TryParse(PropOData2.Text, out float o2))
                obj.FindChildLeaf("OData2")?.SetSingle(o2);
            if (float.TryParse(PropOData3.Text, out float o3))
                obj.FindChildLeaf("OData3")?.SetSingle(o3);
        }

        // Apply Herd markers
        if (ChkObjHerdMarkers.IsChecked == true)
        {
            if (int.TryParse(PropHerdNum.Text, out int nm))
            {
                var leaf = obj.FindChildLeaf("NumMarkers");
                if (leaf != null) leaf.Int32Value = nm;
            }
            if (int.TryParse(PropHerdMarkerType.Text, out int mt))
            {
                var leaf = obj.FindChildLeaf("MarkerType");
                if (leaf != null) leaf.Int32Value = mt;
            }
            if (int.TryParse(PropHerdShowRadius.Text, out int sr))
            {
                var leaf = obj.FindChildLeaf("ShowRadius");
                if (leaf != null) leaf.Int32Value = sr;
            }
        }

        // Apply Herd type (object type ID)
        if (ChkObjHerdType.IsChecked == true)
        {
            var parsedHerd = ObjectNames.ParseInput(PropHerdType.Text ?? "");
            if (parsedHerd.HasValue)
            {
                var leaf = obj.FindChildLeaf("HerdType");
                if (leaf != null) leaf.Int32Value = parsedHerd.Value;
                PropHerdType.Text = ObjectNames.GetDisplayName(parsedHerd.Value);
            }
        }

        // Apply Herd count
        if (ChkObjHerdCount.IsChecked == true)
        {
            if (byte.TryParse(PropTeamCount.Text, out byte tc))
            {
                var leaf = obj.FindChildLeaf("TeamCount");
                if (leaf != null) leaf.ByteValue = tc;
            }
        }

        // Apply Spline key time
        if (ChkObjSplineKeyTime.IsChecked == true && int.TryParse(PropSplineKeyTime.Text, out int kt))
        {
            var leaf = obj.FindChildLeaf("KeyTime");
            if (leaf != null) leaf.Int32Value = kt;
        }

        // Apply Spline scale
        if (ChkObjSplineScale.IsChecked == true)
        {
            if (float.TryParse(PropSplineScaleIn.Text, out float si))
                obj.FindChildLeaf("InScale")?.SetSingle(si);
            if (float.TryParse(PropSplineScaleOut.Text, out float so))
                obj.FindChildLeaf("OutScale")?.SetSingle(so);
        }

        // Apply Spline start ID
        if (ChkObjSplineStartId.IsChecked == true && byte.TryParse(PropSplineStartId.Text, out byte sid))
        {
            var leaf = obj.FindChildLeaf("StartId");
            if (leaf != null) leaf.ByteValue = sid;
        }

        if (typeChanged && _drawRealObjects)
        {
            var objects = _vm.Document.GetObjectInstances();
            Viewport.QueueGlAction(renderer => _modelManager.PreloadModels(objects, renderer));
        }

        // Light objects affect terrain shading — rebuild render data
        int currentType = obj.FindChildLeaf("Type")?.Int32Value ?? 0;
        if (currentType == ObjectTypeLightDirectional)
            UploadTerrainToGpu();

        InvalidateViewport();
        StatusText.Text = "Object properties applied";
    }

    private void ToggleOptionalLeaf(string name, bool enabled, TextBox textBox, float defaultValue)
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (enabled)
        {
            obj.AddSingle(name, defaultValue);
            textBox.Text = defaultValue.ToString("F2");
        }
        else
        {
            var leaf = obj.FindChildLeaf(name);
            if (leaf != null) obj.RemoveLeaf(leaf);
        }
        InvalidateViewport();
    }

    private void ToggleOptionalByteLeaf(string name, bool enabled, TextBox textBox, byte defaultValue)
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (enabled)
        {
            obj.AddByte(name, defaultValue);
            textBox.Text = defaultValue.ToString();
        }
        else
        {
            var leaf = obj.FindChildLeaf(name);
            if (leaf != null) obj.RemoveLeaf(leaf);
        }
        InvalidateViewport();
    }

    private void ToggleOptionalInt32Leaf(string name, bool enabled, TextBox textBox, int defaultValue)
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (enabled)
        {
            obj.AddInt32(name, defaultValue);
            textBox.Text = defaultValue.ToString();
        }
        else
        {
            var leaf = obj.FindChildLeaf(name);
            if (leaf != null) obj.RemoveLeaf(leaf);
        }
        InvalidateViewport();
    }

    private void ToggleTilt()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjTilt.IsChecked == true;
        PanelTilt.IsVisible = enable;
        PropObjTiltFwd.IsEnabled = enable;
        PropObjTiltLeft.IsEnabled = enable;

        if (enable)
        {
            obj.AddSingle("TiltForward", 0);
            obj.AddSingle("TiltLeft", 0);
            PropObjTiltFwd.Text = "0.00";
            PropObjTiltLeft.Text = "0.00";
        }
        else
        {
            var fwd = obj.FindChildLeaf("TiltForward");
            if (fwd != null) obj.RemoveLeaf(fwd);
            var left = obj.FindChildLeaf("TiltLeft");
            if (left != null) obj.RemoveLeaf(left);
        }

        InvalidateViewport();
    }

    private void ToggleLightColor()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjLightColor.IsChecked == true;
        PanelLightColor.IsVisible = enable;

        if (enable)
        {
            obj.AddSingle("LightColorR", 0);
            obj.AddSingle("LightColorG", 0);
            obj.AddSingle("LightColorB", 0);
            PropLightR.Text = "0.0000";
            PropLightG.Text = "0.0000";
            PropLightB.Text = "0.0000";
        }
        else
        {
            foreach (var n in new[] { "LightColorR", "LightColorG", "LightColorB" })
            {
                var leaf = obj.FindChildLeaf(n);
                if (leaf != null) obj.RemoveLeaf(leaf);
            }
        }

        InvalidateViewport();
    }

    private void ToggleOData()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjOData.IsChecked == true;
        PanelOData.IsVisible = enable;

        if (enable)
        {
            obj.AddSingle("OData1", 0);
            obj.AddSingle("OData2", 0);
            obj.AddSingle("OData3", 0);
            PropOData1.Text = "0.0000";
            PropOData2.Text = "0.0000";
            PropOData3.Text = "0.0000";
        }
        else
        {
            foreach (var n in new[] { "OData1", "OData2", "OData3" })
            {
                var leaf = obj.FindChildLeaf(n);
                if (leaf != null) obj.RemoveLeaf(leaf);
            }
        }

        InvalidateViewport();
    }

    private void ToggleHerdMarkers()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjHerdMarkers.IsChecked == true;
        PanelHerdMarkers.IsVisible = enable;

        if (enable)
        {
            obj.AddInt32("NumMarkers", 0);
            obj.AddInt32("MarkerType", 0);
            obj.AddInt32("ShowRadius", 0);
            PropHerdNum.Text = "0";
            PropHerdMarkerType.Text = "0";
            PropHerdShowRadius.Text = "0";
        }
        else
        {
            foreach (var n in new[] { "NumMarkers", "MarkerType", "ShowRadius" })
            {
                var leaf = obj.FindChildLeaf(n);
                if (leaf != null) obj.RemoveLeaf(leaf);
            }
        }

        InvalidateViewport();
    }

    private void ToggleHerdCount()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjHerdCount.IsChecked == true;
        PanelHerdCount.IsVisible = enable;

        if (enable)
        {
            obj.AddByte("TeamCount", 0);
            obj.AddByte("ShowPath", 0);
            PropTeamCount.Text = "0";
        }
        else
        {
            foreach (var n in new[] { "TeamCount", "ShowPath" })
            {
                var leaf = obj.FindChildLeaf(n);
                if (leaf != null) obj.RemoveLeaf(leaf);
            }
        }

        InvalidateViewport();
    }

    private void ToggleSplinePath3D()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (ChkObjSplinePath3D.IsChecked == true)
        {
            if (obj.FindChildNode("SplinePath3D") == null)
                obj.AddNode("SplinePath3D");
        }
        else
        {
            var node = obj.FindChildNode("SplinePath3D");
            if (node != null) obj.RemoveNode(node);
        }

        InvalidateViewport();
    }

    private void ToggleSplineScale()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjSplineScale.IsChecked == true;
        PanelSplineScale.IsVisible = enable;

        if (enable)
        {
            obj.AddSingle("InScale", 1);
            obj.AddSingle("OutScale", 1);
            PropSplineScaleIn.Text = "1.0000";
            PropSplineScaleOut.Text = "1.0000";
        }
        else
        {
            foreach (var n in new[] { "InScale", "OutScale" })
            {
                var leaf = obj.FindChildLeaf(n);
                if (leaf != null) obj.RemoveLeaf(leaf);
            }
        }

        InvalidateViewport();
    }

    #endregion
}
