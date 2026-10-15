using Npgsql;

namespace FocusTracker.Data.Cloud
{
    // Розділ профілю, який Коуч веде про користувача (ключі — як у supabase/functions/assistant/memory.ts)
    public record CoachProfileSection(string Key, string Content);

    // Запис щоденника Коуча: підсумок одного дня
    public record CoachJournalEntry(DateOnly Date, string Summary);

    // Пам'ять Коуча. Таблиці створює хмарна функція при першому запуску; доки її немає — пам'ять порожня.
    public partial class CloudStore
    {
        private const string UndefinedTable = "42P01";

        public async Task<List<CoachProfileSection>> GetCoachProfileAsync(CancellationToken token = default)
        {
            var result = new List<CoachProfileSection>();
            try
            {
                await using var connection = await OpenAsync(token);
                await using var command = new NpgsqlCommand(
                    "select section, content from public.ft_coach_profile;", connection);
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                    result.Add(new CoachProfileSection(reader.GetString(0), reader.GetString(1)));
            }
            catch (PostgresException ex) when (ex.SqlState == UndefinedTable)
            {
            }
            return result;
        }

        public async Task SaveCoachProfileSectionAsync(string key, string content, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand("""
                insert into public.ft_coach_profile (section, content) values (@section, @content)
                on conflict (section) do update set content = excluded.content, updated_at = now();
                """, connection);
            command.Parameters.AddWithValue("section", key);
            command.Parameters.AddWithValue("content", content);
            await command.ExecuteNonQueryAsync(token);
        }

        public async Task<List<CoachJournalEntry>> GetCoachJournalAsync(int days, CancellationToken token = default)
        {
            var result = new List<CoachJournalEntry>();
            try
            {
                await using var connection = await OpenAsync(token);
                await using var command = new NpgsqlCommand(
                    "select date, summary from public.ft_coach_journal order by date desc limit @days;", connection);
                command.Parameters.AddWithValue("days", days);
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                    result.Add(new CoachJournalEntry(reader.GetFieldValue<DateOnly>(0), reader.GetString(1)));
            }
            catch (PostgresException ex) when (ex.SqlState == UndefinedTable)
            {
            }
            return result;
        }

        public async Task SaveCoachJournalAsync(DateOnly date, string summary, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "update public.ft_coach_journal set summary = @summary, updated_at = now() where date = @date;", connection);
            command.Parameters.AddWithValue("date", date);
            command.Parameters.AddWithValue("summary", summary);
            await command.ExecuteNonQueryAsync(token);
        }

        public async Task DeleteCoachJournalAsync(DateOnly date, CancellationToken token = default)
        {
            await using var connection = await OpenAsync(token);
            await using var command = new NpgsqlCommand(
                "delete from public.ft_coach_journal where date = @date;", connection);
            command.Parameters.AddWithValue("date", date);
            await command.ExecuteNonQueryAsync(token);
        }
    }
}
