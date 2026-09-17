// Pure Calendar-view layout helpers, split out from calendar.js so they're unit-testable in Node
// without a DOM. Everything here operates on plain numbers and UTC ISO strings that calendar.js
// has already resolved to the account timezone (via timezone.mjs) — nothing here calls Intl.

// Splits one TimeEntry into per-visible-day segments, each clipped to that day's local bounds.
// `days`: [{ dateKey, startUtc, endUtc }], the account-timezone-local [start, end) of each visible
// day. A segment's start/end are expressed as minutes since that day's local midnight, so an entry
// crossing a day boundary yields one segment per day it actually overlaps.
export function splitEntryIntoDaySegments(entry, days) {
  const entryStart = new Date(entry.startTimeUtc).getTime();
  const entryEnd = new Date(entry.endTimeUtc).getTime();
  const segments = [];

  for (const day of days) {
    const dayStart = new Date(day.startUtc).getTime();
    const dayEnd = new Date(day.endUtc).getTime();
    const overlapStart = Math.max(entryStart, dayStart);
    const overlapEnd = Math.min(entryEnd, dayEnd);
    if (overlapEnd <= overlapStart) continue;

    segments.push({
      entry,
      dateKey: day.dateKey,
      startMinutes: (overlapStart - dayStart) / 60000,
      endMinutes: (overlapEnd - dayStart) / 60000,
    });
  }

  return segments;
}

// Assigns each of one day's segments to a column so overlapping segments never share one —
// greedy interval coloring: process earliest-start first, reuse the first column whose
// previously-placed segment has already ended, otherwise open a new column. Every segment stays
// visible (never hidden or merged); `columnCount` lets the renderer divide the day's width evenly.
export function layoutOverlappingSegments(segments) {
  const sorted = [...segments].sort((a, b) => a.startMinutes - b.startMinutes);
  const columnEnds = [];

  const placed = sorted.map((segment) => {
    let column = columnEnds.findIndex((end) => end <= segment.startMinutes);
    if (column === -1) {
      column = columnEnds.length;
      columnEnds.push(segment.endMinutes);
    } else {
      columnEnds[column] = segment.endMinutes;
    }
    return { ...segment, column };
  });

  const columnCount = columnEnds.length || 1;
  return placed.map((segment) => ({ ...segment, columnCount }));
}
