namespace dcp_project.Data.Components;

public enum UserTaskStatus
{
	Pending,
	Done,
	Cancelled
}

public sealed class UserTask
{
	public int Id { get; init; } 
	public string Title { get; init; } = string.Empty; //название задачи
	public string? OtherDescription { get; init; } //дополнительное описание
	public DateTime? DeadLineAtUTC { get; init; } //дедлайн задачи
	public DateTime? RemindAtUTC { get; init; } //напоминание
	public int Priority { get; init; } = 1; //приоритет задачи (от 1 до 5, где 1 — очень высокий, 5 — очень низкий)
	public UserTaskStatus Status { get; init; } = UserTaskStatus.Pending; //статус задачи
	public DateTime CreatedAtUTC { get; init; } //время создания
	public DateTime UpdatedAtUTC { get; init; } //время последнего обновления
}