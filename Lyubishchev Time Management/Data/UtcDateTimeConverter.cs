using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lyubishchev_Time_Management.Data;

// MySQL's DATETIME columns carry no timezone offset, so EF Core reads every value back with
// DateTimeKind.Unspecified even though it was written from a Kind=Utc value. System.Text.Json only
// appends the "Z" suffix for Kind=Utc, so an Unspecified value round-trips to the frontend as e.g.
// "2026-09-17T06:00:00" — every `new Date(...)` call there then misreads it as browser-local time
// instead of UTC, shifting displayed times by the browser's own offset. Every DateTime column in
// this app is UTC by convention (see AGENTS.md's Timezone rules), so re-tagging Kind=Utc on every
// read is always correct here.
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
