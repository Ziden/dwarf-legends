using System;
using System.Collections.Generic;
using System.Linq;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.GameLogic.Data;
using DwarfFortress.GameLogic.Entities.Components;

namespace DwarfFortress.GameLogic.Entities;

// ── Events ─────────────────────────────────────────────────────────────────

public record struct EntitySpawnedEvent(int EntityId, string DefId);
public record struct EntityKilledEvent (int EntityId, string Cause);
public record struct EntityMovedEvent  (int EntityId, Vec3i OldPos, Vec3i NewPos);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Owns all live entities. Provides typed queries and unique ID generation.
/// Other systems resolve entities by ID (never hold long-lived references to Entity objects).
/// </summary>
public sealed class EntityRegistry : IGameSystem
{
    // ── IGameSystem ────────────────────────────────────────────────────────
    public string SystemId    => SystemIds.EntityRegistry;
    public int    UpdateOrder => 1;
    public bool   IsEnabled   { get; set; } = true;

    private readonly Dictionary<int, Entity> _entities = new();
    private int _nextId = 1;
    private EventBus? _eventBus;
    private GameContext? _ctx;

    // ── IGameSystem ────────────────────────────────────────────────────────

    public void Initialize(GameContext ctx)
    {
        _ctx = ctx;
        _eventBus = ctx.EventBus;
    }
    public void Tick(float delta) { }

    public void OnSave(SaveWriter w)
    {
        w.Write("nextId", _nextId);

        var dwarves = _entities.Values.OfType<Dwarf>().Select(d =>
        {
            var pos = d.Position.Position;
            return new DwarfDto
            {
                Id           = d.Id,
                FirstName    = d.FirstName,
                NickName     = d.NickName,
                ProfessionId = d.ProfessionId,
                X = pos.X, Y = pos.Y, Z = pos.Z,
                MaxHealth     = d.Health.MaxHealth,
                CurrentHealth = d.Health.CurrentHealth,
                IsConscious   = d.Health.IsConscious,
                Wounds        = d.Health.Wounds.Select(wr => new WoundDto
                {
                    BodyPartId = wr.BodyPartId,
                    Severity   = (int)wr.Severity,
                    IsBleeding = wr.IsBleeding,
                }).ToList(),
                HungerLevel     = d.Needs.Hunger.Level,
                ThirstLevel     = d.Needs.Thirst.Level,
                ThirstZeroSeconds = d.Needs.Thirst.TimeAtZeroSeconds,
                SleepLevel      = d.Needs.Sleep.Level,
                SocialLevel     = d.Needs.Social.Level,
                RecreationLevel = d.Needs.Recreation.Level,
                HairType       = (int)d.Appearance.HairType,
                HairColor      = (int)d.Appearance.HairColor,
                BeardType      = (int)d.Appearance.BeardType,
                BeardColor     = (int)d.Appearance.BeardColor,
                EyeType        = (int)d.Appearance.EyeType,
                NoseType       = (int)d.Appearance.NoseType,
                MouthType      = (int)d.Appearance.MouthType,
                FaceType       = (int)d.Appearance.FaceType,
                Skills = d.Skills.All.Select(kv => new SkillDto
                {
                    SkillId = kv.Key,
                    Level   = kv.Value.Level,
                    Xp      = kv.Value.Xp,
                }).ToList(),
                AttributeLevels = d.Attributes.AllLevels.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value,
                    StringComparer.OrdinalIgnoreCase),
                EnabledLabors = d.Labors.EnabledLabors.ToList(),
                LikedFoodId     = d.Preferences.LikedFoodId,
                LikeStrength    = d.Preferences.LikeStrength,
                DislikedFoodId  = d.Preferences.DislikedFoodId,
                DislikeStrength = d.Preferences.DislikeStrength,
                Provenance = new DwarfProvenanceDto
                {
                    WorldSeed = d.Provenance.WorldSeed,
                    FigureId = d.Provenance.FigureId,
                    HouseholdId = d.Provenance.HouseholdId,
                    CivilizationId = d.Provenance.CivilizationId,
                    OriginSiteId = d.Provenance.OriginSiteId,
                    BirthSiteId = d.Provenance.BirthSiteId,
                    MigrationWaveId = d.Provenance.MigrationWaveId,
                    WorldX = d.Provenance.WorldX,
                    WorldY = d.Provenance.WorldY,
                    RegionX = d.Provenance.RegionX,
                    RegionY = d.Provenance.RegionY,
                },
            };
        }).ToList();

        var creatures = _entities.Values.OfType<Creature>().Select(c =>
        {
            var pos = c.Position.Position;
            return new CreatureDto
            {
                Id            = c.Id,
                DefId         = c.DefId,
                IsHostile     = c.IsHostile,
                X             = pos.X,
                Y             = pos.Y,
                Z             = pos.Z,
                MaxHealth     = c.Health.MaxHealth,
                CurrentHealth = c.Health.CurrentHealth,
                IsConscious   = c.Health.IsConscious,
                HungerLevel   = c.Needs.Hunger.Level,
                HungerZeroSeconds = c.Needs.Hunger.TimeAtZeroSeconds,
                ThirstLevel   = c.Needs.Thirst.Level,
                ThirstZeroSeconds = c.Needs.Thirst.TimeAtZeroSeconds,
                Wounds        = c.Health.Wounds.Select(wr => new WoundDto
                {
                    BodyPartId = wr.BodyPartId,
                    Severity   = (int)wr.Severity,
                    IsBleeding = wr.IsBleeding,
                }).ToList(),
                Substances = c.BodyChemistry.All.Select(kv => new SubstanceDto
                {
                    SubstanceId = kv.Key,
                    Amount      = kv.Value,
                }).ToList(),
                Coatings = c.BodyParts.All
                    .Where(bp => bp.CoatingMaterialId is not null && bp.CoatingAmount > 0f)
                    .Select(bp => new CoatingDto
                    {
                        PartId     = bp.PartId,
                        MaterialId = bp.CoatingMaterialId!,
                        Amount     = bp.CoatingAmount,
                    }).ToList(),
            };
        }).ToList();

        w.Write("dwarves", dwarves);
        w.Write("creatures", creatures);

        var boxes = _entities.Values.OfType<Box>().Select(b =>
        {
            var pos = b.Position.Position;
            return new BoxDto { Id = b.Id, X = pos.X, Y = pos.Y, Z = pos.Z };
        }).ToList();
        w.Write("boxes", boxes);
    }

    public void OnLoad(SaveReader r)
    {
        _nextId = r.TryRead<int>("nextId");

        var dwarves = r.TryRead<List<DwarfDto>>("dwarves");
        if (dwarves is null) return;

        // Remove existing dwarves and creatures before restoring from save
        var dwarfIds    = _entities.Values.OfType<Dwarf>().Select(d => d.Id).ToList();
        var creatureIds = _entities.Values.OfType<Creature>().Select(c => c.Id).ToList();
        foreach (var id in dwarfIds)    _entities.Remove(id);
        foreach (var id in creatureIds) _entities.Remove(id);

        foreach (var dto in dwarves)
        {
            var spawnPos = new Vec3i(dto.X, dto.Y, dto.Z);
            var dwarf    = new Dwarf(dto.Id, dto.FirstName, spawnPos, dto.MaxHealth);
            dwarf.ApplyBaseStats(_ctx?.TryGet<DataManager>()?.Creatures.GetOrNull(DefIds.Dwarf));
            dwarf.NickName     = dto.NickName;
            dwarf.ProfessionId = dto.ProfessionId;

            // Restore health
            dwarf.Health.Restore(dto.CurrentHealth, dto.IsConscious);
            foreach (var wd in dto.Wounds)
                dwarf.Health.AddWound(new Wound(wd.BodyPartId, (WoundSeverity)wd.Severity, wd.IsBleeding));

            // Restore needs
            dwarf.Needs.Hunger.SetLevel(dto.HungerLevel);
            dwarf.Needs.Thirst.SetLevel(dto.ThirstLevel, dto.ThirstZeroSeconds);
            dwarf.Needs.Sleep.SetLevel(dto.SleepLevel);
            dwarf.Needs.Social.SetLevel(dto.SocialLevel);
            dwarf.Needs.Recreation.SetLevel(dto.RecreationLevel);
            dwarf.Appearance.HairType = (DwarfHairType)dto.HairType;
            dwarf.Appearance.HairColor = (DwarfHairColor)dto.HairColor;
            dwarf.Appearance.BeardType = (DwarfBeardType)dto.BeardType;
            dwarf.Appearance.BeardColor = (DwarfHairColor)dto.BeardColor;
            dwarf.Appearance.EyeType = (DwarfEyeType)dto.EyeType;
            dwarf.Appearance.NoseType = (DwarfNoseType)dto.NoseType;
            dwarf.Appearance.MouthType = (DwarfMouthType)dto.MouthType;
            dwarf.Appearance.FaceType = (DwarfFaceType)dto.FaceType;

            // Restore skills
            foreach (var sk in dto.Skills)
                dwarf.Skills.RestoreSkill(sk.SkillId, sk.Level, sk.Xp);

            dwarf.Attributes.SetLevels(dto.AttributeLevels);

            // Restore labors (override the default-all-on set by Dwarf ctor)
            dwarf.Labors.DisableAll();
            dwarf.Labors.EnableAll(dto.EnabledLabors);

            // Restore food preferences
            dwarf.Preferences.LikedFoodId     = dto.LikedFoodId;
            dwarf.Preferences.LikeStrength    = dto.LikeStrength;
            dwarf.Preferences.DislikedFoodId  = dto.DislikedFoodId;
            dwarf.Preferences.DislikeStrength = dto.DislikeStrength;

            if (dto.Provenance is not null)
            {
                dwarf.Provenance.WorldSeed = dto.Provenance.WorldSeed;
                dwarf.Provenance.FigureId = dto.Provenance.FigureId;
                dwarf.Provenance.HouseholdId = dto.Provenance.HouseholdId;
                dwarf.Provenance.CivilizationId = dto.Provenance.CivilizationId;
                dwarf.Provenance.OriginSiteId = dto.Provenance.OriginSiteId;
                dwarf.Provenance.BirthSiteId = dto.Provenance.BirthSiteId;
                dwarf.Provenance.MigrationWaveId = dto.Provenance.MigrationWaveId;
                dwarf.Provenance.WorldX = dto.Provenance.WorldX;
                dwarf.Provenance.WorldY = dto.Provenance.WorldY;
                dwarf.Provenance.RegionX = dto.Provenance.RegionX;
                dwarf.Provenance.RegionY = dto.Provenance.RegionY;
            }

            // Register without re-emitting EntitySpawnedEvent
            _entities[dwarf.Id] = dwarf;
        }

        var creatures = r.TryRead<List<CreatureDto>>("creatures");
        if (creatures is null) return;

        foreach (var dto in creatures)
        {
            var spawnPos = new Vec3i(dto.X, dto.Y, dto.Z);
            var creature = new Creature(dto.Id, dto.DefId, spawnPos, dto.MaxHealth, dto.IsHostile);
            creature.ApplyBaseStats(_ctx?.TryGet<DataManager>()?.Creatures.GetOrNull(dto.DefId));

            creature.Health.Restore(dto.CurrentHealth, dto.IsConscious);
            creature.Needs.Hunger.SetLevel(dto.HungerLevel, dto.HungerZeroSeconds);
            creature.Needs.Thirst.SetLevel(dto.ThirstLevel, dto.ThirstZeroSeconds);
            foreach (var wd in dto.Wounds)
                creature.Health.AddWound(new Wound(wd.BodyPartId, (WoundSeverity)wd.Severity, wd.IsBleeding));

            foreach (var substance in dto.Substances)
                creature.BodyChemistry.AddSubstance(substance.SubstanceId, substance.Amount);

            foreach (var coating in dto.Coatings)
            {
                var part = creature.BodyParts.GetOrCreate(coating.PartId);
                part.CoatingMaterialId = coating.MaterialId;
                part.CoatingAmount     = coating.Amount;
            }

            _entities[creature.Id] = creature;
        }

        var boxes = r.TryRead<List<BoxDto>>("boxes");
        if (boxes is not null)
        {
            var existingBoxIds = _entities.Values.OfType<Box>().Select(b => b.Id).ToList();
            foreach (var id in existingBoxIds) _entities.Remove(id);

            foreach (var dto in boxes)
                _entities[dto.Id] = new Box(dto.Id, new Vec3i(dto.X, dto.Y, dto.Z));
        }
    }

    // ── Save DTOs ──────────────────────────────────────────────────────────

    private sealed class DwarfDto
    {
        public int    Id           { get; set; }
        public string FirstName    { get; set; } = "";
        public string NickName     { get; set; } = "";
        public string ProfessionId { get; set; } = ProfessionIds.Peasant;
        public int    X            { get; set; }
        public int    Y            { get; set; }
        public int    Z            { get; set; }
        public float  MaxHealth     { get; set; } = 100f;
        public float  CurrentHealth { get; set; } = 100f;
        public bool   IsConscious   { get; set; } = true;
        public List<WoundDto>  Wounds        { get; set; } = new();
        public float  HungerLevel     { get; set; } = 1f;
        public float  ThirstLevel     { get; set; } = 1f;
        public float  ThirstZeroSeconds { get; set; }
        public float  SleepLevel      { get; set; } = 1f;
        public float  SocialLevel     { get; set; } = 1f;
        public float  RecreationLevel { get; set; } = 1f;
        public int    HairType        { get; set; }
        public int    HairColor       { get; set; }
        public int    BeardType       { get; set; }
        public int    BeardColor      { get; set; }
        public int    EyeType         { get; set; }
        public int    NoseType        { get; set; }
        public int    MouthType       { get; set; }
        public int    FaceType        { get; set; }
        public List<SkillDto>  Skills        { get; set; } = new();
        public Dictionary<string, int> AttributeLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string>    EnabledLabors { get; set; } = new();
        public string? LikedFoodId     { get; set; }
        public byte    LikeStrength    { get; set; }
        public string? DislikedFoodId  { get; set; }
        public byte    DislikeStrength { get; set; }
        public DwarfProvenanceDto? Provenance { get; set; }
    }

    private sealed class DwarfProvenanceDto
    {
        public int WorldSeed { get; set; }
        public string? FigureId { get; set; }
        public string? HouseholdId { get; set; }
        public string? CivilizationId { get; set; }
        public string? OriginSiteId { get; set; }
        public string? BirthSiteId { get; set; }
        public string? MigrationWaveId { get; set; }
        public int? WorldX { get; set; }
        public int? WorldY { get; set; }
        public int? RegionX { get; set; }
        public int? RegionY { get; set; }
    }

    private sealed class WoundDto
    {
        public string BodyPartId { get; set; } = "";
        public int    Severity   { get; set; }
        public bool   IsBleeding { get; set; }
    }

    private sealed class SkillDto
    {
        public string SkillId { get; set; } = "";
        public int    Level   { get; set; }
        public float  Xp      { get; set; }
    }

    private sealed class CreatureDto
    {
        public int    Id            { get; set; }
        public string DefId         { get; set; } = "";
        public bool   IsHostile     { get; set; }
        public int    X             { get; set; }
        public int    Y             { get; set; }
        public int    Z             { get; set; }
        public float  MaxHealth     { get; set; }
        public float  CurrentHealth { get; set; }
        public bool   IsConscious   { get; set; }
        public float  HungerLevel   { get; set; } = 1f;
        public float  HungerZeroSeconds { get; set; }
        public float  ThirstLevel   { get; set; } = 1f;
        public float  ThirstZeroSeconds { get; set; }
        public List<WoundDto>     Wounds     { get; set; } = new();
        public List<SubstanceDto> Substances { get; set; } = new();
        public List<CoatingDto>   Coatings   { get; set; } = new();
    }

    private sealed class SubstanceDto
    {
        public string SubstanceId { get; set; } = "";
        public float  Amount      { get; set; }
    }

    private sealed class CoatingDto
    {
        public string PartId     { get; set; } = "";
        public string MaterialId { get; set; } = "";
        public float  Amount     { get; set; }
    }

    private sealed class BoxDto
    {
        public int Id { get; set; }
        public int X  { get; set; }
        public int Y  { get; set; }
        public int Z  { get; set; }
    }

    // ── Entity lifecycle ───────────────────────────────────────────────────

    /// <summary>Generate the next globally unique entity ID.</summary>
    public int NextId() => _nextId++;

    /// <summary>Register a freshly constructed entity.</summary>
    public void Register(Entity entity)
    {
        if (_entities.ContainsKey(entity.Id))
            throw new InvalidOperationException(
                $"[EntityRegistry] Entity ID {entity.Id} already registered.");

        _entities[entity.Id] = entity;
        _eventBus?.Emit(new EntitySpawnedEvent(entity.Id, entity.DefId));
    }

    /// <summary>Kill an entity and emit EntityKilledEvent.</summary>
    public void Kill(int entityId, string cause = "unknown")
    {
        if (!_entities.TryGetValue(entityId, out var entity))
            return;

        if (!entity.IsAlive)
            return;

        entity.Kill();
        _eventBus?.Emit(new EntityKilledEvent(entityId, cause));
    }

    // ── Queries ────────────────────────────────────────────────────────────

    /// <summary>Get entity by ID. Throws if not found.</summary>
    public Entity GetById(int id)
    {
        if (_entities.TryGetValue(id, out var entity))
            return entity;

        throw new KeyNotFoundException($"[EntityRegistry] Entity ID {id} not found.");
    }

    /// <summary>Get entity by ID as a specific type. Throws if not found or wrong type.</summary>
    public T GetById<T>(int id) where T : Entity => (T)GetById(id);

    /// <summary>Try to get an entity; returns null if not found.</summary>
    public Entity? TryGetById(int id)
        => _entities.TryGetValue(id, out var e) ? e : null;

    /// <summary>Try to get an entity as a specific type. Returns false if not found or wrong type.</summary>
    public bool TryGetById<T>(int id, out T? entity) where T : Entity
    {
        entity = _entities.TryGetValue(id, out var e) && e is T typed ? typed : null;
        return entity is not null;
    }

    /// <summary>All currently alive entities of type T.</summary>
    public IEnumerable<T> GetAlive<T>() where T : Entity
    {
        foreach (var entity in _entities.Values)
            if (entity is T typed && typed.IsAlive)
                yield return typed;
    }

    /// <summary>Count of alive entities of type T.</summary>
    public int CountAlive<T>() where T : Entity
    {
        var count = 0;
        foreach (var entity in _entities.Values)
            if (entity is T typed && typed.IsAlive)
                count++;

        return count;
    }

    /// <summary>All entities (including dead) of type T.</summary>
    public IEnumerable<T> GetAll<T>() where T : Entity
        => _entities.Values.OfType<T>();

    public int TotalCount => _entities.Count;
}
