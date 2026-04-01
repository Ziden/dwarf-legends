using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Data.Defs;
using Godot;

/// <summary>Right-side panel listing all building defs; clicking one enters BuildingPreview mode.</summary>
public partial class BuildMenu : PanelContainer
{
    private VBoxContainer?  _list;
    private InputController? _input;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(300, 0);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left",   8);
        margin.AddThemeConstantOverride("margin_right",  8);
        margin.AddThemeConstantOverride("margin_top",    6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        AddChild(margin);

        var vbox = new VBoxContainer();
        margin.AddChild(vbox);
        vbox.AddChild(new Label { Text = "Build" });
        vbox.AddChild(new HSeparator());

        _list = new VBoxContainer();
        vbox.AddChild(_list);

        // Quick stockpile button
        var spBtn = new Button { Text = "Create Stockpile  [S]", Alignment = HorizontalAlignment.Left };
        spBtn.Pressed += () => _input?.SetMode(InputMode.StockpileZone);
        vbox.AddChild(spBtn);
    }

    public void Setup(InputController input, GameSimulation sim)
    {
        _input = input;

        var dataManager = sim.Context.Get<DataManager>();
        foreach (Node child in _list!.GetChildren()) child.QueueFree();

        foreach (var def in dataManager.Buildings.All())
        {
            var btn = new Button
            {
                Text      = $"{def.DisplayName}  [{(def.IsWorkshop ? "workshop" : "structure")}]",
                Alignment = HorizontalAlignment.Left,
            };
            var capturedId = def.Id;
            btn.Pressed += () =>
            {
                if (_input is null) return;
                _input.PendingBuildingDefId = capturedId;
                _input.SetMode(InputMode.BuildingPreview);
            };
            _list.AddChild(btn);
        }
    }
}
