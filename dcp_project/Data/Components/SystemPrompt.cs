namespace dcp_project.Data.Components;

public sealed class SystemPrompt
{
	public int Id { get; init; }
	public string Title { get; init; } = string.Empty; //имя промпта
	public string Text { get; init; } = string.Empty; //содержание промпта
	public string Scale { get; init; } = "Global"; //либо "Global" — ко всему агенту, либо к конкретному модулю по его названию
	public int SortOrder { get; init; } //порядок при сборке итогового промпта
	public bool IsActive { get; init; } = true; //включён ли системный промпт
	public DateTime CreatedAtUTC { get; init; }
	public DateTime UpdatedAtUTC { get; init; }
}