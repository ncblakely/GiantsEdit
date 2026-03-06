using System.Globalization;
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

                var addedInc = _vm.Document.EnsureIncludeFile(_objectCatalog, parsedType.Value);
                if (addedInc != null)
                    StatusText.Text = $"Auto-added include file: {addedInc}";
            }
            PropObjType.Text = ObjectNames.GetDisplayName(parsedType.Value);
            PropHeader.Text = $"Object: {PropObjType.Text}";
        }

        if (float.TryParse(PropObjX.Text, out float x))
            obj.SetOrAddSingle("X", x);
        if (float.TryParse(PropObjY.Text, out float y))
            obj.SetOrAddSingle("Y", y);
        if (float.TryParse(PropObjZ.Text, out float z))
            obj.SetOrAddSingle("Z", z);
        if (float.TryParse(PropObjAngle.Text, out float angle))
            obj.SetOrAddSingle("DirFacing", angle);
        if (ChkObjTilt.IsChecked == true)
        {
            if (float.TryParse(PropObjTiltFwd.Text, out float tf))
                obj.SetOrAddSingle("TiltForward", tf);
            if (float.TryParse(PropObjTiltLeft.Text, out float tl))
                obj.SetOrAddSingle("TiltLeft", tl);
        }

        // Apply optional fields only if their checkbox is checked
        if (ChkObjScale.IsChecked == true && float.TryParse(PropObjScale.Text, out float scale))
            obj.SetOrAddSingle("Scale", scale);
        if (ChkObjAIMode.IsChecked == true && byte.TryParse(PropObjAIMode.Text, out byte aiMode))
            obj.SetOrAddByte("AIMode", aiMode);
        if (ChkObjTeamID.IsChecked == true && int.TryParse(PropObjTeamID.Text, out int teamId))
            obj.SetOrAddInt32("TeamID", teamId);

        // Apply Light Color
        if (ChkObjLightColor.IsChecked == true)
        {
            if (float.TryParse(PropLightR.Text, out float lr))
                obj.SetOrAddSingle("LightColorR", lr);
            if (float.TryParse(PropLightG.Text, out float lg))
                obj.SetOrAddSingle("LightColorG", lg);
            if (float.TryParse(PropLightB.Text, out float lb))
                obj.SetOrAddSingle("LightColorB", lb);
        }

        // Apply OData
        if (ChkObjOData.IsChecked == true)
        {
            if (float.TryParse(PropOData1.Text, out float o1))
                obj.SetOrAddSingle("OData1", o1);
            if (float.TryParse(PropOData2.Text, out float o2))
                obj.SetOrAddSingle("OData2", o2);
            if (float.TryParse(PropOData3.Text, out float o3))
                obj.SetOrAddSingle("OData3", o3);
        }

        // Apply Herd markers
        if (ChkObjHerdMarkers.IsChecked == true)
        {
            if (int.TryParse(PropHerdNum.Text, out int nm))
                obj.SetOrAddInt32("NumMarkers", nm);
            if (int.TryParse(PropHerdMarkerType.Text, out int mt))
                obj.SetOrAddInt32("MarkerType", mt);
            if (int.TryParse(PropHerdShowRadius.Text, out int sr))
                obj.SetOrAddInt32("ShowRadius", sr);
        }

        // Apply Herd type (object type ID)
        if (ChkObjHerdType.IsChecked == true)
        {
            var parsedHerd = ObjectNames.ParseInput(PropHerdType.Text ?? "");
            if (parsedHerd.HasValue)
            {
                obj.SetOrAddInt32("HerdType", parsedHerd.Value);
                PropHerdType.Text = ObjectNames.GetDisplayName(parsedHerd.Value);
            }
        }

        // Apply Herd count
        if (ChkObjHerdCount.IsChecked == true)
        {
            if (byte.TryParse(PropTeamCount.Text, out byte tc))
                obj.SetOrAddByte("TeamCount", tc);
        }

        // Apply Spline key time
        if (ChkObjSplineKeyTime.IsChecked == true && int.TryParse(PropSplineKeyTime.Text, out int kt))
            obj.SetOrAddInt32("KeyTime", kt);

        // Apply Spline scale
        if (ChkObjSplineScale.IsChecked == true)
        {
            if (float.TryParse(PropSplineScaleIn.Text, out float si))
                obj.SetOrAddSingle("InScale", si);
            if (float.TryParse(PropSplineScaleOut.Text, out float so))
                obj.SetOrAddSingle("OutScale", so);
        }

        // Apply Spline start ID
        if (ChkObjSplineStartId.IsChecked == true && byte.TryParse(PropSplineStartId.Text, out byte sid))
            obj.SetOrAddByte("StartId", sid);

        // Apply Spline tangents
        if (ChkObjSplineTangents.IsChecked == true)
        {
            if (float.TryParse(PropSplineTanIn.Text, out float ti))
                obj.SetOrAddSingle("InTangent", ti);
            if (float.TryParse(PropSplineTanOut.Text, out float to2))
                obj.SetOrAddSingle("OutTangent", to2);
        }

        // Apply AnimType
        if (ChkObjAnimType.IsChecked == true && byte.TryParse(PropAnimType.Text, out byte at))
            obj.SetOrAddByte("AnimType", at);

        // Apply AnimTime
        if (ChkObjAnimTime.IsChecked == true && float.TryParse(PropAnimTime.Text, out float atm))
            obj.SetOrAddSingle("AnimTime", atm);

        // Apply FlickUsed
        if (ChkObjFlickUsed.IsChecked == true)
            obj.SetOrAddString("FlickUsed", PropFlickUsed.Text ?? "");

        // Apply Path
        if (ChkObjPath.IsChecked == true)
        {
            bool isGround = ChkPathGround.IsChecked == true;
            string nodeName = isGround ? "GroundPath" : "Path";
            var pn = obj.FindChildNode("Path") ?? obj.FindChildNode("GroundPath");
            if (pn != null)
            {
                // If type changed, rename node
                if (pn.Name != nodeName)
                {
                    obj.RemoveNode(pn);
                    pn = obj.AddNode(nodeName);
                    pn.AddString("AnimName", PropPathName.Text ?? "");
                    pn.AddSingle("PathSpeed", 0);
                }
                pn.SetOrAddString("AnimName", PropPathName.Text ?? "");
                if (float.TryParse(PropPathSpeed.Text, out float ps))
                    pn.SetOrAddSingle("PathSpeed", ps);
            }
        }

        // Apply Wind
        if (ChkObjWind.IsChecked == true)
        {
            var wn = obj.FindChildNode("Wind");
            if (wn != null)
            {
                wn.SetOrAddString("AnimName", PropWindName.Text ?? "");
                if (float.TryParse(PropWindSpeed.Text, out float ws))
                    wn.SetOrAddSingle("PathSpeed", ws);
                if (float.TryParse(PropWindDist.Text, out float wd))
                    wn.SetOrAddSingle("Distance", wd);
                if (float.TryParse(PropWindMag.Text, out float wm))
                    wn.SetOrAddSingle("Magnitude", wm);
            }
        }

        // Apply AIData
        if (ChkObjAIData.IsChecked == true)
        {
            ApplyIndexedFloatLeaves(obj, "AIData", PropAIData.Text);
        }

        // MinishopRIcons — applied via icon picker dialog, no action needed here

        // MinishopMIcons — applied via icon picker dialog, no action needed here

        // Apply HerdPoints
        if (ChkObjHerdPoint.IsChecked == true)
        {
            ApplyHerdPoints(obj, PropHerdPoints.Text);
        }

        // Apply Lock refs
        if (PanelLock.IsVisible)
        {
            var lockNode = obj.FindChildNode("Lock");
            if (lockNode != null)
            {
                if (byte.TryParse(PropLockRefSrc.Text, out byte lrs))
                    lockNode.SetOrAddByte("LockRefSrc", lrs);
                if (byte.TryParse(PropLockRefDst.Text, out byte lrd))
                    lockNode.SetOrAddByte("LockRefDst", lrd);
            }
        }

        // Apply SmokeGen fields
        if (PanelSmokeGen.IsVisible)
        {
            ApplySmokeGenFields(obj);
        }

        // Apply AreaAlien fields
        if (PanelAreaAlien.IsVisible)
        {
            ApplyAreaAlienFields(obj);
        }

        if (typeChanged && _drawRealObjects)
        {
            var objects = _vm.Document.GetObjectInstances();
            Viewport.QueueGlAction(renderer => _modelManager.PreloadModels(objects, renderer));
        }

        // Light objects affect terrain shading — rebuild render data
        int currentType = obj.GetInt32("Type");
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
            textBox.Text = defaultValue.ToString("F2", CultureInfo.InvariantCulture);
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
            textBox.Text = defaultValue.ToString(CultureInfo.InvariantCulture);
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
            textBox.Text = defaultValue.ToString(CultureInfo.InvariantCulture);
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

    private void ToggleSplineTangents()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjSplineTangents.IsChecked == true;
        PanelSplineTangents.IsVisible = enable;

        if (enable)
        {
            obj.AddSingle("InTangent", 1);
            obj.AddSingle("OutTangent", 1);
            PropSplineTanIn.Text = "1.0000";
            PropSplineTanOut.Text = "1.0000";
        }
        else
        {
            foreach (var n in new[] { "InTangent", "OutTangent" })
            {
                var leaf = obj.FindChildLeaf(n);
                if (leaf != null) obj.RemoveLeaf(leaf);
            }
        }

        InvalidateViewport();
    }

    private void ToggleSplineJet()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        if (ChkObjSplineJet.IsChecked == true)
        {
            if (obj.FindChildNode("SplineJet") == null)
                obj.AddNode("SplineJet");
        }
        else
        {
            var node = obj.FindChildNode("SplineJet");
            if (node != null) obj.RemoveNode(node);
        }

        InvalidateViewport();
    }

    private void TogglePath()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjPath.IsChecked == true;
        PanelPath.IsVisible = enable;

        if (enable)
        {
            var pn = obj.AddNode("Path");
            pn.AddString("AnimName", "");
            pn.AddSingle("PathSpeed", 1);
            PropPathName.Text = "";
            PropPathSpeed.Text = "1.0000";
            ChkPathGround.IsChecked = false;
        }
        else
        {
            var p = obj.FindChildNode("Path");
            if (p != null) obj.RemoveNode(p);
            var gp = obj.FindChildNode("GroundPath");
            if (gp != null) obj.RemoveNode(gp);
        }

        InvalidateViewport();
    }

    private void ToggleWind()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjWind.IsChecked == true;
        PanelWind.IsVisible = enable;

        if (enable)
        {
            var wn = obj.AddNode("Wind");
            wn.AddString("AnimName", "");
            wn.AddSingle("PathSpeed", 1);
            wn.AddSingle("Distance", 0);
            wn.AddSingle("Magnitude", 0);
            PropWindName.Text = "";
            PropWindSpeed.Text = "1.0000";
            PropWindDist.Text = "0.0000";
            PropWindMag.Text = "0.0000";
        }
        else
        {
            var wn = obj.FindChildNode("Wind");
            if (wn != null) obj.RemoveNode(wn);
        }

        InvalidateViewport();
    }

    private void ToggleFlickUsed()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjFlickUsed.IsChecked == true;
        PropFlickUsed.IsEnabled = enable;

        if (enable)
        {
            obj.AddString("FlickUsed", "");
            PropFlickUsed.Text = "";
        }
        else
        {
            var leaf = obj.FindChildLeaf("FlickUsed");
            if (leaf != null) obj.RemoveLeaf(leaf);
        }

        InvalidateViewport();
    }

    private void ToggleAIData()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjAIData.IsChecked == true;
        PanelAIData.IsVisible = enable;

        if (enable)
        {
            obj.AddSingle("AIData0", 0);
            PropAIData.Text = "0.0000";
        }
        else
        {
            RemoveIndexedLeaves(obj, "AIData");
        }

        InvalidateViewport();
    }

    private void ToggleMinishopR()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjMinishopR.IsChecked == true;
        PanelMinishopR.IsVisible = enable;

        if (enable)
        {
            obj.AddInt32("RIcon0", 0);
            TxtMinishopR.Text = IconNames.GetDisplayName(0);
        }
        else
        {
            RemoveIndexedLeaves(obj, "RIcon");
        }

        InvalidateViewport();
    }

    private void ToggleMinishopM()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjMinishopM.IsChecked == true;
        PanelMinishopM.IsVisible = enable;

        if (enable)
        {
            obj.AddInt32("MIcon0", 0);
            TxtMinishopM.Text = IconNames.GetDisplayName(0);
        }
        else
        {
            RemoveIndexedLeaves(obj, "MIcon");
        }

        InvalidateViewport();
    }

    private void ToggleHerdPoint()
    {
        if (_suppressOptionalLeafToggle) return;
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        bool enable = ChkObjHerdPoint.IsChecked == true;
        PanelHerdPoint.IsVisible = enable;

        if (enable)
        {
            var pt = obj.AddNode("HerdPoint");
            pt.AddSingle("X", 0);
            pt.AddSingle("Y", 0);
            pt.AddSingle("Z", 0);
            PropHerdPoints.Text = "0.00, 0.00, 0.00";
        }
        else
        {
            foreach (var hp in obj.EnumerateNodes().Where(n => n.Name == "HerdPoint").ToList())
                obj.RemoveNode(hp);
        }

        InvalidateViewport();
    }

    #region Helpers for indexed leaves

    private static void RemoveIndexedLeaves(TreeNode obj, string prefix)
    {
        int i = 0;
        while (true)
        {
            var leaf = obj.FindChildLeaf($"{prefix}{i}");
            if (leaf == null) break;
            obj.RemoveLeaf(leaf);
            i++;
        }
    }

    private static void ApplyIndexedFloatLeaves(TreeNode obj, string prefix, string? text)
    {
        // Remove existing
        RemoveIndexedLeaves(obj, prefix);
        if (string.IsNullOrWhiteSpace(text)) return;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (float.TryParse(parts[i], out float v))
                obj.AddSingle($"{prefix}{i}", v);
        }
    }

    private static void ApplyIndexedInt32Leaves(TreeNode obj, string prefix, string? text)
    {
        RemoveIndexedLeaves(obj, prefix);
        if (string.IsNullOrWhiteSpace(text)) return;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out int v))
                obj.AddInt32($"{prefix}{i}", v);
        }
    }

    private static void ApplyHerdPoints(TreeNode obj, string? text)
    {
        // Remove existing herd points
        foreach (var hp in obj.EnumerateNodes().Where(n => n.Name == "HerdPoint").ToList())
            obj.RemoveNode(hp);

        if (string.IsNullOrWhiteSpace(text)) return;
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 3
                && float.TryParse(parts[0], out float hx)
                && float.TryParse(parts[1], out float hy)
                && float.TryParse(parts[2], out float hz))
            {
                var pt = obj.AddNode("HerdPoint");
                pt.AddSingle("X", hx);
                pt.AddSingle("Y", hy);
                pt.AddSingle("Z", hz);
            }
        }
    }

    #endregion

    #region SmokeGen / AreaAlien apply helpers

    private void ApplySmokeGenFields(TreeNode obj)
    {
        ApplyFloat(obj, "StopTimeMin", PropSmkStopMin.Text);
        ApplyFloat(obj, "StopTimeMax", PropSmkStopMax.Text);
        ApplyFloat(obj, "GoTimeMin", PropSmkGoMin.Text);
        ApplyFloat(obj, "GoTimeMax", PropSmkGoMax.Text);
        ApplyFloat(obj, "GenRateMin", PropSmkRateMin.Text);
        ApplyFloat(obj, "GenRateMax", PropSmkRateMax.Text);
        ApplyFloat(obj, "ScaleStart", PropSmkScaleStart.Text);
        ApplyFloat(obj, "ScaleEnd", PropSmkScaleEnd.Text);
        ApplyFloat(obj, "SpeedMin", PropSmkSpeedMin.Text);
        ApplyFloat(obj, "SpeedMax", PropSmkSpeedMax.Text);
        ApplyFloat(obj, "FadeTimeMin", PropSmkFadeMin.Text);
        ApplyFloat(obj, "FadeTimeMax", PropSmkFadeMax.Text);
        ApplyFloat(obj, "WindAngMin", PropSmkWAngMin.Text);
        ApplyFloat(obj, "WindAngMax", PropSmkWAngMax.Text);
        ApplyFloat(obj, "WindAngRate", PropSmkWAngRate.Text);
        ApplyFloat(obj, "WindSpeedMin", PropSmkWSpdMin.Text);
        ApplyFloat(obj, "WindSpeedMax", PropSmkWSpdMax.Text);
        ApplyFloat(obj, "WindSpeedRate", PropSmkWSpdRate.Text);
        ApplyFloat(obj, "White", PropSmkWhite.Text);
    }

    private void ApplyAreaAlienFields(TreeNode obj)
    {
        if (byte.TryParse(PropAreaCount.Text, out byte cnt))
            obj.SetOrAddByte("Count", cnt);
        ApplyFloat(obj, "MinRadius", PropAreaMinRadius.Text);
        ApplyFloat(obj, "MaxRadius", PropAreaMaxRadius.Text);

        if (float.TryParse(PropAreaMinScale.Text, out float mins))
            obj.SetOrAddSingle("MinScale", mins);
        if (float.TryParse(PropAreaMaxScale.Text, out float maxs))
            obj.SetOrAddSingle("MaxScale", maxs);
    }

    private static void ApplyFloat(TreeNode obj, string name, string? text)
    {
        if (float.TryParse(text, out float v))
            obj.SetOrAddSingle(name, v);
    }

    #endregion

    #region Icon picker

    private async void OpenIconPicker(string title, string prefix, TextBlock display,
        IReadOnlyList<(int Id, string Name)>? availableIcons = null)
    {
        var obj = _vm.Document.SelectedObject;
        if (obj == null) return;

        // Collect current icon IDs
        var current = new System.Collections.Generic.List<int>();
        int i = 0;
        while (obj.FindChildLeaf($"{prefix}{i}") is { } leaf) { current.Add(leaf.Int32Value); i++; }

        var dialog = new Dialogs.IconPickerDialog(title, current, availableIcons);
        var result = await dialog.ShowDialog<System.Collections.Generic.List<int>?>(this);
        if (result == null) return;

        // Remove old leaves and write new ones
        RemoveIndexedLeaves(obj, prefix);
        for (int j = 0; j < result.Count; j++)
            obj.AddInt32($"{prefix}{j}", result[j]);

        // Update display
        if (result.Count > 0)
            display.Text = string.Join(", ", result.Select(id => IconNames.GetDisplayName(id)));
        else
            display.Text = "(none)";

        InvalidateViewport();
    }

    #endregion

    #endregion
}
