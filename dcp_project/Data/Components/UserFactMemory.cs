namespace dcp_project.Data.Components;

public enum FactSource
{
    User,
    Agent
}

public sealed class UserFactMemory
{
    public int Id { get; init; }
    public string Fact { get; init; } = string.Empty; //факт о пользователе
    public string? Category {  get; init; } //категория факта ("предпочтения", "обучение", "личность")
    public FactSource Source { get; init; } = FactSource.User; //источник факта
    public bool IsActive { get; init; } = true; //можно отключить факт, не удаляя его
    public DateTime CreatedAtUTC { get; init; }
    public DateTime UpdatedAtUTC { get; init; }
}