namespace Lyubishchev_Time_Management.Models.ViewModels;

public sealed record HistoryViewModel(IReadOnlyList<CategoryOption> Categories);

public sealed record CategoryOption(ulong Id, string Name, string Color);
