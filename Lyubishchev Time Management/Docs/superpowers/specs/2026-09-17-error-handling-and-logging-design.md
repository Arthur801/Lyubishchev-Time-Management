# Error Handling and Logging Design

**Date:** 2026-09-17

**Scope:** Complete TODO item 15's error-handling and logging work. Login/Register rate limiting and authentication event logs already exist; this design unifies unexpected failure responses, correlation, and operational logging without changing established business-error responses.

## Decisions

- API responses use RFC 7807 Problem Details. Unexpected API failures return one safe 500 shape with a `traceId` extension.
- HTML page failures render the existing `/Home/Error` view with the same `traceId`; API failures never redirect to HTML.
- Use built-in structured `ILogger` to Console/systemd journal only. No file logger, external telemetry SDK, or cloud-specific dependency is added in V1.
- Expected domain failures remain explicit Result/ErrorCode mappings in controllers: validation 400, not found 404, conflict 409, unauthorized 401, and rate limiting 429. The global handler does not turn them into 500s.

## Correlation and response contract

For every request, the correlation identifier is `Activity.Current?.Id` when available, falling back to `HttpContext.TraceIdentifier`. The handler creates a logging scope containing `TraceId`, `RequestMethod`, and `RequestPath` before emitting any error log. `traceId` is also added to API Problem Details and shown on the HTML error page.

Unexpected API exceptions return:

```json
{
  "type": "https://httpstatuses.com/500",
  "title": "INTERNAL_SERVER_ERROR",
  "status": 500,
  "detail": "An unexpected error occurred. Please try again later.",
  "traceId": "..."
}
```

The `detail`, `title`, and type are constants. They never incorporate exception text, SQL, request bodies, user-entered strings, database credentials, JWTs, cookies, password values, or stack traces. `Content-Type` is `application/problem+json`.

Unexpected non-API requests run through `/Home/Error`; the revised view explains that processing failed, displays the trace ID, and omits the existing Development Mode instructions. Development diagnostics remain available through normal server logs and developer tooling, never through production response content.

## Pipeline placement

Add a global exception-handler registration before the application is built, then call exception handling before HTTPS redirection, routing, rate limiting, authentication, and authorization. The handler branches by `Request.Path.StartsWithSegments("/api")`:

```text
exception occurs
  -> handler opens structured ILogger scope with traceId
  -> logs exception at Error level
  -> /api/*: ProblemDetails 500 JSON
  -> all other routes: re-execute /Home/Error
```

The handler must preserve the response only when it has not started. If a streaming/download response already began, it logs the failure and cannot replace sent bytes with Problem Details. CSV export is currently buffered before `File(...)`, so ordinary CSV generation failures occur before the response starts and are safely handled as API Problem Details.

Use `AddProblemDetails()` and `IProblemDetailsService` (or the equivalent built-in .NET 10 Problem Details path) instead of hand-writing arbitrary JSON. The handler must set the explicit status/title/detail/type and append `traceId` on the Problem Details extensions.

## Logging events

Use structured fields, not string concatenation. Existing `AuthEventLogger` remains responsible for login, registration, and rate-limit events; it must not log passwords, JWTs, cookies, or raw request bodies. Its calls automatically inherit the request scope trace ID.

| Event | Level | Required safe fields |
| --- | --- | --- |
| Unhandled request exception | Error | `TraceId`, method, path, exception object |
| Application start/shutdown | Information | environment, application name |
| Authentication/register success | Information | normalized email, IP address |
| Authentication/register failure | Warning | normalized email, error code, IP address |
| Rate-limit rejection | Warning | endpoint, IP address |
| Timer transaction unexpected failure | Error | trace ID, user ID, operation `Stop` or `Start`, exception object |
| CSV export unexpected failure | Error | trace ID, user ID, bounded-range presence, category-filter presence, search-present boolean, exception object |
| Database-related unexpected failure | Error | trace ID, method, path, exception object |

No log entry may include CSV bytes/cells, passwords, JWTs, cookies, authorization headers, connection strings, or SQL command text. Search strings are represented only by `SearchPresent`, never their value.

## Service boundaries

Introduce a focused `IOperationalEventLogger` / `OperationalEventLogger` abstraction for Timer and CSV failure logs. It wraps `ILogger<OperationalEventLogger>` and exposes methods whose parameters are already safe primitives:

```csharp
void TimerTransactionFailed(ulong userId, string operation, Exception exception);
void CsvExportFailed(ulong userId, bool hasRange, bool hasCategoryFilter, bool hasSearch, Exception exception);
```

`TimerService` catches only unexpected exceptions around its transaction boundary, logs via this abstraction, rolls back/disposes the transaction, then rethrows so the global handler owns the HTTP 500. Existing `DbUpdateConcurrencyException` and known duplicate-start `DbUpdateException` continue to return their current business outcomes and are not Error logs.

`CsvExportService` catches only unexpected exceptions from settings lookup/query/projection/writer, logs safe metadata, and rethrows. It does not catch cancellation (`OperationCanceledException` when the request token is cancelled); cancellation is not an application error and must not produce Error logs or a fabricated 500 response.

The global exception handler logs failures not already logged at service level. To avoid duplicate full exception records, service logs use event IDs and the global handler records request failure once with a `FailureSource` field. If a service failure is logged and rethrown, the handler adds only request/correlation context at Error level without logging sensitive payloads again.

## Tests and acceptance criteria

Add automated coverage for:

1. A deliberately throwing API endpoint returns 500 `application/problem+json` with nonempty `traceId`, fixed safe text, and no exception message.
2. A deliberately throwing HTML endpoint renders `/Home/Error` and exposes the same safe trace ID rather than JSON or a stack trace.
3. Existing 400/401/404/409/429 endpoint behavior remains unchanged.
4. A captured logger verifies Timer and CSV unexpected failures contain user/operation/filter-presence metadata but not CSV text, search values, cookie values, JWTs, passwords, connection strings, or SQL.
5. Timer concurrency and CSV cancellation preserve their current non-error semantics.
6. Startup/shutdown and existing auth/rate-limit events continue to use the configured Console `ILogger` provider.

Verify manually in a production-like environment that unhandled MVC and API exceptions yield the correct response types, that `traceId` identifies their journal records, and that Console/systemd output contains structured fields. Run the full test suite and `git diff --check` before recording completion.

## Consistency with current code

`Program.cs` already configures Console-compatible default logging levels, `AuthEventLogger` uses `ILogger`, rate limiting returns Problem Details, and controllers map known ErrorCodes to appropriate statuses. `HomeController.Error` already creates a RequestId from Activity/TraceIdentifier. This design evolves those patterns: the new global handler centralizes unexpected failures; the error view uses the same correlation identifier; and `TimerService`/`CsvExportService` gain only failure-event logging. It does not alter data ownership, antiforgery, rate limiting policy, transaction semantics, or CSV contents.
