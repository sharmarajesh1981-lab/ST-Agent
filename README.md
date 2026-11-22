# ST-Agent

**ST-Agent** is a local-first deterministic workflow and RAG runtime for agent-native engineering.

## Minimal runnable core

This implementation provides:

- .NET 9 ASP.NET Core REST service
- SQLite task persistence
- Ollama model provider
- Background task queue
- Three automatic retries
- Cancel and delete operations
- Built-in web dashboard

## Run

Install [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and [Ollama](https://ollama.com/). Start Ollama and pull a model:

```bash
ollama pull llama3.2
ollama serve
```

Run the service:

```bash
dotnet run
```

Open <http://localhost:5100>.

Set the Ollama URL or SQLite database location with environment variables:

```bash
OLLAMA_URL=http://localhost:11434 ST_AGENT_DB=st-agent.db dotnet run
```

## REST API

- `GET /api/health` — service health
- `POST /api/tasks` — submit a task (`{"prompt":"...","model":"llama3.2"}`)
- `GET /api/tasks` — list tasks
- `GET /api/tasks/{id}` — get a task
- `POST /api/tasks/{id}/cancel` — cancel a task
- `DELETE /api/tasks/{id}` — delete a task

## Example

```bash
curl -X POST http://localhost:5100/api/tasks \
  -H 'Content-Type: application/json' \
  -d '{"prompt":"Explain a REST API in one sentence","model":"llama3.2"}'
``

Future work includes workflow DAGs, scheduling, RAG, named-pipe IPC, prompt registries, and additional providers.
