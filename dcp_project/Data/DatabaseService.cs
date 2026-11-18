//создание БД, выполнение запросов и сборка DataBox

using System.IO;
using System.Globalization;
using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
using dcp_project.Data.Components;

namespace dcp_project.Data;

public sealed class DatabaseService
{
	private readonly string _connectionString; //строка подключения к БД

	public DatabaseService(string? databasePath = null)
	{
		databasePath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"DesktopSupportPersonality",
			"desktop_support.db");
		string? directory = Path.GetDirectoryName(databasePath);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		_connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			ForeignKeys = true
		}.ToString();
	}
	//открытие подключения
	private async Task<SqliteConnection> OpenConnectionAsync()
	{
		var connection = new SqliteConnection(_connectionString);
		await connection.OpenAsync();
		return connection;
	}

	private static string RequireText(string? value, string FieldName)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException($"{FieldName} не может быть пустым!", FieldName);
		return value.Trim();
	}

	//1. создание таблиц
	public async Task InitializeAsync()
	{
		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = """
            CREATE TABLE IF NOT EXISTS UserTask (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                OtherDescription TEXT,
                DeadLineAtUTC TEXT,
                RemindAtUTC TEXT,
                Priority INTEGER NOT NULL DEFAULT 1
                    CHECK (Priority BETWEEN 1 AND 5),
                Status TEXT NOT NULL DEFAULT 'Pending'
                    CHECK (Status IN ('Pending', 'Done', 'Cancelled')),
                CreatedAtUTC TEXT NOT NULL,
                UpdatedAtUTC TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ScheduleEvent (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                OtherDescription TEXT,
                StartsAtUTC TEXT NOT NULL,
                EndAtUTC TEXT,
                RepeatRule TEXT,
                RemindMinutesTime INTEGER,
                CreatedAtUTC TEXT NOT NULL,
                UpdatedAtUTC TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS UserFactMemory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Fact TEXT NOT NULL,
                Category TEXT,
                Source TEXT NOT NULL DEFAULT 'User'
                    CHECK (Source IN ('User', 'Agent')),
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUTC TEXT NOT NULL,
                UpdatedAtUTC TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SystemPrompt (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Text TEXT NOT NULL,
                Scale TEXT NOT NULL DEFAULT 'Global',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUTC TEXT NOT NULL,
                UpdatedAtUTC TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Settings (
                SettingKey TEXT PRIMARY KEY,
                SettingValue TEXT NOT NULL, 
                UpdatedAtUTC TEXT NOT NULL
            );
            """;
		await command.ExecuteNonQueryAsync();
	}

	//преобразование даты в строку для бД
	private static string ToDatabaseDate(DateTime value)
	{
		DateTime utc = value.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(value, DateTimeKind.Utc)
			: value.ToUniversalTime();
		return utc.ToString("O", CultureInfo.InvariantCulture);
	}

	//обратное в дату
	private static DateTime FromDatabaseDate(string value)
	{
		DateTime date = DateTime.Parse(
			value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind);
		return date.Kind == DateTimeKind.Utc
			? date
			: DateTime.SpecifyKind(date, DateTimeKind.Utc);
	}

	private static object DbDate(DateTime? value) => value.HasValue ? ToDatabaseDate(value.Value) : DBNull.Value;
	private static object DbValue(object? value) =>	value ?? DBNull.Value;

	//добавление параметра (вместо ставки пользовательских данных в запрос)
	private static void AddParameter(SqliteCommand command,	string name, object? value)
	{
		command.Parameters.AddWithValue(name, DbValue(value));
	}

	//2. чтение задач (преобразование в UserTask)
	public async Task<IReadOnlyList<UserTask>> GetTasksAsync()
	{
		var result = new List<UserTask>();

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            SELECT Id, Title, OtherDescription, DeadLineAtUTC, RemindAtUTC, Priority, Status, CreatedAtUTC, UpdatedAtUTC
            FROM UserTask
            ORDER BY Priority ASC, 
                     CASE WHEN DeadLineAtUTC IS NULL THEN 1 ELSE 0 END, 
                     DeadLineAtUTC ASC;
            """;

		await using var reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			result.Add(new UserTask
			{
				Id = reader.GetInt32(0),
				Title = reader.GetString(1),
				OtherDescription = reader.IsDBNull(2) ? null : reader.GetString(2),
				DeadLineAtUTC = reader.IsDBNull(3) ? null : FromDatabaseDate(reader.GetString(3)),
				RemindAtUTC = reader.IsDBNull(4) ? null : FromDatabaseDate(reader.GetString(4)),
				Priority = reader.GetInt32(5),
				Status = Enum.Parse<UserTaskStatus>(reader.GetString(6)),
				CreatedAtUTC = FromDatabaseDate(reader.GetString(7)),
				UpdatedAtUTC = FromDatabaseDate(reader.GetString(8))
			});
		}
		return result.AsReadOnly();
	}

	//3. чтение расписания (преобразование в ScheduleEvent)
	public async Task<IReadOnlyList<ScheduleEvent>> GetScheduleAsync()
	{
		var result = new List<ScheduleEvent>();

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            SELECT Id, Title, OtherDescription, StartsAtUTC, EndAtUTC, RepeatRule, RemindMinutesTime, CreatedAtUTC, UpdatedAtUTC
            FROM ScheduleEvent
            ORDER BY StartsAtUTC ASC;
            """;

		await using var reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			result.Add(new ScheduleEvent
			{
				Id = reader.GetInt32(0),
				Title = reader.GetString(1),
				OtherDescription = reader.IsDBNull(2) ? null : reader.GetString(2),
				StartsAtUTC = FromDatabaseDate(reader.GetString(3)),
				EndAtUTC = reader.IsDBNull(4) ? null : FromDatabaseDate(reader.GetString(4)),
				RepeatRule = reader.IsDBNull(5) ? null : reader.GetString(5),
				RemindMinutesTime = reader.IsDBNull(6) ? null : reader.GetInt32(6),
				CreatedAtUTC = FromDatabaseDate(reader.GetString(7)),
				UpdatedAtUTC = FromDatabaseDate(reader.GetString(8))
			});
		}
		return result.AsReadOnly();
	}

	//4. чтение памяти пользователя (преобразование в UserFactMemory)
	public async Task<IReadOnlyList<UserFactMemory>> GetMemoryAsync()
	{
		var result = new List<UserFactMemory>();

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            SELECT Id, Fact, Category, Source, IsActive, CreatedAtUTC, UpdatedAtUTC
            FROM UserFactMemory
            ORDER BY Id ASC;
            """;

		await using var reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			result.Add(new UserFactMemory
			{
				Id = reader.GetInt32(0),
				Fact = reader.GetString(1),
				Category = reader.IsDBNull(2) ? null : reader.GetString(2),
				Source = Enum.Parse<FactSource>(reader.GetString(3)),
				IsActive = reader.GetInt32(4) != 0,
				CreatedAtUTC = FromDatabaseDate(reader.GetString(5)),
				UpdatedAtUTC = FromDatabaseDate(reader.GetString(6))
			});
		}
		return result.AsReadOnly();
	}

	//5. чтение системных промптов (преобразование в SystemPrompt)
	public async Task<IReadOnlyList<SystemPrompt>> GetPromptsAsync()
	{
		var result = new List<SystemPrompt>();

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            SELECT Id, Title, Text, Scale, SortOrder, IsActive, CreatedAtUTC, UpdatedAtUTC
            FROM SystemPrompt
            ORDER BY SortOrder ASC, Id ASC;
            """;

		await using var reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
		{
			result.Add(new SystemPrompt
			{
				Id = reader.GetInt32(0),
				Title = reader.GetString(1),
				Text = reader.GetString(2),
				Scale = reader.GetString(3),
				SortOrder = reader.GetInt32(4),
				IsActive = reader.GetInt32(5) != 0,
				CreatedAtUTC = FromDatabaseDate(reader.GetString(6)),
				UpdatedAtUTC = FromDatabaseDate(reader.GetString(7))
			});
		}
		return result.AsReadOnly();
	}

	//6. чтение настроек
	public async Task<IReadOnlyDictionary<string, string>> GetSettingsAsync()
	{
		var result = new Dictionary<string, string>();

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = "SELECT SettingKey, SettingValue FROM Settings;";

		await using var reader = await command.ExecuteReaderAsync();

		while (await reader.ReadAsync())
			result[reader.GetString(0)] = reader.GetString(1);
		return new ReadOnlyDictionary<string, string>(result);
	}

	//7. загрузка всех данных в DataBox
	public async Task<DataBox> GetStartupDataAsync()
	{
		await InitializeAsync();
		return new DataBox
		{
			DateTimeLoadedAtUTC = DateTime.UtcNow,
			Settings = await GetSettingsAsync(),
			Tasks = await GetTasksAsync(),
			Schedule = await GetScheduleAsync(),
			Memory = await GetMemoryAsync(),
			Prompts = await GetPromptsAsync()
		};
	}

	//8. добавление задачи
	public async Task<int> AddTaskAsync(UserTask task)
	{
		DateTime now = DateTime.UtcNow;
		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            INSERT INTO UserTask
            (Title, OtherDescription, DeadLineAtUTC, RemindAtUTC,
             Priority, Status, CreatedAtUTC, UpdatedAtUTC)
            VALUES
            (@title, @description, @deadline, @remind,
             @priority, @status, @created, @updated);
            SELECT last_insert_rowid();
            """;

		AddParameter(command, "@title", RequireText(task.Title, nameof(task.Title)));
		AddParameter(command, "@description", task.OtherDescription);
		AddParameter(command, "@deadline", DbDate(task.DeadLineAtUTC));
		AddParameter(command, "@remind", DbDate(task.RemindAtUTC));
		AddParameter(command, "@priority", task.Priority);
		AddParameter(command, "@status", task.Status.ToString());
		AddParameter(command, "@created", ToDatabaseDate(task.CreatedAtUTC == default ? now : task.CreatedAtUTC));
		AddParameter(command, "@updated", ToDatabaseDate(now));

		return Convert.ToInt32(await command.ExecuteScalarAsync());
	}

	//9. изменение задачи
	public async Task<bool> UpdateTaskAsync(UserTask task)
	{
		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            UPDATE UserTask SET
                Title = @title,
                OtherDescription = @description,
                DeadLineAtUTC = @deadline,
                RemindAtUTC = @remind,
                Priority = @priority,
                Status = @status,
                UpdatedAtUTC = @updated
            WHERE Id = @id;
            """;

		AddParameter(command, "@id", task.Id);
		AddParameter(command, "@title", RequireText(task.Title, nameof(task.Title)));
		AddParameter(command, "@description", task.OtherDescription);
		AddParameter(command, "@deadline", DbDate(task.DeadLineAtUTC));
		AddParameter(command, "@remind", DbDate(task.RemindAtUTC));
		AddParameter(command, "@priority", task.Priority);
		AddParameter(command, "@status", task.Status.ToString());
		AddParameter(command, "@updated", ToDatabaseDate(DateTime.UtcNow));

		return await command.ExecuteNonQueryAsync() > 0;
	}

	//10. удаление задачи
	public async Task<bool> DeleteTaskAsync(int id)
	{
		return await DeleteByIdAsync("UserTask", id);
	}

	//11. добавление события расписания
	public async Task<int> AddScheduleEventAsync(ScheduleEvent item)
	{
		DateTime now = DateTime.UtcNow;

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            INSERT INTO ScheduleEvent
            (Title, OtherDescription, StartsAtUTC, EndAtUTC,
             RepeatRule, RemindMinutesTime, CreatedAtUTC, UpdatedAtUTC)
            VALUES
            (@title, @description, @starts, @ends,
             @repeat, @remind, @created, @updated);
            SELECT last_insert_rowid();
            """;

		AddParameter(command, "@title", RequireText(item.Title, nameof(item.Title)));
		AddParameter(command, "@description", item.OtherDescription);
		AddParameter(command, "@starts", ToDatabaseDate(item.StartsAtUTC));
		AddParameter(command, "@ends", DbDate(item.EndAtUTC));
		AddParameter(command, "@repeat", item.RepeatRule);
		AddParameter(command, "@remind", item.RemindMinutesTime);
		AddParameter(command, "@created", ToDatabaseDate(
			item.CreatedAtUTC == default ? now : item.CreatedAtUTC));
		AddParameter(command, "@updated", ToDatabaseDate(now));

		return Convert.ToInt32(await command.ExecuteScalarAsync());
	}

	//12. изменение события расписания
	public async Task<bool> UpdateScheduleEventAsync(ScheduleEvent item)
	{
		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            UPDATE ScheduleEvent SET
                Title = @title,
                OtherDescription = @description,
                StartsAtUTC = @starts,
                EndAtUTC = @ends,
                RepeatRule = @repeat,
                RemindMinutesTime = @remind,
                UpdatedAtUTC = @updated
            WHERE Id = @id;
            """;

		AddParameter(command, "@id", item.Id);
		AddParameter(command, "@title", RequireText(item.Title, nameof(item.Title)));
		AddParameter(command, "@description", item.OtherDescription);
		AddParameter(command, "@starts", ToDatabaseDate(item.StartsAtUTC));
		AddParameter(command, "@ends", DbDate(item.EndAtUTC));
		AddParameter(command, "@repeat", item.RepeatRule);
		AddParameter(command, "@remind", item.RemindMinutesTime);
		AddParameter(command, "@updated", ToDatabaseDate(DateTime.UtcNow));

		return await command.ExecuteNonQueryAsync() > 0;
	}

	//13. удаление события расписания
	public async Task<bool> DeleteScheduleEventAsync(int id)
	{
		return await DeleteByIdAsync("ScheduleEvent", id);
	}

	//14. добавление факта в память
	public async Task<int> AddMemoryAsync(UserFactMemory item)
	{
		DateTime now = DateTime.UtcNow;

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            INSERT INTO UserFactMemory
            (Fact, Category, Source, IsActive, CreatedAtUTC, UpdatedAtUTC)
            VALUES
            (@fact, @category, @source, @active, @created, @updated);
            SELECT last_insert_rowid();
            """;

		AddParameter(command, "@fact", RequireText(item.Fact, nameof(item.Fact)));
		AddParameter(command, "@category", item.Category);
		AddParameter(command, "@source", item.Source.ToString());
		AddParameter(command, "@active", item.IsActive ? 1 : 0);
		AddParameter(command, "@created", ToDatabaseDate(
			item.CreatedAtUTC == default ? now : item.CreatedAtUTC));
		AddParameter(command, "@updated", ToDatabaseDate(now));

		return Convert.ToInt32(await command.ExecuteScalarAsync());
	}

	//15. изменение факта в памяти
	public async Task<bool> UpdateMemoryAsync(UserFactMemory item)
	{
		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            UPDATE UserFactMemory SET
                Fact = @fact,
                Category = @category,
                Source = @source,
                IsActive = @active,
                UpdatedAtUTC = @updated
            WHERE Id = @id;
            """;

		AddParameter(command, "@id", item.Id);
		AddParameter(command, "@fact", RequireText(item.Fact, nameof(item.Fact)));
		AddParameter(command, "@category", item.Category);
		AddParameter(command, "@source", item.Source.ToString());
		AddParameter(command, "@active", item.IsActive ? 1 : 0);
		AddParameter(command, "@updated", ToDatabaseDate(DateTime.UtcNow));

		return await command.ExecuteNonQueryAsync() > 0;
	}

	//16. удаление факта из памяти
	public async Task<bool> DeleteMemoryAsync(int id)
	{
		return await DeleteByIdAsync("UserFactMemory", id);
	}

	//17. добавление системного промпта
	public async Task<int> AddPromptAsync(SystemPrompt item)
	{
		DateTime now = DateTime.UtcNow;

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            INSERT INTO SystemPrompt
            (Title, Text, Scale, SortOrder, IsActive, CreatedAtUTC, UpdatedAtUTC)
            VALUES
            (@title, @text, @Scale, @sort, @active, @created, @updated);
            SELECT last_insert_rowid();
            """;

		AddParameter(command, "@title", RequireText(item.Title, nameof(item.Title)));
		AddParameter(command, "@text", item.Text);
		AddParameter(command, "@Scale", item.Scale);
		AddParameter(command, "@sort", item.SortOrder);
		AddParameter(command, "@active", item.IsActive ? 1 : 0);
		AddParameter(command, "@created", ToDatabaseDate(
			item.CreatedAtUTC == default ? now : item.CreatedAtUTC));
		AddParameter(command, "@updated", ToDatabaseDate(now));

		return Convert.ToInt32(await command.ExecuteScalarAsync());
	}

	//18. изменение системного промпта
	public async Task<bool> UpdatePromptAsync(SystemPrompt item)
	{
		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            UPDATE SystemPrompt SET
                Title = @title,
                Text = @text,
                Scale = @Scale,
                SortOrder = @sort,
                IsActive = @active,
                UpdatedAtUTC = @updated
            WHERE Id = @id;
            """;

		AddParameter(command, "@id", item.Id);
		AddParameter(command, "@title", RequireText(item.Title, nameof(item.Title)));
		AddParameter(command, "@text", item.Text);
		AddParameter(command, "@Scale", item.Scale);
		AddParameter(command, "@sort", item.SortOrder);
		AddParameter(command, "@active", item.IsActive ? 1 : 0);
		AddParameter(command, "@updated", ToDatabaseDate(DateTime.UtcNow));

		return await command.ExecuteNonQueryAsync() > 0;
	}

	//19. удаление системного промпта
	public async Task<bool> DeletePromptAsync(int id)
	{
		return await DeleteByIdAsync("SystemPrompt", id);
	}

	//20. добавление или изменение настройки
	public async Task SetSettingAsync(string key, string value)
	{
		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = """
            INSERT INTO Settings (SettingKey, SettingValue, UpdatedAtUTC)
            VALUES (@key, @value, @updatedtime)
            ON CONFLICT(SettingKey) DO UPDATE SET SettingValue = excluded.SettingValue, UpdatedAtUTC = excluded.UpdatedAtUTC;
            """;

		AddParameter(command, "@key", key);
		AddParameter(command, "@value", value);
		AddParameter(command, "@updatedtime", ToDatabaseDate(DateTime.UtcNow));

		await command.ExecuteNonQueryAsync();
	}

	//21. удаление настройки
	public async Task<bool> DeleteSettingAsync(string key)
	{
		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = "DELETE FROM Settings WHERE SettingKey = @key;";
		AddParameter(command, "@key", key);

		return await command.ExecuteNonQueryAsync() > 0;
	}

	//22. общий внутренний метод удаления по id
	private async Task<bool> DeleteByIdAsync(string table, int id)
	{
		//названия таблиц передаются только из методов этого класса.
		var allowedTables = new HashSet<string>
		{
			"UserTask",
			"ScheduleEvent",
			"UserFactMemory",
			"SystemPrompt"
		};

		if (!allowedTables.Contains(table))
			throw new ArgumentException("Недопустимое имя таблицы!", nameof(table));

		await using var connection = await OpenConnectionAsync();
		await using var command = connection.CreateCommand();

		command.CommandText = $"DELETE FROM {table} WHERE Id = @id;";
		AddParameter(command, "@id", id);

		return await command.ExecuteNonQueryAsync() > 0;
	}
}
