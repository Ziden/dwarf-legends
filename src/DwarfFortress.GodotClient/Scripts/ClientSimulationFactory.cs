using System;
using System.IO;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using Godot;

public static class ClientSimulationFactory
{
    public static string ResolveDataPath()
    {
        var projectRoot = ProjectSettings.GlobalizePath("res://");
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Debug", "data"),
            Path.Combine(projectRoot, ".godot", "mono", "temp", "bin", "Release", "data"),
            Path.Combine(projectRoot, "..", "..", "data"),
            Path.Combine(projectRoot, "data"),
        }
            .Select(Path.GetFullPath)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(Path.Combine(candidate, "ConfigBundle")))
                return candidate;
        }

        throw new DirectoryNotFoundException(
            "Could not find a valid game data folder containing 'ConfigBundle'. Checked: " +
            string.Join(", ", candidates.Select(path => $"'{path}'")));
    }

    public static GameSimulation CreateSimulation(int seed = 42, int width = 48, int height = 48, int depth = 8)
    {
        var logger = new GodotLogger();
        var dataSource = new FolderDataSource(ResolveDataPath());
        var simulation = GameBootstrapper.Build(logger, dataSource);
        simulation.Context.Commands.Dispatch(new StartFortressCommand(seed, width, height, depth));
        return simulation;
    }
}