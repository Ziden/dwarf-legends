using System;
using System.IO;
using DwarfFortress.GameLogic.Core;

namespace DwarfFortress.GameLogic.Systems;

// ── Events ──────────────────────────────────────────────────────────────────

public record struct GameSavedEvent  (string FilePath);
public record struct GameLoadedEvent (string FilePath);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Handles save/load commands: serialises through GameSimulation, 
/// writes to / reads from file via IDataSource.
/// Order 99.
/// </summary>
public sealed class SaveSystem : IGameSystem
{
    public string SystemId    => SystemIds.SaveSystem;
    public int    UpdateOrder => 99;
    public bool   IsEnabled   { get; set; } = true;

    private GameContext?    _ctx;
    private GameSimulation? _sim;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.Commands.Register<SaveGameCommand>(OnSave);
        ctx.Commands.Register<LoadGameCommand>(OnLoad);
    }

    /// <summary>Must be called before the first Tick so SaveSystem can call sim.Save().</summary>
    public void SetSimulation(GameSimulation sim) => _sim = sim;

    public void Tick(float delta) { }
    public void OnSave(SaveWriter w) { }
    public void OnLoad(SaveReader r) { }

    // ── Command handlers ───────────────────────────────────────────────────

    private void OnSave(SaveGameCommand cmd)
    {
        if (_sim is null) { _ctx!.Logger?.Warn("SaveSystem: simulation not set"); return; }

        var json     = _sim.Save();
        var filePath = $"saves/{cmd.SlotName}.json";

        // Write via DataSource if it supports writing; otherwise fall back to File.IO
        if (_ctx!.DataSource is IWritableDataSource writable)
        {
            writable.WriteText(filePath, json);
        }
        else
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, json);
        }

        _ctx.EventBus.Emit(new GameSavedEvent(filePath));
        _ctx.Logger?.Info($"Game saved to: {filePath}");
    }

    private void OnLoad(LoadGameCommand cmd)
    {
        if (_sim is null) { _ctx!.Logger?.Warn("SaveSystem: simulation not set"); return; }

        var filePath = $"saves/{cmd.SlotName}.json";
        string json;
        if (_ctx!.DataSource.Exists(filePath))
        {
            json = _ctx.DataSource.ReadText(filePath);
        }
        else
        {
            _ctx.Logger?.Warn($"SaveSystem: save file not found: {filePath}");
            return;
        }

        _sim.Load(json);
        _ctx.EventBus.Emit(new GameLoadedEvent(filePath));
        _ctx.Logger?.Info($"Game loaded from: {filePath}");
    }
}

// ── Extension interface for writable DataSource ────────────────────────────

/// <summary>Optional extension of IDataSource that supports writing.</summary>
public interface IWritableDataSource : IDataSource
{
    void WriteText(string path, string content);
}
