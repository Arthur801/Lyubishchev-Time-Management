// Pure timezone conversion helpers shared by pages that let the user read or type a wall-clock
// time (History List's filters and entry modal). All persisted timestamps are UTC; these
// functions translate between that UTC value and the user's *account* timezone (from
// GET /api/settings/timezone), never the browser's own local timezone, which can differ from it.

function getZonedParts(date, timeZone) {
  const formatter = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
  const parts = {};
  for (const { type, value } of formatter.formatToParts(date)) {
    if (type !== 'literal') parts[type] = Number(value);
  }
  return parts;
}

function addCalendarDays(year, month, day, delta) {
  const anchor = new Date(Date.UTC(year, month - 1, day));
  anchor.setUTCDate(anchor.getUTCDate() + delta);
  return { year: anchor.getUTCFullYear(), month: anchor.getUTCMonth() + 1, day: anchor.getUTCDate() };
}

function weekdayOf(year, month, day) {
  return new Date(Date.UTC(year, month - 1, day)).getUTCDay();
}

const pad = (n) => String(n).padStart(2, '0');

// Converts a local calendar date/time in `timeZone` to the UTC instant it represents. Guesses the
// instant assuming UTC, reads that guess's actual offset in `timeZone`, and corrects for it — one
// correction is exact except within a DST transition's own window, which manual time entry does
// not need to resolve to the minute.
export function zonedTimeToUtcIso(year, month, day, hour, minute, timeZone) {
  const utcGuessMs = Date.UTC(year, month - 1, day, hour, minute);
  const zoned = getZonedParts(new Date(utcGuessMs), timeZone);
  const zonedAsUtcMs = Date.UTC(zoned.year, zoned.month - 1, zoned.day, zoned.hour, zoned.minute, zoned.second);
  return new Date(utcGuessMs - (zonedAsUtcMs - utcGuessMs)).toISOString();
}

// value: "YYYY-MM-DDTHH:mm" (the `<input type="datetime-local">` format), interpreted as a
// wall-clock time in `timeZone`.
export function dateTimeLocalValueToUtcIso(value, timeZone) {
  const [datePart, timePart] = value.split('T');
  const [year, month, day] = datePart.split('-').map(Number);
  const [hour, minute] = timePart.split(':').map(Number);
  return zonedTimeToUtcIso(year, month, day, hour, minute, timeZone);
}

export function isoToDateTimeLocalValue(isoString, timeZone) {
  const { year, month, day, hour, minute } = getZonedParts(new Date(isoString), timeZone);
  return `${year}-${pad(month)}-${pad(day)}T${pad(hour)}:${pad(minute)}`;
}

export function formatTimeOfDayInZone(isoString, timeZone) {
  const { hour, minute } = getZonedParts(new Date(isoString), timeZone);
  return `${pad(hour)}:${pad(minute)}`;
}

export function zonedDateKey(isoString, timeZone) {
  const { year, month, day } = getZonedParts(new Date(isoString), timeZone);
  return `${year}-${pad(month)}-${pad(day)}`;
}

// Exposes the Intl-backed zoned calendar date (and time-of-day) for callers, such as the Calendar
// view, that need to anchor navigation on "today in the account timezone" rather than the
// instant->UTC-string helpers above.
export function getZonedDateParts(date, timeZone) {
  return getZonedParts(date, timeZone);
}

export function addZonedDays(year, month, day, delta) {
  return addCalendarDays(year, month, day, delta);
}

export function weekdayOfDate(year, month, day) {
  return weekdayOf(year, month, day);
}

// The [startUtc, endUtc) instant bounds of one local calendar day in `timeZone`, as UTC ISO
// strings — the Calendar view's per-day query window and entry-splitting boundary.
export function zonedDayUtcBounds(year, month, day, timeZone) {
  const startUtc = zonedTimeToUtcIso(year, month, day, 0, 0, timeZone);
  const next = addCalendarDays(year, month, day, 1);
  const endUtc = zonedTimeToUtcIso(next.year, next.month, next.day, 0, 0, timeZone);
  return { startUtc, endUtc };
}

// Sunday-start day/week/month boundaries computed in `timeZone`, returned as UTC ISO instants
// ([startUtc, endUtc)). `referenceDate` defaults to now and exists so tests can pin "today".
export function getZonedRangeForPreset(preset, timeZone, referenceDate = new Date()) {
  const today = getZonedParts(referenceDate, timeZone);
  const startOfUtcDay = ({ year, month, day }) => zonedTimeToUtcIso(year, month, day, 0, 0, timeZone);

  if (preset === 'today') {
    const tomorrow = addCalendarDays(today.year, today.month, today.day, 1);
    return { startUtc: startOfUtcDay(today), endUtc: startOfUtcDay(tomorrow) };
  }

  if (preset === 'week') {
    const weekday = weekdayOf(today.year, today.month, today.day);
    const weekStart = addCalendarDays(today.year, today.month, today.day, -weekday);
    const weekEnd = addCalendarDays(weekStart.year, weekStart.month, weekStart.day, 7);
    return { startUtc: startOfUtcDay(weekStart), endUtc: startOfUtcDay(weekEnd) };
  }

  if (preset === 'month') {
    const monthStart = { year: today.year, month: today.month, day: 1 };
    const nextMonthStart = today.month === 12
      ? { year: today.year + 1, month: 1, day: 1 }
      : { year: today.year, month: today.month + 1, day: 1 };
    return { startUtc: startOfUtcDay(monthStart), endUtc: startOfUtcDay(nextMonthStart) };
  }

  return { startUtc: null, endUtc: null };
}
