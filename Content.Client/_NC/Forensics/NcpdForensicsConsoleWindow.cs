using Content.Shared._NC.Forensics;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using System.Numerics;
using Content.Client._NC.CitiNet.UI;
using Content.Shared._NC.CitiNet;
using Robust.Client.UserInterface.XAML;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;

namespace Content.Client._NC.Forensics;

public sealed class NcpdForensicsConsoleWindow : FancyWindow
{
    public event Action<int, NcpdForensicsAlertAction>? OnAlertAction;
    private readonly BoxContainer _list;
    private readonly CitiNetMapControl _map;

    public NcpdForensicsConsoleWindow()
    {
        Title = Loc.GetString("nc-forensics-console-title");
        MinSize = new Vector2(1000f, 650f);
        SetSize = new Vector2(1100f, 700f);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(4) };
        ContentsContainer.AddChild(root);

        // LEFT PANEL: List
        var leftPanel = new PanelContainer { MinWidth = 450, Margin = new Thickness(0, 0, 4, 0) };
        leftPanel.PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#091316"), BorderColor = Color.FromHex("#14606E"), BorderThickness = new Thickness(1) };
        
        var listContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        listContainer.AddChild(new Label { Text = Loc.GetString("nc-forensics-console-header"), FontColorOverride = Color.FromHex("#00E5FF"), HorizontalAlignment = Control.HAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
        
        _list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        var scroll = new ScrollContainer { VerticalExpand = true };
        scroll.AddChild(_list);
        listContainer.AddChild(scroll);
        leftPanel.AddChild(listContainer);
        root.AddChild(leftPanel);

        // RIGHT PANEL: Map
        var rightPanel = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        
        var mapContainer = new PanelContainer { VerticalExpand = true, RectClipContent = true };
        mapContainer.PanelOverride = new StyleBoxFlat { BackgroundColor = Color.Black, BorderColor = Color.FromHex("#14606E"), BorderThickness = new Thickness(1) };
        
        _map = new CitiNetMapControl { HorizontalExpand = true, VerticalExpand = true };
        mapContainer.AddChild(_map);
        rightPanel.AddChild(mapContainer);
        root.AddChild(rightPanel);
    }

    public void UpdateState(NcpdForensicsConsoleBuiState state)
    {
        _list.RemoveAllChildren();
        _map.MapBeacons.Clear();

        var entManager = IoCManager.Resolve<IEntityManager>();

        for (int i = 0; i < state.Alerts.Count; i++)
        {
            var alert = state.Alerts[i];
            if (alert.Archived)
                continue;

            var index = i;
            var rowPanel = new PanelContainer { Margin = new Thickness(2), MinHeight = 80 };
            rowPanel.PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#0b171c"), BorderColor = Color.FromHex("#14606E"), BorderThickness = new Thickness(1) };
            
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
            
            var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            header.AddChild(new Label { Text = alert.Victim.ToUpper(), FontColorOverride = Color.Yellow, HorizontalExpand = true, StyleClasses = { "LabelHeading" } });
            
            var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            var archiveBtn = new Button { Text = Loc.GetString("nc-forensics-console-btn-archive"), MinWidth = 80 };
            var printBtn = new Button { Text = Loc.GetString("nc-forensics-console-btn-print"), MinWidth = 80, Modulate = Color.FromHex("#4DD0E1") };

            archiveBtn.OnPressed += _ => OnAlertAction?.Invoke(index, NcpdForensicsAlertAction.Archive);
            printBtn.OnPressed += _ => OnAlertAction?.Invoke(index, NcpdForensicsAlertAction.PrintTicket);

            actions.AddChild(archiveBtn);
            actions.AddChild(printBtn);
            header.AddChild(actions);

            row.AddChild(header);
            row.AddChild(new Label { Text = Loc.GetString("nc-forensics-console-location", ("loc", alert.Location)), FontColorOverride = Color.FromHex("#4DD0E1") });
            row.AddChild(new Label { Text = Loc.GetString("nc-forensics-console-time", ("time", alert.Time.ToString(@"hh\:mm\:ss"))), FontColorOverride = Color.LightSkyBlue });
            
            rowPanel.AddChild(row);
            _list.AddChild(rowPanel);

            // Add to Map
            if (alert.Coordinates != null)
            {
                var coords = entManager.GetCoordinates(alert.Coordinates.Value);
                if (coords.EntityId.Valid)
                {
                    // Update map to use the correct grid/map
                    if (_map.MapUid == null)
                    {
                        _map.MapUid = coords.EntityId;
                        _map.MapRange = 500f; // Show large area by default
                        _map.ForceNavMapUpdate();
                    }

                    _map.MapBeacons.Add(new CitiNetMapBeaconData(
                        netEnt: NetEntity.Invalid,
                        label: alert.Victim,
                        icon: null,
                        color: Color.Red,
                        localPosition: coords.Position,
                        fontSize: 10
                    ) { IsDead = true });
                }
            }
        }
    }
}
