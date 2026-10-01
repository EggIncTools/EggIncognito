namespace EggIncognito.Services.Predictions.Events;

public sealed record EventOccurrence(string Type, bool Ultra, DateOnly Date, double Start, double Duration);
