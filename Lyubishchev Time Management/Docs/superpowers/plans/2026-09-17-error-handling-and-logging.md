# Error Handling and Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return safe, traceable RFC 7807 responses for unexpected API failures, safe HTML error pages for MVC failures, and structured operational logs without changing existing domain-error behavior.

**Architecture:** Keep controllers responsible for known Result/ErrorCode mapping. Register one global exception handler that branches on `/api`, uses the built-in Problem Details service, and scopes all request logs with a trace ID. `OperationalEventLogger` records safe Timer/CSV failure metadata while rethrowing to the handler.

**Tech Stack:** ASP.NET Core .NET 10 exception handling/Problem Details, built-in `ILogger`, Console/systemd journal, xUnit, ASP.NET Core integration testing.

---

## Target files

- Create: `Infrastructure/Errors/GlobalExceptionHandler.cs`
- Create: `Infrastructure/Logging/IOperationalEventLogger.cs`
- Create: `Infrastructure/Logging/OperationalEventLogger.cs`
- Create: `Tests/WebFlow.Tests/WebFlow.Tests.csproj`
- Create: `Tests/WebFlow.Tests/ErrorHandlingTests.cs`
- Modify: `Program.cs`
- Modify: `Controllers/HomeController.cs`
- Modify: `Views/Shared/Error.cshtml`
- Modify: `Services/TimerService.cs`
- Modify: `Services/CsvExportService.cs`
- Modify after verification only: `Docs/TODO.md`, `Docs/HANDOFF.md`

### Task 1: Establish global error contract with an integration test

- [ ] **Step 1: Create a WebFlow test project and failing tests**

Create `Tests/WebFlow.Tests/WebFlow.Tests.csproj` with `Microsoft.AspNetCore.Mvc.Testing` matching the application runtime, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and a ProjectReference to `../../Lyubishchev Time Management.csproj`. Add a `WebApplicationFactory<Program>` fixture that replaces the app DbContext with an in-memory/test provider and sets nonsecret test JWT/configuration values.

In `ErrorHandlingTests.cs`, add a test-only endpoint mapper through the factory’s test startup hook: `/api/test/throw` throws `InvalidOperationException("secret exception text")`; `/test/throw` does the same. Assert API response is 500 `application/problem+json`, has `title` `INTERNAL_SERVER_ERROR`, nonempty `traceId`, and does not contain `secret exception text`. Assert HTML response is 500, renders the safe error page, includes its trace ID, and does not contain exception text or Development Mode instructions.

- [ ] **Step 2: Run and confirm initial failure**

Run `dotnet test .\Lyubishchev\ Time\ Management\Tests\WebFlow.Tests\WebFlow.Tests.csproj --no-restore`. Expected result: failure because no global handler provides the API Problem Details contract.

- [ ] **Step 3: Add Problem Details and exception-handler registration**

In `Program.cs`, register `AddProblemDetails()` and scoped `GlobalExceptionHandler`. Before HTTPS redirection and routing, use `app.UseExceptionHandler(...)`. The API branch must not redirect; the MVC branch re-executes `/Home/Error`. Keep rate limiting, authentication, and authorization order unchanged.

Create `GlobalExceptionHandler.cs` implementing `IExceptionHandler`. It must derive the trace ID from `Activity.Current?.Id ?? context.TraceIdentifier`, begin an `ILogger` scope with `TraceId`, `RequestMethod`, and `RequestPath`, then log the exception at Error. For `/api`, set 500 status and call `IProblemDetailsService.WriteAsync` with fixed type `https://httpstatuses.com/500`, title `INTERNAL_SERVER_ERROR`, fixed generic detail, and `Extensions["traceId"]`. Return `true`. For non-API paths, set `context.Items["TraceId"]`, re-execute `/Home/Error`, and return `true`. If `Response.HasStarted`, log and return `false` without writing a second response. If the exception is request-aborted `OperationCanceledException`, return `false` and do not log it as Error.

- [ ] **Step 4: Make HTML errors safe and correlated**

Update `HomeController.Error` to use `HttpContext.Items["TraceId"] as string` before Activity/TraceIdentifier fallback. Replace `Views/Shared/Error.cshtml` with a short generic message and a Request ID line; remove all Development Mode guidance and any exception-derived content.

- [ ] **Step 5: Re-run and commit**

Re-run the Step 2 command. Expected result: API and MVC tests pass. Commit the handler, Program, Home/Error, and WebFlow tests with `feat: add safe global error handling`.

### Task 2: Add safe operational event logging

- [ ] **Step 1: Write failing logger capture tests**

Create a test `ILoggerProvider` in `WebFlow.Tests` that captures level, event ID, message template, exception, and structured state. Add tests that invoke a forced Timer transaction failure and a forced CSV export failure. Assert Error records contain only safe primitives: user ID, operation, trace ID, `HasRange`, `HasCategoryFilter`, `HasSearch`, and exception. Assert captured formatted/state data does not contain the test password, JWT, cookie value, SQL, connection string, CSV cell text, or search text.

- [ ] **Step 2: Implement the operational logger abstraction**

Create this interface:

```csharp
public interface IOperationalEventLogger
{
    void TimerTransactionFailed(ulong userId, string operation, Exception exception);
    void CsvExportFailed(ulong userId, bool hasRange, bool hasCategoryFilter, bool hasSearch, Exception exception);
}
```

Implement `OperationalEventLogger` with `ILogger<OperationalEventLogger>`. Use distinct stable event IDs: `1001` for Timer transaction failure and `1002` for CSV export failure. Log the exception overload with structured placeholders only; never pass `name`, tags, CSV bytes, raw search text, request headers, credentials, or connection strings. Register it as singleton or scoped in `Program.cs` consistently with `ILogger` usage; scoped is preferred for request lifetime symmetry.

- [ ] **Step 3: Instrument Timer and CSV failure boundaries**

Inject `IOperationalEventLogger` into `TimerService` and `CsvExportService`. In `TimerService.StopAsync`, add one catch for unexpected exceptions surrounding the transaction work: log `TimerTransactionFailed(userId, "Stop", exception)`, roll back if the transaction is active, then rethrow. Preserve the existing `DbUpdateConcurrencyException -> TIMER_NOT_RUNNING` behavior and do not log it at Error. In `CsvExportService.ExportAsync`, wrap settings/query/row/writer work in `try`; on unexpected non-cancellation exception call `CsvExportFailed(userId, request.StartUtc is not null || request.EndUtc is not null, request.CategoryId is not null, !string.IsNullOrWhiteSpace(request.Search), exception)` then rethrow. Let `OperationCanceledException` propagate without operational Error logging.

- [ ] **Step 4: Run focused tests and commit**

Run `dotnet test .\Lyubishchev\ Time\ Management\Tests\WebFlow.Tests\WebFlow.Tests.csproj --no-restore`. Expected result: error-log metadata tests pass. Commit service/logger/test changes with `feat: log operational service failures safely`.

### Task 3: Preserve expected failures and verify production behavior

- [ ] **Step 1: Add regression assertions**

In `ErrorHandlingTests`, call one known invalid range endpoint and one unauthorized endpoint. Assert the invalid request remains 400 Problem Details with `INVALID_TIME_RANGE`; assert unauthenticated API response remains 401; assert rate-limit rejection remains 429 `TOO_MANY_REQUESTS`. Add Timer concurrent-stop test coverage confirming the known `TIMER_NOT_RUNNING` path is not treated as an unhandled Error.

- [ ] **Step 2: Run all test suites**

Run `dotnet test` for `AuthFlow.Tests`, `TimerFlow.Tests`, `TimeEntryFlow.Tests`, and `WebFlow.Tests`, each with `--no-restore`; then run `git diff --check`. Expected result: all pass and the diff check has no output.

- [ ] **Step 3: Perform a production-like journal check**

Run the application with non-development environment settings, trigger one test API failure and one test MVC failure, and verify: API is JSON rather than redirect HTML; MVC is the safe error view; returned trace IDs match structured Console/systemd journal records; no sensitive text appears in either response or log. Verify normal startup/shutdown, existing auth logs, and rate-limit logs remain at their documented levels.

- [ ] **Step 4: Record verified completion**

Only after all automated and manual checks pass, mark TODO item 15 complete. Update HANDOFF with the handler, Problem Details contract, trace ID source, operational event IDs, Console/systemd logging choice, and test project. Commit docs with `docs: record error handling and logging completion`.

## Plan self-review

Task 1 implements API/MVC branching, safe Problem Details, correlation, HTML error safety, and handler placement. Task 2 adds required Timer/CSV operational logs while retaining business-error semantics and cancellation behavior. Task 3 proves 400/401/404/409/429 behavior, test-suite compatibility, production response shape, and journal correlation. The plan makes no changes to authorization ownership, rate-limit limits, persistence data, CSV contents, or domain calculations.
