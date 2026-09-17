# CSV Export Design

**Date:** 2026-09-17

**Scope:** Implement item 13, CSV export for TimeEntry history. This is a download of time-entry detail, not an aggregation report and not a backup format.

## Confirmed decisions

- The export action lives only on `/TimeEntry`, beside the existing history-list controls.
- It exports all entries matching the history screen's active filters, not merely the current pagination page.
- The file is comma-separated UTF-8 with BOM so that Microsoft Excel opens Chinese text correctly.
- UTC timestamps remain the source of truth. Values rendered in the CSV use the authenticated user's *current* saved IANA time zone.

## User experience

The history page has an `Export CSV` button in its header or filter bar. Activating it performs a normal GET download to `GET /api/time-entries/export` with the same effective filter values as the list request:

| Query parameter | Meaning |
| --- | --- |
| `startUtc` | Inclusive UTC start of the active local date range; absent for `all` |
| `endUtc` | Exclusive UTC end of the active local date range; absent for `all` |
| `categoryId` | Optional selected category |
| `search` | Optional trimmed name/tag search text |

`page` and `pageSize` are intentionally omitted. The download includes every matching entry in descending `StartTimeUtc`, then descending `Id` order, matching the list's stable ordering.

The existing history presets (`today`, `week`, `month`, `all`) remain the source of selected range. For the three bounded presets, the browser must calculate `startUtc` and `endUtc` from the account time zone returned by `GET /api/settings/timezone`, rather than from the browser's zone. This closes the present mismatch where `time-entry.js` calculates preset boundaries with the browser-local `Date` API. `all` has neither bound.

The export button is disabled while a download is being prepared. It restores its enabled state after navigation/download begins or if the request cannot be started. The browser's standard file-download behavior is used; no CSV content is parsed or constructed in JavaScript. No new export page, saved preset, scheduled export, background job, or report export is part of V1.

## Endpoint and authorization

```text
GET /api/time-entries/export?startUtc=...&endUtc=...&categoryId=...&search=...
```

The endpoint is `[Authorize]`. Its controller obtains `userId` exclusively through `CurrentUserService.GetRequiredUserId()`; a request must never supply a user ID. The service query always begins with `TimeEntry.UserId == userId`, so an export cannot reveal another account's entries.

The controller validates the same range rule as the list endpoint: when both values are present, `endUtc` must be later than `startUtc`. A malformed timestamp, non-positive bounded range, or invalid `categoryId` binding returns `400` Problem Details with `INVALID_TIME_RANGE` when applicable. An unauthenticated request returns `401`. A selected category that is not owned by the current user simply yields no entries, consistent with the list filter rather than disclosing its existence.

The success response has:

```text
Content-Type: text/csv; charset=utf-8
Content-Disposition: attachment; filename="time-entries-YYYYMMDD-YYYYMMDD.csv"
```

For a bounded range, the file-name dates are the selected local start and inclusive local end dates. For `all`, the filename is `time-entries-all.csv`. The filename contains no user-entered text.

## Export data and time semantics

Each CSV row represents one stored TimeEntry. Export does not split an entry at midnight or at the selected range boundary. The selected range only determines whether the entry is included, via the established visible-overlap predicate:

```text
entry.StartTimeUtc < rangeEndUtc
AND entry.EndTimeUtc > rangeStartUtc
```

Consequently, a 23:00–01:00 entry that overlaps a requested day is exported once, with its full original start, end, and duration. This is deliberately different from `TimeAggregationService`, which intersects entries to calculate statistics.

Use `UserSettingsService.GetAsync` (or a focused equivalent that reads the same user-owned `TimeZoneId`) and `TimeZoneCatalog.ResolveOrUtc` to get the account's current zone. Convert the stored UTC start and end independently with `TimeZoneInfo.ConvertTimeFromUtc`. Do not change historical UTC values and do not use the browser zone. The `Time Zone` column contains the saved IANA ID used for that conversion.

| Header | Value |
| --- | --- |
| `Date` | Local date of `Start Time`, formatted `yyyy-MM-dd` |
| `Start Time` | Local start datetime, formatted `yyyy-MM-dd HH:mm:ss` |
| `End Time` | Local end datetime, formatted `yyyy-MM-dd HH:mm:ss` |
| `Duration` | Whole entry duration as `HH:mm:ss`; durations may exceed 24 hours |
| `Name` | Entry name; empty when null |
| `Category` | Category name, or literal `Uncategorized` when null |
| `Tags` | Tag names sorted ordinal-ignore-case and joined with `; `; empty when none |
| `Time Zone` | Current account IANA time-zone ID |

The `Duration` comes from `EndTimeUtc - StartTimeUtc`, not from wall-clock subtraction of local displayed values; it remains correct across DST transitions. A zone change affects future export rendering only and never mutates stored entries.

## CSV encoding and escaping

`CsvWriter` is responsible only for RFC 4180 serialization. It emits a UTF-8 BOM once, uses `\r\n` line endings, comma delimiters, and a final record terminator. Every cell is quoted when necessary: quote fields containing comma, double quote, CR, or LF; represent a literal quote as two quotes. Null values become an empty cell. The writer receives already formatted scalar values and must not know about EF Core, user ownership, time zones, or HTTP.

User-controlled values (`Name`, category name, and tags) additionally need spreadsheet-formula neutralization before CSV escaping. If the first non-whitespace character is `=`, `+`, `-`, or `@`, prefix the displayed value with a single apostrophe. This prevents Excel/compatible spreadsheet applications from evaluating entry text as a formula while preserving its visible content.

An export with no matching entries is successful and contains the header row only.

## Service boundaries

```text
TimeEntryApiController.Export
  -> CurrentUserService (authenticated user ID)
  -> CsvExportService.ExportAsync(user ID, filters)
       -> AppDbContext (owned, filtered, ordered TimeEntry query)
       -> UserSettingsService / TimeZoneCatalog (current account zone)
       -> CsvWriter (bytes)
  -> File(byte[], "text/csv; charset=utf-8", filename)
```

`CsvExportService` owns query composition, eager-loading category/tags, local display conversion, duration formatting, formula neutralization, and file-name date selection. It must not call `TimeAggregationService`: aggregation clips intervals and would produce incorrect detail rows. `TimeEntryService.ListAsync` stays paginated for the interactive list; the export service may share a small filter/query helper only if it preserves the separate pagination and export contracts.

`TimeEntryApiController` stays thin: bind/validate query values, derive user identity, call the export service, and return the file. Register `CsvExportService` as scoped. No schema migration or persisted export record is required.

## Error handling and logging

Expected client-visible outcomes are:

| Situation | HTTP/result |
| --- | --- |
| Valid request, including zero rows | `200` attachment |
| Invalid bounded date range | `400` `INVALID_TIME_RANGE` Problem Details |
| Invalid model binding | `400` validation Problem Details |
| Unauthenticated request | `401` |
| Unexpected export/database failure | `500` generic Problem Details |

Unexpected failures must be logged through the established application logging path as a CSV export failure, with safe operational context such as authenticated user ID, range presence, and exception. Never log CSV cell content, authentication cookies, JWTs, database credentials, or the generated file body.

## Tests and acceptance criteria

Add focused unit tests for `CsvWriter` and integration tests for `CsvExportService`/the API path using the project’s shared SQLite test fixture. Coverage must demonstrate:

1. UTF-8 BOM, header order, CRLF records, commas/quotes/newlines, and formula-neutralized user text are serialized safely.
2. An owned entry includes local converted date/times, IANA zone, full UTC duration, category fallback, and deterministic tag order.
3. UTC-to-local conversion is correct for `Asia/Taipei` and for a DST-observing supported zone; DST does not alter the exported duration.
4. A cross-midnight entry is returned once when it overlaps the selected range and keeps its original start, end, and duration.
5. Category, text, and half-open range filters match the history-list semantics; pagination does not limit export.
6. A different user’s entries never appear, including when IDs or filters are guessed.
7. Empty results return a valid header-only CSV, and invalid ranges return the documented 400 response.

## Consistency with the current project

This design reuses the current `/api/time-entries` filter names, ownership predicate, descending ordering, `TimeEntryResponse` domain fields, `UserSettingsService`, `TimeZoneCatalog`, and the established UTC storage rule. It intentionally does not reuse `TimeAggregationService` because export is detail-oriented rather than interval aggregation. The only identified prerequisite adjustment is to make History's bounded preset calculations use the saved account zone so that its visible list and its exported CSV refer to exactly the same local dates.
