namespace DwarfFortress.GameLogic.Systems;

public readonly record struct JobRefusedEvent(int DwarfId, int JobId, string ReasonId, string Message);