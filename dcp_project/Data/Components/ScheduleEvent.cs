namespace dcp_project.Data.Components;

public sealed class ScheduleEvent
{
    public int Id { get; init; }
    public string Title { get; init; }
    public string? OtherDescription { get; init; }
    public DateTime StartsAtUTS { get; init; }  //время начала
    public DateTime? EndAtUTC { get; init; } //время окончания
    public string? RepeatRule { get; init; } //правило для повторения (например: "каждый день", если null — не повторяется)
    public int? RemindMinutesTime { get; init; } //за сколько минут напоминать
    public DateTime CreatedAtUTC { get; init; }
    public DateTime UpdatedAtUTC { get; init; } 
}