using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic;
using DwarfFortress.GameLogic.Core;
using Godot;

namespace DwarfFortress.GodotClient.UI;


public partial class DebugWindow : PanelContainer
{
    private const int HistoryLookbackFrames = 300;
    private const int SpikeListCount = 12;
    private const double RefreshIntervalSeconds = 0.25;

    private GameSimulation? _simulation;
    private SimulationProfiler? _profiler;
    private Label? _summaryLabel;
    private ItemList? _frameList;
    private Tree? _systemTree;
    private Tree? _spanTree;
    private long? _selectedFrameSequence;
    private string? _selectedSystemId;
    private double _refreshCountdown;
    private readonly Dictionary<int, long> _frameSequencesByIndex = new();
    private readonly Dictionary<TreeItem, string> _systemIdsByItem = new();

    public Action? OnStoryPressed { get; set; }

    public bool HasProfilerData => _frameList is not null && _frameList.ItemCount > 0;

    public override void _Ready()
    {
        BuildUi();
        HideWindow();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _profiler is null)
            return;

        _refreshCountdown -= delta;
        if (_refreshCountdown > 0d)
            return;

        Refresh();
    }

    public void Setup(GameSimulation simulation)
    {
        _simulation = simulation;
        _profiler = simulation.Profiler;
        _selectedFrameSequence = null;
        _selectedSystemId = null;
        Refresh();
    }

    public void ToggleWindow()
    {
        if (Visible)
        {
            HideWindow();
            return;
        }

        ShowWindow();
    }

    public void ShowWindow()
    {
        Show();
        SetProcess(true);
        Refresh();
    }

    public void HideWindow()
    {
        Hide();
        SetProcess(false);
    }

    public void Refresh()
    {
        _refreshCountdown = RefreshIntervalSeconds;

        if (_summaryLabel is null || _frameList is null || _systemTree is null || _spanTree is null)
            return;

        if (_profiler is null)
        {
            _summaryLabel.Text = "Profiler unavailable.";
            PopulateFrameList(null, Array.Empty<ProfilerFrame>());
            PopulateSystemTree(null, Array.Empty<ProfilerSystemSummary>());
            PopulateSpanTree(null, null);
            return;
        }

        var latestFrame = _profiler.LatestFrame;
        if (latestFrame is null)
        {
            _summaryLabel.Text = "No profiler frames captured yet.";
            PopulateFrameList(null, Array.Empty<ProfilerFrame>());
            PopulateSystemTree(null, Array.Empty<ProfilerSystemSummary>());
            PopulateSpanTree(null, null);
            return;
        }

        var selectedFrame = ResolveSelectedFrame(latestFrame);
        var summaries = _profiler.GetSystemSummaries(HistoryLookbackFrames);

        _summaryLabel.Text =
            $"Latest #{latestFrame.Sequence} {FormatMilliseconds(latestFrame.TotalDurationMs)} across {latestFrame.Systems.Count} systems  |  " +
            $"Viewing #{selectedFrame.Sequence}  |  History {_profiler.FrameCount} ticks";

        PopulateFrameList(latestFrame, BuildFrameCandidates(latestFrame, selectedFrame));
        PopulateSystemTree(selectedFrame, summaries);
        PopulateSpanTree(selectedFrame, _selectedSystemId);
    }

    private void BuildUi()
    {
        var margin = new MarginContainer
        {
            Name = "Margin",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var layout = new VBoxContainer
        {
            Name = "Layout",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddThemeConstantOverride("separation", 8);
        margin.AddChild(layout);

        var header = new HBoxContainer { Name = "Header" };
        header.AddThemeConstantOverride("separation", 8);
        layout.AddChild(header);

        header.AddChild(new Label
        {
            Text = "Debug Console",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });

        var latestButton = new Button
        {
            Text = "Latest",
            TooltipText = "Jump back to the newest profiler frame.",
        };
        latestButton.Pressed += FocusLatestFrame;
        header.AddChild(latestButton);

        var storyButton = new Button
        {
            Name = "StoryButton",
            Text = "Story",
            TooltipText = "Open the story inspector.",
        };
        storyButton.Pressed += () => OnStoryPressed?.Invoke();
        header.AddChild(storyButton);

        var closeButton = new Button
        {
            Text = "Close",
            TooltipText = "Hide the debug console.",
        };
        closeButton.Pressed += HideWindow;
        header.AddChild(closeButton);

        _summaryLabel = new Label
        {
            Name = "SummaryLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        layout.AddChild(_summaryLabel);

        layout.AddChild(new HSeparator());

        var tabs = new TabContainer
        {
            Name = "Tabs",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddChild(tabs);

        var profilerTab = new VBoxContainer
        {
            Name = "Profiler",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        profilerTab.AddThemeConstantOverride("separation", 8);
        tabs.AddChild(profilerTab);

        profilerTab.AddChild(new Label
        {
            Text = "Select a spike frame to inspect which systems consumed the tick and which nested spans dominated inside that system.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var split = new HSplitContainer
        {
            Name = "ProfilerSplit",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        profilerTab.AddChild(split);

        var historyColumn = new VBoxContainer
        {
            Name = "HistoryColumn",
            CustomMinimumSize = new Vector2(240f, 0f),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        historyColumn.AddThemeConstantOverride("separation", 6);
        split.AddChild(historyColumn);

        historyColumn.AddChild(new Label { Text = "Recent Spike Frames" });
        _frameList = new ItemList
        {
            Name = "ProfilerFrameList",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            AllowReselect = true,
        };
        _frameList.ItemSelected += OnFrameSelected;
        historyColumn.AddChild(_frameList);

        var detailSplit = new HSplitContainer
        {
            Name = "DetailSplit",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        split.AddChild(detailSplit);

        var systemsColumn = new VBoxContainer
        {
            Name = "SystemsColumn",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        systemsColumn.AddThemeConstantOverride("separation", 6);
        detailSplit.AddChild(systemsColumn);

        systemsColumn.AddChild(new Label { Text = "Systems In Selected Frame" });
        _systemTree = new Tree
        {
            Name = "ProfilerSystemTree",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Columns = 6,
            HideRoot = true,
            ColumnTitlesVisible = true,
        };
        _systemTree.SetColumnTitle(0, "System");
        _systemTree.SetColumnTitle(1, "Tick");
        _systemTree.SetColumnTitle(2, "Avg");
        _systemTree.SetColumnTitle(3, "Max");
        _systemTree.SetColumnTitle(4, "Share");
        _systemTree.SetColumnTitle(5, "Spikes");
        _systemTree.SetColumnExpand(0, true);
        _systemTree.SetColumnExpand(1, false);
        _systemTree.SetColumnExpand(2, false);
        _systemTree.SetColumnExpand(3, false);
        _systemTree.SetColumnExpand(4, false);
        _systemTree.SetColumnExpand(5, false);
        _systemTree.SetColumnCustomMinimumWidth(0, 220);
        _systemTree.ItemSelected += OnSystemSelected;
        systemsColumn.AddChild(_systemTree);

        var spansColumn = new VBoxContainer
        {
            Name = "SpansColumn",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        spansColumn.AddThemeConstantOverride("separation", 6);
        detailSplit.AddChild(spansColumn);

        spansColumn.AddChild(new Label { Text = "Nested Span Breakdown" });
        _spanTree = new Tree
        {
            Name = "ProfilerSpanTree",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Columns = 2,
            HideRoot = true,
            ColumnTitlesVisible = true,
        };
        _spanTree.SetColumnTitle(0, "Span");
        _spanTree.SetColumnTitle(1, "Duration");
        _spanTree.SetColumnExpand(0, true);
        _spanTree.SetColumnExpand(1, false);
        _spanTree.SetColumnCustomMinimumWidth(0, 240);
        spansColumn.AddChild(_spanTree);
    }

    private void FocusLatestFrame()
    {
        _selectedFrameSequence = _profiler?.LatestFrame?.Sequence;
        Refresh();
    }

    private ProfilerFrame ResolveSelectedFrame(ProfilerFrame latestFrame)
    {
        if (_selectedFrameSequence is long frameSequence)
        {
            var selectedFrame = _profiler?.GetFrame(frameSequence);
            if (selectedFrame is not null)
                return selectedFrame;
        }

        _selectedFrameSequence = latestFrame.Sequence;
        return latestFrame;
    }

    private ProfilerFrame[] BuildFrameCandidates(ProfilerFrame latestFrame, ProfilerFrame selectedFrame)
    {
        var bySequence = new Dictionary<long, ProfilerFrame>
        {
            [latestFrame.Sequence] = latestFrame,
            [selectedFrame.Sequence] = selectedFrame,
        };

        foreach (var frame in _profiler?.GetSlowFrames(HistoryLookbackFrames, SpikeListCount) ?? Array.Empty<ProfilerFrame>())
            bySequence[frame.Sequence] = frame;

        return bySequence.Values
            .OrderByDescending(frame => frame.TotalDurationMs)
            .ThenByDescending(frame => frame.Sequence)
            .ToArray();
    }

    private void PopulateFrameList(ProfilerFrame? latestFrame, IReadOnlyList<ProfilerFrame> frames)
    {
        if (_frameList is null)
            return;

        _frameList.Clear();
        _frameSequencesByIndex.Clear();

        if (frames.Count == 0)
            return;

        var selectedIndex = -1;
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var label = frame.Sequence == latestFrame?.Sequence
                ? $"Latest #{frame.Sequence}  {FormatMilliseconds(frame.TotalDurationMs)}"
                : $"#{frame.Sequence}  {FormatMilliseconds(frame.TotalDurationMs)}";

            var itemIndex = _frameList.AddItem(label);
            _frameList.SetItemTooltip(itemIndex,
                $"Frame #{frame.Sequence}\nTotal tick {FormatMilliseconds(frame.TotalDurationMs)}\nSystems: {frame.Systems.Count}");
            _frameSequencesByIndex[itemIndex] = frame.Sequence;

            if (_selectedFrameSequence == frame.Sequence)
                selectedIndex = itemIndex;
        }

        if (selectedIndex < 0)
        {
            selectedIndex = 0;
            _selectedFrameSequence = _frameSequencesByIndex[selectedIndex];
        }

        _frameList.Select(selectedIndex);
    }

    private void PopulateSystemTree(ProfilerFrame? frame, IReadOnlyList<ProfilerSystemSummary> summaries)
    {
        if (_systemTree is null)
            return;

        _systemTree.Clear();
        _systemIdsByItem.Clear();

        var root = _systemTree.CreateItem();
        if (frame is null)
        {
            CreatePlaceholderItem(_systemTree, root, "No system samples yet.");
            _selectedSystemId = null;
            return;
        }

        var summaryById = summaries.ToDictionary(summary => summary.SystemId, StringComparer.Ordinal);
        var orderedSystems = frame.Systems
            .OrderByDescending(system => system.DurationMs)
            .ThenBy(system => system.UpdateOrder)
            .ToArray();

        if (orderedSystems.Length == 0)
        {
            CreatePlaceholderItem(_systemTree, root, "Frame contains no system timings.");
            _selectedSystemId = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedSystemId) || orderedSystems.All(system => system.SystemId != _selectedSystemId))
            _selectedSystemId = orderedSystems[0].SystemId;

        TreeItem? selectedItem = null;
        foreach (var system in orderedSystems)
        {
            var item = _systemTree.CreateItem(root);
            item.SetText(0, system.SystemId);
            item.SetText(1, FormatMilliseconds(system.DurationMs));

            if (summaryById.TryGetValue(system.SystemId, out var summary))
            {
                item.SetText(2, FormatMilliseconds(summary.AverageDurationMs));
                item.SetText(3, FormatMilliseconds(summary.MaxDurationMs));
                item.SetText(5, summary.SpikeCount.ToString());
            }
            else
            {
                item.SetText(2, "-");
                item.SetText(3, "-");
                item.SetText(5, "0");
            }

            var share = frame.TotalDurationMs <= 0d ? 0d : system.DurationMs / frame.TotalDurationMs;
            item.SetText(4, $"{share * 100d:0.0}%");
            _systemIdsByItem[item] = system.SystemId;

            if (system.SystemId == _selectedSystemId)
                selectedItem = item;
        }

        if (selectedItem is not null)
            _systemTree.SetSelected(selectedItem, 0);
    }

    private void PopulateSpanTree(ProfilerFrame? frame, string? systemId)
    {
        if (_spanTree is null)
            return;

        _spanTree.Clear();
        var root = _spanTree.CreateItem();

        if (frame is null || string.IsNullOrWhiteSpace(systemId))
        {
            CreatePlaceholderItem(_spanTree, root, "Select a system to inspect nested spans.");
            return;
        }

        var system = frame.Systems.FirstOrDefault(sample => sample.SystemId == systemId);
        if (system is null)
        {
            CreatePlaceholderItem(_spanTree, root, "Selected system is missing from this frame.");
            return;
        }

        if (system.Spans.Count == 0)
        {
            CreatePlaceholderItem(_spanTree, root, "No nested spans recorded for this system.");
            return;
        }

        foreach (var span in system.Spans)
            AddSpanItem(root, span);
    }

    private void AddSpanItem(TreeItem parent, ProfilerSpanSample span)
    {
        if (_spanTree is null)
            return;

        var item = _spanTree.CreateItem(parent);
        item.SetText(0, span.Name);
        item.SetText(1, FormatMilliseconds(span.DurationMs));
        item.Collapsed = false;

        foreach (var child in span.Children)
            AddSpanItem(item, child);
    }

    private static void CreatePlaceholderItem(Tree tree, TreeItem root, string message)
    {
        var item = tree.CreateItem(root);
        item.SetText(0, message);
        item.SetText(1, string.Empty);
    }

    private void OnFrameSelected(long index)
    {
        if (!_frameSequencesByIndex.TryGetValue((int)index, out var frameSequence))
            return;

        _selectedFrameSequence = frameSequence;
        Refresh();
    }

    private void OnSystemSelected()
    {
        if (_systemTree?.GetSelected() is not TreeItem selectedItem)
            return;

        if (!_systemIdsByItem.TryGetValue(selectedItem, out var systemId))
            return;

        _selectedSystemId = systemId;
        PopulateSpanTree(_profiler?.GetFrame(_selectedFrameSequence ?? 0), systemId);
    }

    private static string FormatMilliseconds(double durationMs)
        => $"{durationMs:0.###} ms";
}