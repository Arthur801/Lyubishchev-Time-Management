// Pure elapsed-duration formatter for the Timer card, split out so it's unit-testable in Node.
// This is deliberately timezone-independent: it formats a plain elapsed-second count, not a
// calendar date, so there is no local/account timezone to get right here.
export function formatClock(totalSeconds) {
  const safeSeconds = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(safeSeconds / 3600);
  const minutes = Math.floor((safeSeconds % 3600) / 60);
  const seconds = safeSeconds % 60;
  const pad = (n) => String(n).padStart(2, '0');
  return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
}
