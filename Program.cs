using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TaskStore>();
builder.Services.AddSingleton<OllamaClient>();
builder.Services.AddHostedService<TaskWorker>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }));

app.MapPost("/api/tasks", async (CreateTaskRequest request, TaskStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
        return Results.BadRequest(new { error = "Prompt is required." });

    var task = await store.CreateAsync(request.Prompt, request.Model ?? "llama3.2");
    return Results.Accepted($"/api/tasks/{task.Id}", task);
});

app.MapGet("/api/tasks", async (TaskStore store) => Results.Ok(await store.ListAsync()));

app.MapGet("/api/tasks/{id:guid}", async (Guid id, TaskStore store) =>
{
    var task = await store.GetAsync(id);
    return task is null ? Results.NotFound(new { error = "Task not found." }) : Results.Ok(task);
});

app.MapDelete("/api/tasks/{id:guid}", async (Guid id, TaskStore store) =>
    await store.DeleteAsync(id) ? Results.NoContent() : Results.NotFound(new { error = "Task not found." }));

app.MapPost("/api/tasks/{id:guid}/cancel", async (Guid id, TaskStore store) =>
{
    var updated = await store.CancelAsync(id);
    return updated ? Results.Ok(updated) : Results.NotFound(new { error = "Task not found." });
});

await app.RunAsync();

sealed record CreateTaskRequest(string Prompt, string? Model);

sealed record AgentTask
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";
    [JsonPropertyName("model")]
    public string Model { get; init; } = "llama3.2";
    [JsonPropertyName("status")]
    public string Status { get; init; } = "queued";
    [JsonPropertyName("result")]
    public string? Result { get; init; }
    [JsonPropertyName("error")]
    public string? Error { get; init; }
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; init; }
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; }
}

sealed class TaskStore
{
    private readonly string _connectionString;

    public TaskStore(IConfiguration configuration)
    {
        var path = configuration["ST_AGENT_DB"] ?? "st-agent.db";
        _connectionString = $"Data Source={path};";

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                prompt TEXT NOT NULL,
                model TEXT NOT NULL,
                status TEXT NOT NULL,
                result TEXT,
                error TEXT,
                retry_count INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task<AgentTask> CreateAsync(string prompt, string model)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(), Prompt = prompt, Model = model, Status = "queued",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tasks VALUES ($id,$prompt,$model,$status,$result,$error,$retry,$created,$updated)";
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$prompt", task.Prompt);
        command.Parameters.AddWithValue("$model", task.Model);
        command.Parameters.AddWithValue("$status", task.Status);
        command.Parameters.AddWithValue("$result", (object?)task.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)task.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$retry", task.RetryCount);
        command.Parameters.AddWithValue("$created", task.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync();
        return task;
    }

    public async Task<IReadOnlyList<AgentTask>> ListAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM tasks ORDER BY created_at DESC";
        await using var reader = await command.ExecuteReaderAsync();
        var tasks = new List<AgentTask>();
        while (await reader.ReadAsync()) tasks.Add(Read(reader));
        return tasks;
    }

    public async Task<AgentTask?> GetAsync(Guid id)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM tasks WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    public async Task<AgentTask?> GetNextQueuedAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM tasks WHERE status='queued' ORDER BY created_at LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    public async Task UpdateAsync(AgentTask task)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE tasks SET status=$status,result=$result,error=$error,retry_count=$retry,updated_at=$updated WHERE id=$id";
        command.Parameters.AddWithValue("$status", task.Status);
        command.Parameters.AddWithValue("$result", (object?)task.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)task.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$retry", task.RetryCount);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tasks WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<AgentTask?> CancelAsync(Guid id)
    {
        var task = await GetAsync(id);
        if (task is null || task.Status is "completed" or "failed" or "cancelled") return task;
        var cancelled = task with { Status = "cancelled", UpdatedAt = DateTimeOffset.UtcNow };
        await UpdateAsync(cancelled);
        return cancelled;
    }

    private static AgentTask Read(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        Prompt = reader.GetString(reader.GetOrdinal("prompt")),
        Model = reader.GetString(reader.GetOrdinal("model")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        Result = reader.IsDBNull(reader.GetOrdinal("result")) ? null : reader.GetString(reader.GetOrdinal("result")),
        Error = reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error")),
        RetryCount = reader.GetInt32(reader.GetOrdinal("retry_count")),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
    };
}

sealed class OllamaClient
{
    private readonly HttpClient _http = new();
    private readonly string _baseUrl;

    public OllamaClient(IConfiguration configuration)
    {
        _baseUrl = (configuration["OLLAMA_URL"] ?? "http://localhost:11434").TrimEnd('/');
    }

    public async Task<string> GenerateAsync(string model, string prompt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate");
        request.Content = JsonContent.Create(new { model, prompt, stream = false });
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama returned {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("response").GetString() ?? "";
    }
}

sealed class TaskWorker(TaskStore store, OllamaClient ollama, ILogger<TaskWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var task = await store.GetNextQueuedAsync();
            if (task is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            await store.UpdateAsync(task with { Status = "running", UpdatedAt = DateTimeOffset.UtcNow });
            try
            {
                var result = await ollama.GenerateAsync(task.Model, task.Prompt, stoppingToken);
                var latest = await store.GetAsync(task.Id);
                if (latest?.Status == "cancelled") continue;
                await store.UpdateAsync(task with { Status = "completed", Result = result, Error = null, UpdatedAt = DateTimeOffset.UtcNow });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Task {TaskId} failed", task.Id);
                var retryCount = task.RetryCount + 1;
                await store.UpdateAsync(task with
                {
                    Status = retryCount < 3 ? "queued" : "failed",
                    Error = exception.Message,
                    RetryCount = retryCount,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                if (retryCount < 3) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
