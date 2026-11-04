# 🏭 IndustrialTelemetry.Engine

[![.NET Core CI](https://github.com/yancsalmeida/IndustrialTelemetry.Engine/actions/workflows/ci.yml/badge.svg)](https://github.com/yancsalmeida/IndustrialTelemetry.Engine/actions/workflows/ci.yml)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-336791?logo=postgresql)](https://www.postgresql.org/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker)](https://www.docker.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

An enterprise-grade **Industrial Telemetry, Batch Execution & Safety Alarm Management Web API** built with **C# (.NET 8)**, **PostgreSQL**, and strict **Clean Architecture**.

Designed for high-throughput sensor telemetry ingestion in batch manufacturing environments (chemical reactors, semiconductor cleanrooms, pharmaceutical synthesis, and food & beverage), bridging chemical engineering process automation with cloud-native backend software engineering.

---

## 🏗️ Architecture (Clean Architecture & DDD)

The solution is partitioned into modular layers adhering to strict **Dependency Inversion** and **Separation of Concerns**:

```mermaid
flowchart TD
    subgraph Client["SCADA Transmitters / HTTP Clients"]
        HTTP["HTTP Client / Sensor Hub"]
    end

    subgraph API["IndustrialTelemetry.Api (ASP.NET Core Web API)"]
        Endpoints["Controllers (/api/batches, /api/telemetry, /api/alerts)"]
        Middleware["Global Exception Handling Middleware (RFC 7807 ProblemDetails)"]
        Swagger["OpenAPI / Swagger UI Docs"]
    end

    subgraph App["IndustrialTelemetry.Application (Use Cases & CQRS)"]
        Services["Services (BatchService, TelemetryIngestionService, AlertService)"]
        Validators["FluentValidation (Input & Sensor Validation)"]
        Interfaces["Abstractions (IBatchRepository, ITelemetryRepository, IAlertRepository)"]
    end

    subgraph Domain["IndustrialTelemetry.Domain (Core Business Logic - Zero Dependencies)"]
        Entities["Entities (Batch, TelemetryMeasurement, AlertRule, ProcessAlert)"]
        Enums["Enums (BatchStatus, MeasurementUnit, TelemetryQuality, AlertSeverity)"]
        Exceptions["Domain Exceptions (InvalidBatchStateTransitionException)"]
    end

    subgraph Infra["IndustrialTelemetry.Infrastructure (Data Persistence)"]
        EFCore["Entity Framework Core (Npgsql)"]
        Repos["Repository Implementations"]
        DbInit["DbInitializer (Automatic Migrations & Safety Seeding)"]
    end

    subgraph DB["Database"]
        PG[("PostgreSQL 16")]
    end

    subgraph Tests["IndustrialTelemetry.UnitTests (xUnit)"]
        UnitTests["26 Unit & Domain Invariant Tests (Shouldly, NSubstitute)"]
    end

    HTTP --> Endpoints
    Endpoints --> Services
    Services --> Validators
    Services --> Interfaces
    Interfaces -.->|"Implemented by"| Repos
    Repos --> EFCore
    EFCore --> PG
    UnitTests -->|"Verifies"| Domain
    UnitTests -->|"Verifies"| App
```

---

## 🌐 Distributed Industrial Ecosystem (End-to-End Integration)

`IndustrialTelemetry.Engine` serves as the central time-series telemetry archive, safety alarm evaluator, and regulatory batch tracking engine in an interconnected industrial microservices ecosystem paired with [**`ProcessTwin.Py`**](https://github.com/yancsalmeida/ProcessTwin.Py):

```mermaid
flowchart LR
    subgraph Twin["ProcessTwin.Py (Digital Twin Simulator)"]
        Physics["CSTR Reactor & Tank ODEs\n(5 Hz Discrete Integration)"]
        SIS["Safety Instrumented System\n(IEC 61511 Automated Trips)"]
        Bridge["IndustrialTelemetryBridge\n(Async HTTP Client)"]
        HMI["Dark-Mode SCADA HMI\n(HTML5 Canvas & SVG Dashboard)"]
        Physics --> Bridge
        SIS -.->|"Emergency Trip Abort"| Bridge
        Physics --> HMI
    end

    subgraph Engine["IndustrialTelemetry.Engine (ASP.NET Core .NET 8)"]
        Ingest["POST /api/telemetry/ingest\n(High-Throughput Validation)"]
        State["Batch Execution State Machine\n(Scheduled → InProgress → Completed/Aborted)"]
        Rules["Safety Alert Rules Engine\n(TT-101, PT-101, LT-101)"]
        Analytics["Batch KPI Analytics\n(Duration, Mean & Peak Heat)"]
        Ingest --> State
        Ingest --> Rules
        State --> Analytics
    end

    subgraph Storage["Enterprise Persistence"]
        PG[("PostgreSQL 16\nTime-Series Composite Indexes\n(batch_id, timestamp_utc)")]
        Rules --> PG
        State --> PG
        Ingest --> PG
    end

    Bridge == "HTTP Telemetry Stream (1 Hz)" ==> Ingest
    Bridge == "Batch Auto-Negotiation & Aborts" ==> State
```

### End-to-End Capabilities:
- **Autonomous Batch Negotiation:** When `ProcessTwin.Py` starts, it automatically queries the .NET API for an `InProgress` batch. If none exists, it schedules (`POST /api/batches`) and starts (`POST /api/batches/{id}/start`) a new batch session (e.g. `BATCH-TWIN-20261104-...`).
- **Real-Time Threshold Alerting:** As the physical CSTR model fluctuates, sensor readings are evaluated against PostgreSQL-backed `AlertRule` thresholds in sub-millisecond execution times. Excursions trigger auditable `ProcessAlert` records.
- **Automated Regulatory Emergency Interlocks (SIS / IEC 61511):** If the chemical digital twin detects a thermal runaway ($>135^\circ\text{C}$), its Safety Instrumented System automatically invokes `POST /api/batches/{id}/abort` on the .NET engine with the compliance audit reason `"Emergency SIS interlock tripped on ProcessTwin."`.
- **Compliance Run Analytics:** Completing a batch (`POST /api/batches/{id}/complete`) calculates cycle durations, thermal peaks, and aggregated alarm metrics for quality release.

---

## ⚡ Key Capabilities

1. **Batch Lifecycle & State Machine:**
   - Strict transition rules (`Scheduled` $\rightarrow$ `InProgress` $\rightarrow$ `Completed` or `Aborted`).
   - Abort operations require mandatory operator rationale for regulatory auditing.
2. **High-Throughput Telemetry Ingestion:**
   - Ingests batch measurements across sensors (`TT-101` temperature, `PT-101` pressure, `LT-101` surge level, `FT-101`/`FT-102` flows, `TCV-101` cooling valve, `pH-101`, `RPM-101`).
   - Validates physical measurement units (`Celsius`, `Bar`, `Meters`, `M3PerHour`, `Percent`, `Rpm`, `Ph`) and telemetry quality indicators.
3. **Automated Safety Alarm Evaluation:**
   - Incoming measurements are evaluated against active high/low safety thresholds in real-time.
   - Immediate generation of auditable `ProcessAlert` records with severity levels (`Info`, `Warning`, `Critical`).
4. **Historical Analytics & KPI Summaries:**
   - Aggregates sensor statistics (average, minimum, peak values, run durations) per batch run.
5. **Robust Database Design:**
   - Composite indexes on `(batch_id, timestamp_utc)` and `(batch_id, sensor_tag)` for optimal time-series range query performance.

---

## 🛠️ Technology Stack

| Layer | Technologies |
| :--- | :--- |
| **Runtime & Framework** | .NET 8 (C# 12), ASP.NET Core Web API |
| **Architecture** | Clean Architecture, Domain-Driven Design (DDD) |
| **Database & ORM** | PostgreSQL 16, Entity Framework Core (Npgsql), Code-First Fluent API |
| **Validation** | FluentValidation |
| **Logging** | Serilog (Structured JSON Logging to Console & Diagnostics) |
| **Documentation** | Swagger / OpenAPI UI served directly at root `/` |
| **Testing** | xUnit, Shouldly, NSubstitute (26 automated tests) |
| **Containerization** | Multi-stage Dockerfile, Docker Compose |
| **CI/CD** | GitHub Actions Automated Test & Build Pipeline |

---

## 📋 REST API Specification

### Batches (`/api/batches`)
| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `POST` | `/api/batches` | Create and schedule a new manufacturing batch |
| `GET` | `/api/batches` | List batches with optional status filtering (`?status=InProgress`) |
| `GET` | `/api/batches/{id}` | Retrieve batch details and metadata |
| `POST` | `/api/batches/{id}/start` | Transition batch from `Scheduled` to `InProgress` |
| `POST` | `/api/batches/{id}/complete` | Mark active batch run as successfully `Completed` |
| `POST` | `/api/batches/{id}/abort` | Emergency abort batch with compliance reason |
| `GET` | `/api/batches/{id}/summary` | Retrieve batch KPI analytics (duration, averages, peak values) |

### Telemetry (`/api/telemetry`)
| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `POST` | `/api/telemetry/ingest` | High-throughput sensor ingestion + automated threshold alert evaluation |
| `GET` | `/api/telemetry/batch/{batchId}` | Query chronological time-series measurements (`?sensorTag=TT-101`) |

### Safety Alerts (`/api/alerts`)
| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/alerts/rules` | List active process safety and alarm rules |
| `POST` | `/api/alerts/rules` | Register a new threshold rule for a sensor tag |
| `GET` | `/api/alerts/batch/{batchId}` | List triggered alerts for a specific batch |
| `POST` | `/api/alerts/{id}/acknowledge` | Acknowledge an alarm with operator ID |

---

## 🚀 Quickstart & Execution

### 1. Run via Docker Compose (Recommended)

Start the entire environment (PostgreSQL 16 + ASP.NET Core API) with a single command:

```bash
docker compose up -d --build
```

- **Interactive Swagger UI:** Navigate to `http://localhost:5100`
- **Healthcheck:** `curl http://localhost:5100/health`

### 2. Run Locally via .NET CLI

Ensure a local PostgreSQL instance is running on port 5432:

```bash
# Clone the repository
git clone https://github.com/yancsalmeida/IndustrialTelemetry.Engine.git
cd IndustrialTelemetry.Engine

# Restore & Build
dotnet restore
dotnet build

# Run API
dotnet run --project src/IndustrialTelemetry.Api
```

---

## 🧪 Automated Testing

Execute the comprehensive unit test suite:

```bash
dotnet test
```

```text
Passed! - Failed: 0, Passed: 26, Skipped: 0, Total: 26, Duration: 113 ms
```

---

## 👤 Author

**Yan Almeida**  
- B.Sc. Chemical Engineering (Budapest University of Technology and Economics — BME)  
- Software Engineering & Systems Development (ISPGAYA — TPSI)  
- LinkedIn: [linkedin.com/in/yancsa](https://www.linkedin.com/in/yancsa/)  
- GitHub: [github.com/yancsalmeida](https://github.com/yancsalmeida)
