export function formatDuration(totalSeconds) {
  const safeSeconds = Math.max(0, Math.round(Number(totalSeconds) || 0));
  const minutes = Math.round(safeSeconds / 60);
  return `${Math.floor(minutes / 60)} 小時 ${minutes % 60} 分`;
}

export function isValidDateRange(start, end) {
  return Boolean(start && end && start <= end);
}
