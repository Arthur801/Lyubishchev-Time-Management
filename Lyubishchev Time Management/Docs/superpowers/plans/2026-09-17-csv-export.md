# CSV Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an authenticated user download every TimeEntry matching the active History filters as a safe, Excel-compatible CSV rendered in their saved time zone.

**Architecture:** `TimeEntryApiController.Export` binds the existing list filters and delegates to a new scoped `CsvExportService`. The service owns the unpaginated, user-scoped EF query and local-time row projection; a focused `CsvWriter` turns formatted rows into UTF-8 BOM RFC 4180 bytes. The History client produces the same account-time-zone UTC bounds for list and export, but never creates CSV content itself.

**Tech Stack:** ASP.NET Core MVC (.NET 10), EF Core/MySQL, Razor, browser Fetch/URL APIs, UTF-8, xUnit with shared SQLite in-memory integration fixture.

---

## File structure

| File | Responsibility |
| --- | --- |
| `Infrastructure/Csv/CsvWriter.cs` | Stateless RFC 4180 encoder with BOM, CRLF, formula neutralization, and no web/EF dependencies. |
| `Services/CsvExportService.cs` | Owned, unpaginated TimeEntry query; account zone lookup; display-row projection; filename selection. |
| `Controllers/Api/TimeEntryApiController.cs` | Thin authenticated `GET /api/time-entries/export` endpoint and list-equivalent range validation. |
| `Program.cs` | Scoped `CsvExportService` registration. |
| `Views/TimeEntry/Index.cshtml` | Export button next to the History controls. |
| `wwwroot/js/time-entry.js` | Account-zone-aware preset conversion and download URL built from effective filters. |
| `Tests/TimeEntryFlow.Tests/Unit/CsvWriterTests.cs` | Serialization and spreadsheet-injection tests. |
| `Tests/TimeEntryFlow.Tests/Integration/CsvExportServiceTests.cs` | Query, ownership, time zone, overlap, content, and empty-export tests. |

No migration, entity, persisted export job, background worker, or third-party CSV package is required.

### Task 1: Define and test the CSV byte writer

**Files:**

- Create: `Tests/TimeEntryFlow.Tests/Unit/CsvWriterTests.cs`
- Modify: `Infrastructure/Csv/CsvWriter.cs`

- [ ] **Step 1: Write failing CSV writer tests**

Create `CsvWriterTests` with the following explicit test cases. Use `Encoding.UTF8.GetString` only after first asserting the three BOM bytes (`EF BB BF`); remove the leading `\uFEFF` before comparing textual records.

```csharp
using System.Text;
using Lyubishchev_Time_Management.Infrastructure.Csv;
using Xunit;

namespace TimeEntryFlow.Tests.Unit;

public sealed class CsvWriterTests
{
    [Fact]
    public void Write_emits_utf8_bom_header_and_crlf_terminated_records()
    {
        var bytes = CsvWriter.Write(
            ["Date", "Name"],
            [["2026-09-17", "Focus"]]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Equal("Date,Name\r\n2026-09-17,Focus\r\n", Encoding.UTF8.GetString(bytes)[1..]);
    }

    [Fact]
    public void Write_quotes_rfc4180_special_characters_and_neutralizes_formulas()
    {
        var bytes = CsvWriter.Write(
            ["Name", "Notes"],
            [["=SUM(A1:A2)", "one, \"two\"\r\nthree"], ["  @danger", null]]);

        Assert.Equal(
            "Name,Notes\r\n'=SUM(A1:A2),\"one, \"\"two\"\"\r\nthree\"\r\n'  @danger,\r\n",
            Encoding.UTF8.GetString(bytes)[1..]);
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test .\Lyubishchev\ Time\ Management\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore --filter FullyQualifiedName~CsvWriterTests
```

Expected: compile failure because `CsvWriter.Write` does not exist; `CsvWriter.cs` is currently empty.

- [ ] **Step 3: Implement a focused, dependency-free writer**

Replace the empty `Infrastructure/Csv/CsvWriter.cs` with this implementation. Keep formula neutralization here because it is an output-encoding boundary; callers pass only display strings.

```csharp
using System.Text;

namespace Lyubishchev_Time_Management.Infrastructure.Csv;

public static class CsvWriter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        AppendRecord(builder, headers);
        foreach (var row in rows)
        {
            AppendRecord(builder, row);
        }

        return Utf8WithBom.GetBytes(builder.ToString());
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string?> fields)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendField(builder, fields[index] ?? string.Empty);
        }

        builder.Append("\r\n");
    }

    private static void AppendField(StringBuilder builder, string value)
    {
        var safeValue = NeutralizeFormula(value);
        var requiresQuotes = safeValue.IndexOfAny([',', '\"', '\r', '\n']) >= 0;
        if (requiresQuotes)
        {
            builder.Append('\"');
        }

        builder.Append(safeValue.Replace("\"", "\"\"", StringComparison.Ordinal));

        if (requiresQuotes)
        {
            builder.Append('\"');
        }
    }

    private static string NeutralizeFormula(string value)
    {
        var firstNonWhitespace = value.FirstOrDefault(character => !char.IsWhiteSpace(character));
        return firstNonWhitespace is '=' or '+' or '-' or '@' ? $"'{value}" : value;
    }
}
```

- [ ] **Step 4: Run the focused test and verify it passes**

Run the command from Step 2.

Expected: both `CsvWriterTests` pass.

- [ ] **Step 5: Commit the writer slice**

```powershell
git add -- "Lyubishchev Time Management/Infrastructure/Csv/CsvWriter.cs" "Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Unit/CsvWriterTests.cs"
git commit -m "feat: add safe CSV writer"
```

### Task 2: Build and verify the CSV export service

**Files:**

- Create: `Tests/TimeEntryFlow.Tests/Integration/CsvExportServiceTests.cs`
- Modify: `Services/CsvExportService.cs`

- [ ] **Step 1: Write failing integration tests for exported detail rows**

Use `TestDatabase.CreateAsync`, `TestClock`, `TimeEntryService`, `TimeZoneCatalog`, and `UserSettingsService` exactly as existing time-entry/aggregation integration tests do. Define this helper in the test class:

```csharp
private static DateTime Utc(int year, int month, int day, int hour, int minute)
    => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

private static CsvExportService CreateService(AppDbContext dbContext)
{
    var clock = new TestClock(Utc(2026, 9, 17, 0, 0));
    var catalog = new TimeZoneCatalog();
    return new CsvExportService(dbContext, new UserSettingsService(dbContext, clock, catalog), catalog);
}
```

Add these tests before implementation:

```csharp
[Fact]
public async Task ExportAsync_writes_complete_localized_rows_in_stable_descending_order()
{
    // Create Taipei user data: an older uncategorized entry and a newer Work entry with tags
    // "zeta" and "Alpha". Export unbounded data and assert the newer row is first.
    // Assert the header, `2026-01-02 09:00:00` display conversion from UTC+8,
    // `01:30:00`, `Uncategorized`, `Alpha; zeta`, and `Asia/Taipei` are present.
}

[Fact]
public async Task ExportAsync_includes_a_cross_midnight_entry_once_when_it_overlaps_the_bounded_range()
{
    // Save 2026-01-01 15:00Z through 17:00Z for a Taipei user.
    // Query [2026-01-01 16:00Z, 2026-01-02 16:00Z): assert one row and its original
    // `2026-01-01 23:00:00`, `2026-01-02 01:00:00`, and `02:00:00` values.
}

[Fact]
public async Task ExportAsync_applies_category_and_name_or_tag_filters_without_pagination()
{
    // Create matching and nonmatching entries, including 201 matching records, then export
    // with category and tag search. Assert all matching rows are present and no `Skip/Take`
    // limit was applied.
}

[Fact]
public async Task ExportAsync_excludes_another_users_entries_and_returns_header_only_when_empty()
{
    // Create one entry for each of two users. Export as the first user, verify only that user's
    // name appears; then query an empty range and assert exactly the BOM plus header record.
}

[Fact]
public async Task ExportAsync_uses_the_saved_dst_zone_for_display_but_utc_duration_for_elapsed_time()
{
    // Set the user to America/New_York and create an entry spanning the 2026 spring DST shift.
    // Assert local display values reflect the offset transition while duration is the exact UTC
    // elapsed `01:00:00`, not a wall-clock subtraction.
}
```

- [ ] **Step 2: Run the integration tests and verify they fail**

Run:

```powershell
dotnet test .\Lyubishchev\ Time\ Management\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore --filter FullyQualifiedName~CsvExportServiceTests
```

Expected: compile failure because `CsvExportService`, `CsvExportRequest`, and `CsvExportDocument` are not yet defined.

- [ ] **Step 3: Implement the service contract and query**

Replace the empty `Services/CsvExportService.cs` with a scoped service using the following public contract. Keep the request’s UTC bounds and the service’s full-entry (not aggregation) semantics explicit.

```csharp
public sealed record CsvExportRequest(
    DateTime? StartUtc,
    DateTime? EndUtc,
    ulong? CategoryId,
    string? Search);

public sealed record CsvExportDocument(byte[] Content, string FileName);

public sealed class CsvExportService(
    AppDbContext dbContext,
    UserSettingsService userSettingsService,
    TimeZoneCatalog timeZoneCatalog)
{
    public async Task<CsvExportDocument> ExportAsync(
        ulong userId,
        CsvExportRequest request,
        CancellationToken cancellationToken)
    {
        // Read saved TimeZoneId, resolve it with ResolveOrUtc, and query only UserId == userId.
        // Add half-open overlap predicates, category filtering, and the same trimmed name/tag
        // Like filter as TimeEntryService.ListAsync. Include Category and TimeEntryTags.Tag.
        // Order StartTimeUtc descending, then `(long)Id` descending. Do not use Skip or Take.
        // Project each entity to the documented eight display fields and invoke CsvWriter.Write.
    }
}
```

Implement these private helpers in the same file, with `CultureInfo.InvariantCulture`:

```csharp
private static readonly string[] Headers =
    ["Date", "Start Time", "End Time", "Duration", "Name", "Category", "Tags", "Time Zone"];

private static string FormatDateTime(DateTime utc, TimeZoneInfo timeZone)
    => TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

private static string FormatDuration(TimeSpan duration)
    => $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

private static string BuildFileName(DateTime? startUtc, DateTime? endUtc, TimeZoneInfo timeZone)
{
    if (startUtc is null || endUtc is null)
    {
        return "time-entries-all.csv";
    }

    var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc.Value, timeZone));
    var endDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endUtc.Value.AddTicks(-1), timeZone));
    return $"time-entries-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.csv";
}
```

For the row projection, use `Uncategorized` for a null category, empty string for a null name/no tags, and `string.Join("; ", entry.TimeEntryTags.Select(link => link.Tag.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))`. The first `Date` cell is the local start date in `yyyy-MM-dd`. `Duration` uses `entry.EndTimeUtc - entry.StartTimeUtc`, never local-time arithmetic.

- [ ] **Step 4: Run and expand the service tests until all pass**

Run the command from Step 2.

Expected: all `CsvExportServiceTests` pass, including the 201-row test proving that export is not limited by History’s page size.

- [ ] **Step 5: Commit the service slice**

```powershell
git add -- "Lyubishchev Time Management/Services/CsvExportService.cs" "Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration/CsvExportServiceTests.cs"
git commit -m "feat: export filtered time entries as CSV"
```

### Task 3: Expose the authenticated download endpoint

**Files:**

- Modify: `Controllers/Api/TimeEntryApiController.cs`
- Modify: `Program.cs`

- [ ] **Step 1: Write the controller behavior checklist as focused tests or an API smoke test**

In `CsvExportServiceTests`, add assertions for the service-level behavior the thin controller maps directly: malformed/non-positive bounded range is rejected before service invocation, valid empty data produces a header-only document, and cross-user data is absent. Then prepare this authenticated manual smoke test for the running application:

```powershell
Invoke-WebRequest -Uri 'https://localhost:<port>/api/time-entries/export?startUtc=2026-01-01T00:00:00Z&endUtc=2026-01-02T00:00:00Z' -WebSession $session -OutFile .\time-entries.csv
```

Expected after implementation: HTTP 200 with `Content-Type: text/csv; charset=utf-8`, `Content-Disposition: attachment`, and a file beginning `EF BB BF`.

- [ ] **Step 2: Register the service**

In `Program.cs`, directly after the existing scoped application services, add:

```csharp
builder.Services.AddScoped<CsvExportService>();
```

- [ ] **Step 3: Add a thin `Export` action**

Add `CsvExportService csvExportService` to the `TimeEntryApiController` primary constructor. Add this action above `Create`:

```csharp
[HttpGet("export")]
public async Task<IActionResult> Export(
    [FromQuery] DateTime? startUtc,
    [FromQuery] DateTime? endUtc,
    [FromQuery] ulong? categoryId,
    [FromQuery] string? search,
    CancellationToken cancellationToken = default)
{
    if (startUtc is not null && endUtc is not null && endUtc <= startUtc)
    {
        return Problem(
            detail: "The end of the selected range must be later than its start.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "INVALID_TIME_RANGE");
    }

    var document = await csvExportService.ExportAsync(
        currentUserService.GetRequiredUserId(),
        new CsvExportRequest(startUtc, endUtc, categoryId, search),
        cancellationToken);

    return File(document.Content, "text/csv; charset=utf-8", document.FileName);
}
```

Do not add antiforgery validation because this is a safe GET that has no state change. Do not accept `userId`, a client time-zone ID, `page`, or `pageSize`.

- [ ] **Step 4: Run build, focused tests, and the authenticated smoke test**

Run:

```powershell
dotnet test .\Lyubishchev\ Time\ Management\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~CsvWriterTests|FullyQualifiedName~CsvExportServiceTests"
dotnet build .\Lyubishchev\ Time\ Management\Lyubishchev\ Time\ Management.csproj --no-restore
```

Expected: tests and build pass. With a logged-in development session, run the Step 1 smoke request and confirm the response headers and BOM.

- [ ] **Step 5: Commit the endpoint slice**

```powershell
git add -- "Lyubishchev Time Management/Controllers/Api/TimeEntryApiController.cs" "Lyubishchev Time Management/Program.cs" "Lyubishchev Time Management/Tests/TimeEntryFlow.Tests/Integration/CsvExportServiceTests.cs"
git commit -m "feat: add time entry CSV download endpoint"
```

### Task 4: Make History range calculation and export action account-zone aware

**Files:**

- Modify: `Views/TimeEntry/Index.cshtml`
- Modify: `wwwroot/js/time-entry.js`

- [ ] **Step 1: Add the export control to the History UI**

In the `.page-heading` action area of `Views/TimeEntry/Index.cshtml`, place a button before the existing add-entry button:

```html
<button class="outline-button" type="button" id="export-csv">Export CSV</button>
```

Use the project’s localized text convention when applying the final UI copy. The stable `id="export-csv"` is required by the script and does not depend on translated text.

- [ ] **Step 2: Replace browser-zone preset calculations with saved account-zone calculations**

In `time-entry.js`, add a page-level `timeZoneId` field to `state`, then load it before the initial `fetchEntries()`:

```javascript
async function loadSettings() {
  const settings = await callApi('/api/settings/timezone');
  state.timeZoneId = settings.timeZoneId;
}
```

Replace `getUtcRangeForPreset` so it uses `Intl.DateTimeFormat(..., { timeZone: state.timeZoneId })` to obtain the account-zone calendar year/month/day, constructs local preset boundaries (Sunday-start week, first-to-first month), and converts those wall-clock boundaries to UTC. Add a small `zonedDateTimeToUtc(year, month, day, hour, minute, timeZoneId)` helper using `Intl.DateTimeFormat(..., { timeZoneName: 'longOffset' })` to derive the numeric offset and return an ISO UTC value. Do not use `new Date(year, month, day)` as the final boundary because it uses the browser zone.

Keep the existing `all` result `{ startUtc: null, endUtc: null }`. Make `buildListQuery(includePagination = true)` omit page/pageSize when false:

```javascript
function buildListQuery(includePagination = true) {
  // Preserve current startUtc, endUtc, categoryId, and trimmed search parameters.
  // Add page and pageSize only when includePagination is true.
}
```

Initialize in this order so no list/export request is built without the saved zone:

```javascript
loadSettings().then(fetchEntries).catch((error) => {
  $('#history-count').textContent = error.message;
});
```

- [ ] **Step 3: Wire the download button without fetching CSV through JavaScript**

Add this listener near the other History controls:

```javascript
$('#export-csv').addEventListener('click', () => {
  const button = $('#export-csv');
  button.disabled = true;
  window.location.assign(`/api/time-entries/export?${buildListQuery(false)}`);
  window.setTimeout(() => { button.disabled = false; }, 1000);
});
```

Do not call `callApi` for this action: it attempts `response.json()` and would corrupt the normal browser-download flow. The short timeout merely prevents repeated activation while navigation begins; the endpoint response remains the browser’s responsibility.

- [ ] **Step 4: Perform desktop and mobile manual checks**

Run the app and verify:

1. With the browser set to a different zone from the account (for example browser UTC, account `Asia/Taipei`), Today/Week/Month list results and the downloaded CSV use the account zone.
2. Changing category/search/range changes both list and export URL; changing page does not change export URL or record count.
3. `all` downloads `time-entries-all.csv` and bounded ranges use local inclusive date filenames.
4. The button stays usable at desktop and mobile widths, and a failed settings load displays the same History error area rather than sending an unbounded export.

- [ ] **Step 5: Commit the History slice**

```powershell
git add -- "Lyubishchev Time Management/Views/TimeEntry/Index.cshtml" "Lyubishchev Time Management/wwwroot/js/time-entry.js"
git commit -m "feat: export filtered history as CSV"
```

### Task 5: Run regression checks and record the feature state

**Files:**

- Modify: `Docs/TODO.md`
- Modify: `Docs/HANDOFF.md`

- [ ] **Step 1: Execute the targeted and full test suites**

Run:

```powershell
dotnet test .\Lyubishchev\ Time\ Management\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj --no-restore
dotnet test .\Lyubishchev\ Time\ Management\Tests\TimerFlow.Tests\TimerFlow.Tests.csproj --no-restore
dotnet test .\Lyubishchev\ Time\ Management\Tests\AuthFlow.Tests\AuthFlow.Tests.csproj --no-restore
git diff --check
```

Expected: all suites pass and `git diff --check` has no output.

- [ ] **Step 2: Update project handoff records only after the feature passes**

In `Docs/TODO.md`, mark item 13 complete and link both the CSV design and this plan. In `Docs/HANDOFF.md`, record the endpoint, eight-column schema, account-zone behavior, the History range-boundary correction, and the CSV test locations. Do not mark the item complete if any manual download or test from Tasks 3–5 remains unverified.

- [ ] **Step 3: Commit documentation and verified feature state**

```powershell
git add -- "Lyubishchev Time Management/Docs/TODO.md" "Lyubishchev Time Management/Docs/HANDOFF.md"
git commit -m "docs: record CSV export completion"
```

## Plan self-review

Coverage mapping:

- UTF-8 BOM, RFC 4180, formula defense, and header-only files: Task 1 and Task 2.
- Current-user ownership, filters, unpaginated ordering, category/tag data: Task 2 and Task 3.
- UTC source of truth, account-zone rendering, DST duration, and cross-midnight full rows: Task 2.
- Endpoint contract and file download headers: Task 3.
- History-only entry point and identical list/export effective range: Task 4.
- Regression, manual verification, and handoff state: Task 5.

The plan deliberately keeps interval aggregation out of this feature: `TimeAggregationService` clips entries for statistics, while CSV export serializes each original TimeEntry once.
