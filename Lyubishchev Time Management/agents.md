# agent.md

## Project Overview

Build a single-user time-tracking web application based on the Lyubishev time-management method.

Primary goals:

1. Accurately record where the user's time goes.
2. Analyze time allocation and long-term trends.
3. Keep the first version small, maintainable, and production-ready.

Do **not** expand the project into a task manager, calendar planner, AI productivity assistant, team workspace, or subscription product.

---

## Tech Stack

- Backend: C# / ASP.NET Core MVC
- Runtime: .NET 10
- ORM: Entity Framework Core
- Database: MySQL / InnoDB
- Frontend: Razor Views + HTML + CSS + JavaScript
- Dynamic requests: Fetch API / AJAX
- Authentication: Email + Password + JWT
- JWT storage: Secure + HttpOnly Cookie
- Hosting: AWS EC2
- Reverse proxy: Nginx
- Architecture: Modular Monolith + Layered Architecture
- UI: Desktop + Mobile RWD
- Export: CSV

---

## Core Domain Rules

### User

- All user data is private.
- Never trust a `UserId` supplied by the frontend.
- Always derive the authenticated user from JWT claims.
- Every query/update/delete for user-owned data must also filter by authenticated `UserId`.

### RunningTimer

- Each user can have **at most one** RunningTimer.
- RunningTimer is stored on the server.
- Timer continues if browser is closed.
- Different devices can view and stop the same timer.
- RunningTimer stores only:
  - `UserId`
  - `StartedAtUtc`
- Use `UserId` as the primary key of `RunningTimers`.
- Do not represent an active timer as `TimeEntry.EndTime = NULL`.

### Stop Timer

Stop Timer must run inside one DB transaction:

1. Lock/get the current RunningTimer.
2. Read current UTC time.
3. Create TimeEntry.
4. Delete RunningTimer.
5. Commit.

If any step fails, rollback.

Concurrent Stop requests from multiple devices must create only one TimeEntry.

### TimeEntry

Required:

- `StartTimeUtc`
- `EndTimeUtc`

Optional:

- `Name`
- `CategoryId`
- Tags

Rules:

- `EndTimeUtc > StartTimeUtc`
- Manual TimeEntry creation is allowed.
- TimeEntries may overlap.
- Overlapping durations are added directly.
- Therefore tracked time may exceed 24 hours per day.
- Long TimeEntries are allowed.

### Category

- Single-level only.
- A TimeEntry has `0..1` Category.
- Category contains:
  - Name
  - Color
- CRUD supported.
- Deleting Category must keep TimeEntry and set:
  - `TimeEntry.CategoryId = NULL`
- Use `ON DELETE SET NULL`.

### Tag

- Free-form tag.
- A TimeEntry has `0..N` Tags.
- Tags can be created:
  - from Tag management page
  - inline while editing/creating TimeEntry
- If an inline tag does not exist, create it.
- Tag and TimeEntry are many-to-many.
- Deleting a Tag removes only join-table relations, not TimeEntries.
- Enforce `UNIQUE(UserId, Name)`.

### Timezone

- Store all timestamps in UTC.
- Every user has a configurable timezone.
- Changing timezone must not rewrite historical UTC timestamps.
- Convert user-local date ranges to UTC before querying.

### Cross-midnight Statistics

For a TimeEntry crossing a query boundary, calculate only the interval intersection.

Example:

```text
TimeEntry: 23:00 → 01:00
Query day: 00:00 → 24:00
Counted:   00:00 → 01:00
```

Do not group only by `StartTimeUtc`.

---

## Functional Scope

### Authentication

Implement only:

- Register
- Login
- Logout

Do not implement in V1:

- Email verification
- Forgot password
- Change password
- OAuth
- Multi-user/team features

### Dashboard

Support date range selection:

- Today
- This Week
- This Month
- Custom Range

Show:

- Total tracked time
- Category distribution
- Tag distribution
- Daily tracked time

Category statistics must include:

- `Uncategorized`

Tag statistics are additive. One TimeEntry with multiple tags contributes its full duration to every assigned tag.

### Report

V1 charts:

- Category time distribution: Pie chart
- Tag time distribution: Bar chart

Do not use a pie chart for tags because tag totals can exceed total tracked time.

### History

Provide two views:

1. List View
   - newest to oldest
   - edit
   - delete

2. Calendar View
   - display only in V1
   - no drag/drop
   - no resize editing
   - use Category color
   - desktop: week/day timeline
   - mobile: day-oriented layout

### CSV Export

Allow CSV export by date range.

Recommended columns:

- Date
- Start Time
- End Time
- Duration
- Name
- Category
- Tags
- Time Zone

Convert UTC values to the user's current timezone before export.

---

## Architecture Rules

Use this dependency direction:

```text
Browser
  ↓
Controller
  ↓
Application Service
  ↓
EF Core / Infrastructure
  ↓
MySQL
```

### Controllers

Controllers must be thin.

Responsibilities:

- Receive request
- Validate input
- Read authenticated user
- Call service
- Return View / JSON / Redirect

Do not place complex business logic or long EF queries inside controllers.

### Application Services

Use services such as:

- `AuthService`
- `TimerService`
- `TimeEntryService`
- `CategoryService`
- `TagService`
- `DashboardService`
- `ReportService`
- `TimeAggregationService`
- `CsvExportService`
- `UserSettingsService`

### TimeAggregationService

Centralize:

- timezone conversion
- interval intersection
- duration calculation
- Category aggregation
- Tag aggregation

Dashboard and Report must share the same aggregation rules.

### Repository Pattern

Do **not** add a generic `Repository<T>` layer unless a concrete need appears.

Use EF Core `DbContext` directly from the application/data-access layer.

---

## Project Structure

```text
TimeTracker/
│
├─ Controllers/
│  ├─ AccountController.cs
│  ├─ DashboardController.cs
│  ├─ TimeEntryController.cs
│  ├─ ReportController.cs
│  ├─ CategoryController.cs
│  ├─ TagController.cs
│  ├─ SettingsController.cs
│  │
│  └─ Api/
│     ├─ TimerApiController.cs
│     ├─ TimeEntryApiController.cs
│     ├─ CategoryApiController.cs
│     ├─ TagApiController.cs
│     ├─ DashboardApiController.cs
│     ├─ ReportApiController.cs
│     └─ SettingsApiController.cs
│
├─ Models/
│  ├─ Entities/
│  │  ├─ User.cs
│  │  ├─ RunningTimer.cs
│  │  ├─ TimeEntry.cs
│  │  ├─ Category.cs
│  │  ├─ Tag.cs
│  │  └─ TimeEntryTag.cs
│  │
│  ├─ Requests/
│  │  ├─ LoginRequest.cs
│  │  ├─ RegisterRequest.cs
│  │  ├─ CreateTimeEntryRequest.cs
│  │  ├─ UpdateTimeEntryRequest.cs
│  │  ├─ CategoryRequest.cs
│  │  ├─ TagRequest.cs
│  │  └─ UpdateTimezoneRequest.cs
│  │
│  ├─ Responses/
│  │  ├─ TimerResponse.cs
│  │  ├─ TimeEntryResponse.cs
│  │  ├─ DashboardResponse.cs
│  │  └─ ReportResponse.cs
│  │
│  └─ ViewModels/
│     ├─ DashboardViewModel.cs
│     ├─ HistoryViewModel.cs
│     ├─ CalendarViewModel.cs
│     └─ ReportViewModel.cs
│
├─ Services/
│  ├─ AuthService.cs
│  ├─ TimerService.cs
│  ├─ TimeEntryService.cs
│  ├─ CategoryService.cs
│  ├─ TagService.cs
│  ├─ DashboardService.cs
│  ├─ ReportService.cs
│  ├─ TimeAggregationService.cs
│  ├─ CsvExportService.cs
│  └─ UserSettingsService.cs
│
├─ Data/
│  ├─ AppDbContext.cs
│  ├─ Configurations/
│  │  ├─ UserConfiguration.cs
│  │  ├─ RunningTimerConfiguration.cs
│  │  ├─ TimeEntryConfiguration.cs
│  │  ├─ CategoryConfiguration.cs
│  │  ├─ TagConfiguration.cs
│  │  └─ TimeEntryTagConfiguration.cs
│  └─ Migrations/
│
├─ Security/
│  ├─ JwtTokenService.cs
│  ├─ CurrentUserService.cs
│  └─ AuthConstants.cs
│
├─ Infrastructure/
│  ├─ Clock/
│  │  ├─ IClock.cs
│  │  └─ SystemClock.cs
│  ├─ Csv/
│  │  └─ CsvWriter.cs
│  └─ Logging/
│
├─ Views/
│  ├─ Account/
│  ├─ Dashboard/
│  ├─ TimeEntry/
│  ├─ Report/
│  ├─ Category/
│  ├─ Tag/
│  ├─ Settings/
│  └─ Shared/
│
├─ wwwroot/
│  ├─ css/
│  │  ├─ site.css
│  │  ├─ dashboard.css
│  │  ├─ history.css
│  │  └─ responsive.css
│  │
│  └─ js/
│     ├─ timer.js
│     ├─ time-entry.js
│     ├─ calendar.js
│     ├─ dashboard.js
│     ├─ report.js
│     ├─ category.js
│     ├─ tag.js
│     └─ settings.js
│
├─ Tests/
│  ├─ Unit/
│  └─ Integration/
│
├─ Program.cs
├─ appsettings.json
├─ appsettings.Development.json
└─ TimeTracker.csproj
```

---

## Database Model

### Users

```text
Id
Email UNIQUE
PasswordHash
TimeZoneId
CreatedAtUtc
UpdatedAtUtc
```

### RunningTimers

```text
UserId PK/FK
StartedAtUtc
```

### Categories

```text
Id
UserId FK
Name
Color
CreatedAtUtc
UpdatedAtUtc
```

Index:

```text
(UserId, Name)
```

### Tags

```text
Id
UserId FK
Name
CreatedAtUtc
UpdatedAtUtc
```

Constraint:

```text
UNIQUE(UserId, Name)
```

### TimeEntries

```text
Id
UserId FK
CategoryId FK NULL
Name NULL
StartTimeUtc
EndTimeUtc
CreatedAtUtc
UpdatedAtUtc
```

Indexes:

```text
(UserId, StartTimeUtc)
(UserId, CategoryId, StartTimeUtc)
```

### TimeEntryTags

```text
TimeEntryId FK
TagId FK
PRIMARY KEY(TimeEntryId, TagId)
```

---

## Main API Endpoints

```text
GET    /api/timer
POST   /api/timer/start
POST   /api/timer/stop

GET    /api/time-entries
POST   /api/time-entries
PATCH  /api/time-entries/{id}
DELETE /api/time-entries/{id}
GET    /api/time-entries/export

GET    /api/categories
POST   /api/categories
PATCH  /api/categories/{id}
DELETE /api/categories/{id}

GET    /api/tags
POST   /api/tags
PATCH  /api/tags/{id}
DELETE /api/tags/{id}

GET    /api/dashboard
GET    /api/reports/category
GET    /api/reports/tag

PATCH  /api/settings/timezone
```

---

## Security Requirements

- Store Password Hash only.
- Store JWT in Secure + HttpOnly Cookie.
- Enable HTTPS in production.
- Use Anti-forgery/CSRF protection for state-changing requests.
- Never expose:
  - stack traces
  - SQL
  - DB credentials
  - JWT
  - passwords
- Validate ownership of all Category/Tag/TimeEntry IDs.
- Login/Register should use basic rate limiting.
- Secrets must not be committed to Git.

---

## Error Handling

Expected mapping:

| HTTP | Usage |
|---|---|
| 400 | Invalid input |
| 401 | Not authenticated |
| 403 | Forbidden |
| 404 | Resource not found |
| 409 | State conflict |
| 500 | Unexpected error |

Example:

```text
POST /api/timer/start
```

when timer already exists:

```text
409
errorCode = TIMER_ALREADY_RUNNING
```

Unexpected exceptions:

```text
Global Exception Handler
→ Application Log
→ Generic frontend error
```

Frontend generic message:

```text
發生錯誤，請稍後再試。
```

---

## Query Rules

### Calendar

Never load all TimeEntries.

Query only visible range.

Overlap condition:

```text
Entry.StartTimeUtc < RangeEndUtc
AND
Entry.EndTimeUtc > RangeStartUtc
```

### History

Use pagination.

Recommended default:

```text
50 records/page
```

### Statistics

Do not duplicate aggregation logic across Dashboard and Report.

Use `TimeAggregationService`.

---

## RWD Rules

At minimum support two layouts.

### Desktop

Recommended:

```text
>= 768px
```

- Sidebar navigation
- Multi-column Dashboard
- Week/day Calendar
- Two-column Report layout
- Table/List History

### Mobile

Recommended:

```text
< 768px
```

- Compact top nav or bottom nav
- Vertical Dashboard cards
- Day-oriented Calendar
- Vertical charts
- Card-style History items
- Timer action always easy to reach

---

## Deployment

Production flow:

```text
Internet
→ AWS Security Group
→ Nginx :443
→ ASP.NET Core / Kestrel
→ MySQL localhost:3306
```

Rules:

- Only HTTPS publicly exposed.
- MySQL port 3306 must not be public.
- Restrict SSH access.
- Run ASP.NET Core with `systemd`.
- Database and app may share one EC2 in V1.
- Because this is a single point of failure, configure regular DB backups to external storage/S3.

---

## Logging

Log:

- startup/shutdown
- unexpected exceptions
- DB errors
- authentication failure summary
- timer transaction failure
- CSV export failure

Never log:

- Password
- JWT
- auth cookies
- DB password

---

## Testing Priorities

### Unit Tests

Prioritize:

- duration
- overlap
- interval intersection
- cross-midnight calculation
- timezone conversion
- Category aggregation
- Tag aggregation
- Timer state

### Integration Tests

Prioritize:

- Register/Login
- Start/Stop Timer
- concurrent Stop behavior
- TimeEntry CRUD
- Category delete → SET NULL
- Tag delete → remove relation only
- cross-user access rejection

---

## Implementation Order

```text
1. Project / MySQL / EF Core
2. User / Register / Login / Logout / JWT
3. RunningTimer / Start / Stop
4. Manual TimeEntry CRUD
5. Category
6. Tag + TimeEntryTag
7. History List
8. Calendar View
9. TimeAggregationService
10. Dashboard
11. Report
12. Timezone settings
13. CSV export
14. RWD
15. Error handling / Logging / Rate limit
16. Nginx / EC2 / Backup
```

---

## Definition of Done

A feature is not complete until:

1. It follows the ownership/security rules.
2. Business logic is in services, not controllers.
3. UTC/timezone behavior is correct.
4. Relevant DB constraints/indexes exist.
5. Expected errors return correct HTTP status.
6. Desktop and mobile layouts remain usable.
7. Critical domain logic has tests.
8. No secret or sensitive token is committed or logged.

---

## Non-goals for V1

Do not implement unless requirements change:

- Team/workspace
- Shared data
- AI recommendations
- Subscription/payment
- Task planning
- Time budget/planning
- Calendar drag/drop editing
- OAuth
- Redis
- Microservices
- Kubernetes
