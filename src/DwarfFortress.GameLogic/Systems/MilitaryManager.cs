using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Entities;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Systems;

// ── Data ────────────────────────────────────────────────────────────────────

public sealed class Squad
{
    public int         Id        { get; init; }
    public string      Name      { get; set; } = string.Empty;
    public List<int>   MemberIds { get; }      = new();
    public bool        IsOnAlert { get; set; } = false;
    public List<Vec3i> PatrolRoute { get; }    = new();
}

// ── Events ──────────────────────────────────────────────────────────────────

public record struct SquadCreatedEvent (int SquadId, string Name);
public record struct SquadDisbandedEvent(int SquadId);
public record struct AlertChangedEvent  (int SquadId, bool IsOnAlert);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages squads, patrol routes, and alert states.
/// Issues patrol/attack jobs when alerted.
/// Order 16.
/// </summary>
public sealed class MilitaryManager : IGameSystem
{
    public string SystemId    => SystemIds.MilitaryManager;
    public int    UpdateOrder => 16;
    public bool   IsEnabled   { get; set; } = true;

    private readonly Dictionary<int, Squad> _squads = new();
    private int _nextSquadId = 1;

    private GameContext? _ctx;

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        ctx.Commands.Register<CreateSquadCommand>(OnCreateSquad);
        ctx.Commands.Register<DisbandSquadCommand>(OnDisbandSquad);
        ctx.Commands.Register<AssignDwarfToSquadCommand>(OnAssignDwarfToSquad);
        ctx.Commands.Register<ToggleSquadAlertCommand>(OnToggleAlert);
        ctx.Commands.Register<SetPatrolRouteCommand>(OnSetPatrolRoute);
    }

    public void Tick(float delta)
    {
        var registry  = _ctx!.Get<EntityRegistry>();
        var jobSystem = _ctx!.TryGet<Jobs.JobSystem>();
        if (jobSystem is null) return;

        foreach (var squad in _squads.Values.Where(s => s.IsOnAlert))
        {
            foreach (var memberId in squad.MemberIds)
            {
                if (!registry.TryGetById<Dwarf>(memberId, out var dwarf) || dwarf is null) continue;

                // Issue patrol jobs along the route
                if (squad.PatrolRoute.Count == 0) continue;

                bool hasPatrolJob = jobSystem.GetAllJobs()
                    .Any(j => j.JobDefId == Jobs.JobDefIds.Patrol &&
                              j.AssignedDwarfId == memberId &&
                              j.Status == Jobs.JobStatus.InProgress);

                if (!hasPatrolJob)
                {
                    var dwarfPos = dwarf.Components.Get<PositionComponent>().Position;
                    var nextWaypoint = GetNextWaypoint(squad, dwarfPos);
                    jobSystem.CreateJob(Jobs.JobDefIds.Patrol, nextWaypoint, priority: 8);
                }
            }
        }
    }

    public void OnSave(SaveWriter w)
    {
        w.Write("nextSquadId", _nextSquadId);

        var saved = _squads.Values.Select(s => new SquadDto
        {
            Id          = s.Id,
            Name        = s.Name,
            MemberIds   = s.MemberIds.ToList(),
            IsOnAlert   = s.IsOnAlert,
            PatrolRoute = s.PatrolRoute
                          .Select(p => new Vec3iDto { X = p.X, Y = p.Y, Z = p.Z })
                          .ToList(),
        }).ToList();

        w.Write("squads", saved);
    }

    public void OnLoad(SaveReader r)
    {
        _nextSquadId = r.TryRead<int>("nextSquadId");

        var saved = r.TryRead<System.Collections.Generic.List<SquadDto>>("squads");
        if (saved is null) return;

        _squads.Clear();
        foreach (var dto in saved)
        {
            var squad = new Squad
            {
                Id        = dto.Id,
                Name      = dto.Name,
                IsOnAlert = dto.IsOnAlert,
            };
            squad.MemberIds.AddRange(dto.MemberIds);
            squad.PatrolRoute.AddRange(dto.PatrolRoute.Select(p => new Vec3i(p.X, p.Y, p.Z)));
            _squads[squad.Id] = squad;
        }
    }

    // ── Save model ─────────────────────────────────────────────────────────────

    private sealed class SquadDto
    {
        public int         Id          { get; set; }
        public string      Name        { get; set; } = "";
        public System.Collections.Generic.List<int>      MemberIds   { get; set; } = new();
        public bool        IsOnAlert   { get; set; }
        public System.Collections.Generic.List<Vec3iDto> PatrolRoute { get; set; } = new();
    }

    private sealed class Vec3iDto { public int X { get; set; } public int Y { get; set; } public int Z { get; set; } }
    public Squad? GetSquad(int id) => _squads.TryGetValue(id, out var s) ? s : null;

    // ── Private ────────────────────────────────────────────────────────────

    private Vec3i GetNextWaypoint(Squad squad, Vec3i currentPos)
    {
        var closest = squad.PatrolRoute
            .OrderBy(wp => currentPos.ManhattanDistanceTo(wp))
            .First();
        int idx  = squad.PatrolRoute.IndexOf(closest);
        int next = (idx + 1) % squad.PatrolRoute.Count;
        return squad.PatrolRoute[next];
    }

    private void OnCreateSquad(CreateSquadCommand cmd)
    {
        var squad = new Squad { Id = _nextSquadId++, Name = cmd.Name };
        _squads[squad.Id] = squad;
        _ctx!.EventBus.Emit(new SquadCreatedEvent(squad.Id, squad.Name));
    }

    private void OnDisbandSquad(DisbandSquadCommand cmd)
    {
        if (!_squads.Remove(cmd.SquadId)) return;
        _ctx!.EventBus.Emit(new SquadDisbandedEvent(cmd.SquadId));
    }

    private void OnAssignDwarfToSquad(AssignDwarfToSquadCommand cmd)
    {
        if (!_squads.TryGetValue(cmd.SquadId, out var squad)) return;
        if (!squad.MemberIds.Contains(cmd.DwarfId))
            squad.MemberIds.Add(cmd.DwarfId);
    }

    private void OnToggleAlert(ToggleSquadAlertCommand cmd)
    {
        if (!_squads.TryGetValue(cmd.SquadId, out var squad)) return;
        squad.IsOnAlert = cmd.Active;
        _ctx!.EventBus.Emit(new AlertChangedEvent(cmd.SquadId, cmd.Active));
    }

    private void OnSetPatrolRoute(SetPatrolRouteCommand cmd)
    {
        if (!_squads.TryGetValue(cmd.SquadId, out var squad)) return;
        squad.PatrolRoute.Clear();
        squad.PatrolRoute.AddRange(cmd.Waypoints);
    }
}
