# ST-Agent


Local-first deterministic workflow + RAG runtime for agent-native engineering.

A .NET 9 orchestration service with SQLite WAL persistence, prompt/skill registries, scheduler, and RAG pipeline (fetch → chunk → embed → vector search). Multiple clients connect via REST API and named-pipe IPC: a VS Code extension, a CLI, a Logseq plugin, and a built-in web dashboard. Runs fully local with Ollama or LM Studio; swap to Claude/Codex/Gemini by adding keys.

What makes this different
Local-first, provider-agnostic: works fully offline with Ollama or LM Studio; affinity routing spans local/cloud (Claude/Codex/Gemini) and multiple Ollama hosts.
Service is the product: a self-contained orchestration platform exposing REST + named-pipe IPC. Clients (VS Code, CLI, Logseq, web dashboard) are thin and interchangeable.
Workflow + RAG: queueing/scheduling, DLQ, retries/timeouts, prompt/skill registry/templates, and a built-in RAG pipeline (web fetch/search, chunk, embed, vector search) that feeds orchestrated subtasks.
Workflow engine: DAGs, routers, pause/resume, human approval gates, auditable activity logs—closer to a mini Temporal/Airflow than chat UIs.
Capabilities
Task orchestration with queueing, scheduler, DLQ, persistence, retry/timeout policies.
Workflow engine with DAGs/routers/pause/resume/human approval gates, context var substitution ({{var}}), Git-linked activity logging, prompt registry/templates loaded from prompts/, and skill registry loaded from skills/.
RAG pipeline: web fetch/search, text chunking, embeddings, vector search, cache, and safety redaction feeding orchestrated subtasks.
Multi-provider LLM support (Claude, Codex, Gemini, Ollama) with streaming, token counting, structured parsing, and affinity-based model routing across multiple Ollama hosts.
Quality scoring, workflow policy enforcement (protected paths, max steps), audit logging, and run tracing.
Clients: VS Code extension (Active Tasks, History, DLQ, Workflow Explorer, Streaming Output, Diff Approval), CLI (sag), web dashboard at http://localhost:5100, Logseq plugin scaffold.
Extras: 39 skill definitions (skills/), build/deploy/validation PowerShell scripts, comparison groups, configurable concurrency.
Architecture
Service modules (src/SAGIDE.*)
The orchestration service is the core of the system. It is split across 7 projects:

Project	Responsibility
SAGIDE.Service	Host: ASP.NET Core Kestrel, named-pipe server, REST API (12 endpoint groups), web dashboard (wwwroot/)
SAGIDE.Contracts	Interfaces: IPromptRegistry, ISkillRegistry, PromptDefinition, SkillDefinition
SAGIDE.Core	Domain models, DTOs, events
SAGIDE.Workflows	Workflow engine: DAG evaluation, step dispatch, approval gates, loop control, instance store, policy engine, run tracing
SAGIDE.ModelRouter	Capability-based model routing, quality sampling
SAGIDE.Memory	RAG pipeline: WebFetcher, WebSearchAdapter, TextChunker, EmbeddingService, VectorStore, NotesIndexerService
SAGIDE.Security	Bearer-token auth (HMAC constant-time), SQLite audit log
SAGIDE.Observability	OpenTelemetry tracing (ASP.NET Core, HttpClient, OTLP exporters)
SAGIDE.Tools	In-process tool registry for agent tool calls
Service internals (src/SAGIDE.Service/)
SAGIDE.Service/
├── Api/               REST endpoints (12 groups)
│   ├── TaskEndpoints.cs        /api/tasks
│   ├── PromptEndpoints.cs      /api/prompts
│   ├── SkillsEndpoints.cs      /api/skills
│   ├── ResultEndpoints.cs      /api/results
│   ├── ReportsEndpoints.cs     /api/reports
│   ├── NotesEndpoints.cs       /api/notes
│   ├── MetricsEndpoints.cs     /api/metrics
│   ├── MemoryEndpoints.cs      /api/memory
│   └── ...
├── Communication/     Named-pipe IPC
│   ├── NamedPipeServer.cs      Multi-client server with binary framing
│   ├── MessageHandler.cs       15+ message types (submit_task, start_workflow, ...)
│   └── Messages/PipeMessage.cs
├── Orchestrator/      Task + workflow coordination
│   ├── AgentOrchestrator.cs    Routes tasks to providers, manages lifecycle
│   ├── SubtaskCoordinator.cs   Expands prompts into subtasks, dispatches, synthesizes
│   ├── TaskQueue.cs            Priority queue with DLQ
│   ├── QualityScorer.cs        Post-run quality evaluation
│   └── Templates/              Built-in workflow YAML (6 templates)
├── Providers/         LLM backends
│   ├── ProviderFactory.cs      Selects provider by name/modelId
│   ├── ClaudeProvider.cs
│   ├── CodexProvider.cs
│   ├── GeminiProvider.cs
│   ├── OllamaProvider.cs       Multi-host with affinity routing
│   └── OllamaHostHealthMonitor.cs
├── Rag/               RAG pipeline (mirrors SAGIDE.Memory)
├── Persistence/       SQLite repositories (11)
│   ├── SqliteTaskRepository.cs
│   ├── SqliteWorkflowRepository.cs
│   ├── SqliteSchedulerRepository.cs
│   ├── SearchCacheRepository.cs
│   └── ...
├── Scheduling/        Cron-based prompt scheduler
├── Prompts/           Prompt/skill file registry
├── Routing/           Capability-based model routing
├── Resilience/        Retry, timeout, circuit-breaker policies
├── Events/            Event bus (broadcast to pipe clients)
└── wwwroot/           Web dashboard (HTML/JS/CSS)
Clients
Client	Protocol	Source
VS Code extension	Named pipe (IPC)	src/vscode-extension/
CLI (sag)	REST API	tools/cli/sag/
Web dashboard	REST API (built-in)	Served from SAGIDE.Service/wwwroot/ at /
Logseq plugin	REST API	tools/logseq-plugin/
Any HTTP client can consume the REST API; any named-pipe client can subscribe to real-time broadcasts.

Request flow

Why named pipes for VS Code
Keeps the extension host lean; orchestration/state lives in the isolated .NET process with bidirectional IPC and per-client write locks.
Avoids Node event-loop stalls under heavy streaming with concurrent task queue, retry policies, and timeout management.
Works cross-platform; service can be restarted independently of VS Code. Binary framing (4-byte length prefix) ensures message boundary integrity.
Real-time broadcasts (task updates, streaming chunks) reach all connected clients in <0.5ms.
