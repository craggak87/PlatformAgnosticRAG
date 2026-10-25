# Cell Knowledge Assistant (Platform-Agnostic Edition)

[![CI Build & Test Verification](https://github.com/alex-huza/CellKnowledgeAssistant/actions/workflows/ci.yml/badge.svg)](https://github.com/alex-huza/CellKnowledgeAssistant/actions/workflows/ci.yml)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Architecture: Clean Architecture](https://img.shields.io/badge/Architecture-Clean%20Architecture-purple.svg)](4.Cell_Knowledge_Assistant_Domain_Model_Spec.md)
[![Deployment: 100% Offline](https://img.shields.io/badge/Deployment-Air--Gapped%20%2F%20Offline-orange.svg)](1.Cell_Knowledge_Assistant_ADR.md)

Production-grade, offline-first Retrieval-Augmented Generation (RAG) assistant for automated manufacturing cells. Bridges robotic cells (ABB IRC5, Fanuc, KUKA), PLC controllers (Siemens S7-1500, Allen-Bradley GuardLogix), variable frequency drives (PowerFlex 525), vision systems (Cognex), and safety circuits (Pilz) directly with engineering knowledge vaults.

---

## Key Features

- **Strictly Offline & Air-Gapped (ADR-002, ADR-013)**: 100% local inference. Zero cloud dependencies, zero external telemetry, and zero cloud AI SDKs (`Azure.AI.OpenAI`, `OpenAI`, `Anthropic`, `SemanticKernel` are explicitly excluded).
- **Clean Architecture & Hexagonal Ports (ADR-001)**: Domain logic strictly decoupled from infrastructure adapters (`Microsoft.Data.Sqlite`, `Qdrant.Client`, `UglyToad.PdfPig`, `Markdig`, `YamlDotNet`).
- **Hybrid Retrieval (Reciprocal Rank Fusion - RRF) (ADR-004)**: Combines dense vector similarity (`embeddinggemma-2:latest`, 768-dim) with exact industrial identifier BM25/keyword filtering (`ROBOT01`, `FLT-001`, `ALM-205`, `I:0/10`, `E102`).
- **Conservative Hallucination Mitigation (ADR-011)**: When confidence is low or query is out-of-scope, responds strictly with:
  > *"The available documentation does not contain sufficient information."*
- **16-Folder Manufacturing Knowledge Vault (Doc 9)**: Standardized Obsidian Markdown with YAML frontmatter + multi-page binary PDF manuals.
- **Continuous Incremental Ingestion**: Debounced filesystem watcher detecting file creations, modifications, deletions, and renames with SHA-256 cryptographic hash reconciliation.

---

## Architecture Overview

```
                      +---------------------------------------+
                      |       Web / HMI / REST Clients        |
                      +-------------------+-------------------+
                                          |
                                   HTTP / JSON (Port 8080)
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| CellAssistant.Api (ASP.NET Core Minimal API / OpenAPI)                            |
|  - Correlation ID middleware, Global Exception Filter, Streaming SSE               |
+-----------------------------------------+-----------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| CellAssistant.Application (Use Cases & Ports)                                     |
|  - QueryKnowledgeBaseUseCase, StreamQueryUseCase, ReindexVaultUseCase             |
|  - Reciprocal Rank Fusion (RRF, k=60), Confidence Scorer (Doc 4 §7)               |
+-----------------------------------------+-----------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| CellAssistant.Domain (Core Entities & Contracts)                                  |
|  - Document, DocumentChunk, SearchQuery, IndustrialIdentifier, ConfidenceLevel    |
+-----------------------------------------+-----------------------------------------+
                                          |
       +----------------------------------+----------------------------------+
       |                                                                     |
       v                                                                     v
+--------------------------------------+             +--------------------------------------+
| CellAssistant.Infrastructure         |             | CellAssistant.Worker                 |
|  - OllamaChatAdapter (Gemma 4 / Qwen)|             |  - Background Hosted Service         |
|  - OllamaEmbeddingAdapter (768-dim)  |             |  - DebouncedFileWatcherService       |
|  - QdrantVectorStoreAdapter (RRF)    |             |  - DocumentIndexer Pipeline          |
|  - SqliteDocumentRepository (WAL)    |             +--------------------------------------+
|  - MarkdigYamlParser & PdfPigParser  |
+--------------------------------------+
```

---

## 16-Folder Vault Structure (Doc 9 §2)

Located by default at `sample-data/KnowledgeBase/`:

```
KnowledgeBase/
├── Equipment/         # Machine profiles (ROBOT01, CONV01, VFD01, PLC01)
├── Faults/            # Diagnostic guides (FLT-001 Home Position, Robot Gripper)
├── PLC/               # Logic functional descriptions (Conveyor Running Logic)
├── Robot/             # Motion programs & tool data (IRB4600 Motion Subroutines)
├── Vision/            # Camera jobs & lighting guides (CAM01 Inspection)
├── Safety/            # Perimeter & interlock docs (Safety Gate 2)
├── Electrical/        # Panel architectures (Main Power Distribution)
├── Pneumatics/        # FRL and valve manifolds (Pneumatic Main)
├── Mechanical/        # Mechanical assemblies & belts (Infeed Conveyor Mechanical)
├── Maintenance/       # PM protocols & checklists (Preventive Maintenance Robot)
├── Alarms/            # Alarm recovery instructions (ALM-205, ALM-102)
├── Procedures/        # EHS procedures (Lockout Tagout Cell A)
├── Improvements/      # Continuous improvement records (Cycle Time Optimization)
├── Lessons-Learned/   # Engineering root cause analyses (Gripper Sensor Routing)
├── Drawings/          # Schematics & references (E102 Safety, E205 Power)
└── Manuals/           # OEM technical documentation (Valid binary multi-page PDFs)
```

---

## Prerequisites

1. **.NET 9 SDK**: Version 9.0.100 or later (`dotnet --version`).
2. **Local Ollama Daemon**: Version 0.4.0+ installed and running on `http://localhost:11434`.
3. **Local Qdrant Vector Engine**: Version 1.12+ listening on ports 6333 (HTTP) and 6334 (gRPC).

---

## Local Setup & Development

### 1. Clone & Build
```bash
git clone https://github.com/alex-huza/CellKnowledgeAssistant.git
cd CellKnowledgeAssistant/PlatformAgnosticRAG

# Build solution
dotnet build CellKnowledgeAssistant.sln
```

### 2. Pull Required Ollama Models
Ensure Ollama is running, then run the initialization script:
```bash
./deployment/scripts/init-models.sh
```
This pulls:
- `gemma4:e4b-it-q4_K_M` (Primary Chat LLM)
- `embeddinggemma-2:latest` (768-dimension Embedding Model)
- `qwen3.8:latest` (Fallback Chat LLM)

### 3. Start Local Qdrant
Using Docker:
```bash
docker run -d --name qdrant -p 6333:6333 -p 6334:6334 -v $(pwd)/data/qdrant_storage:/qdrant/storage qdrant/qdrant:latest
```
Or run the native Qdrant binary on Windows/macOS.

### 4. Run the Worker & API Services
In terminal 1 (Background Ingestion Worker):
```bash
dotnet run --project src/CellAssistant.Worker/CellAssistant.Worker.csproj
```

In terminal 2 (REST API):
```bash
dotnet run --project src/CellAssistant.Api/CellAssistant.Api.csproj
```
The API is now live at `http://localhost:8080`. Swagger documentation is available at `http://localhost:8080/swagger`.

---

## Docker Compose Deployment

Deploy the entire stack (API, Worker, Qdrant) in one command:

```bash
docker compose up -d --build
```

Check service status and logs:
```bash
docker compose ps
docker compose logs -f api
```

---

## Air-Gapped / Offline Deployment Guide (Doc 8 §8)

For production facilities without internet access:

1. **Pre-stage on Connected Workstation**:
   - Save Docker images:
     ```bash
     docker save -o cell-knowledge-stack.tar qdrant/qdrant:latest cell-assistant-api cell-assistant-worker
     ```
   - Pull and export Ollama models:
     ```bash
     tar -czf ollama-models.tar.gz -C ~/.ollama/models .
     ```

2. **Transfer via Approved Offline Media**:
   - Copy `cell-knowledge-stack.tar` and `ollama-models.tar.gz` to the target industrial PC.

3. **Deploy on Target PC**:
   - Extract Ollama models to `/usr/share/ollama/.ollama/models` (Linux) or `%USERPROFILE%\.ollama\models` (Windows).
   - Load container images: `docker load -i cell-knowledge-stack.tar`.
   - Start the stack using `docker compose up -d`.

---

## Backup & Recovery (Doc 8 §9)

Run the automated backup script to create a point-in-time snapshot of SQLite metadata and Qdrant collections:

```bash
./deployment/scripts/backup.sh
```
Backups are archived into `/opt/cell-knowledge-assistant/backups` with an automated 30-day retention purge.

---

## REST API Endpoint Reference

| HTTP Method | Route | Description |
|:---|:---|:---|
| `POST` | `/api/query` | Synchronous RAG query with confidence score and source citations |
| `POST` | `/api/query/stream` | Server-Sent Events (SSE) streaming token response |
| `GET` | `/api/documents/{id}/file` | Retrieve raw document chunk or source file |
| `GET` | `/api/history` | Paginated search query audit history |
| `GET` | `/api/health` | Service health status (Qdrant, Ollama, SQLite checks) |
| `POST` | `/api/admin/reindex` | Trigger manual vault hash reconciliation & re-indexing |

### Sample Query Request
```bash
curl -X POST http://localhost:8080/api/query \
  -H "Content-Type: application/json" \
  -d '{"question": "What causes gripper timeout on ROBOT01?"}'
```

### Sample Response
```json
{
  "answer": "Gripper timeout on ROBOT01 is primarily caused by cable damage on inputs I:0/15 and I1.15 due to dress pack torsional fatigue at Axis 6, pneumatic valve sticking on the SMC manifold, or magnetic proximity sensor misalignment.",
  "confidence": "High",
  "confidenceScore": 0.88,
  "sources": [
    {
      "fileName": "Robot Gripper.md",
      "folder": "Faults",
      "heading": "Root Cause",
      "pageNumber": 1
    }
  ],
  "durationMs": 142
}
```

---

## Test Verification

Run all test suites:

```bash
# Unit tests
dotnet test tests/CellAssistant.UnitTests/CellAssistant.UnitTests.csproj

# E2E & Tier 1 Feature tests (Includes Feature 25 Vault Verification)
dotnet test tests/CellAssistant.E2ETests/CellAssistant.E2ETests.csproj
```

All 721 unit tests and 365 E2E tests execute and pass 100% offline.

---

## License & Intellectual Property

Copyright © 2026 Alex Huza. Released under the MIT License.
