using dcp_project.Data.Components;

namespace dcp_project.Data;

public sealed class DataBox
{
	public DateTime DateTimeLoadedAtUTC { get; init; } //время загрузки
	
	//словарь настроек (язык, тема, громкость плеера, запуск при включении, показ уведомлений, используемая LLM и другие
	public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

	public IReadOnlyList<UserTask> Tasks { get; init; } = Array.Empty<UserTask>(); //список задач у пользователя
	public IReadOnlyList<ScheduleEvent> Schedule { get; init; } = Array.Empty<ScheduleEvent>(); //список запланированных событий
	public IReadOnlyList<UserFactMemory> Memory { get; init; } = Array.Empty<UserFactMemory>(); //записывающая память — факты о пользователе
	public IReadOnlyList<SystemPrompt> Prompts { get; init; } = Array.Empty<SystemPrompt>(); //список системных промптов
	//итоговый системный промпт = Memory + Prompts
}