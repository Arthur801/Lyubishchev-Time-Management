const snapshots = {
  today: {
    label: '今天 · 2026 年 9 月 17 日',
    totalMinutes: 300,
    change: '比昨天多 45 分',
    daily: [{ label: '今天', minutes: 300 }],
    categories: [
      { name: '工作', minutes: 160, color: '#e5533d' },
      { name: '學習', minutes: 100, color: '#51758c' },
      { name: '未分類', minutes: 40, color: '#a6adb7' }
    ],
    tags: [
      { name: '深度工作', minutes: 120 },
      { name: '規劃', minutes: 90 },
      { name: '閱讀', minutes: 70 },
      { name: '整理', minutes: 50 }
    ],
    recent: [
      { name: 'Dashboard 前端規劃', category: '工作', duration: '1 小時 40 分', time: '09:20 – 11:00' },
      { name: '閱讀設計筆記', category: '學習', duration: '1 小時 10 分', time: '07:40 – 08:50' }
    ]
  },
  week: {
    label: '本週 · 9 月 14 日至 9 月 20 日',
    totalMinutes: 1590,
    change: '比上週多 2 小時 10 分',
    daily: [
      { label: '一', minutes: 240 }, { label: '二', minutes: 310 }, { label: '三', minutes: 190 },
      { label: '四', minutes: 300 }, { label: '五', minutes: 220 }, { label: '六', minutes: 180 }, { label: '日', minutes: 150 }
    ],
    categories: [
      { name: '工作', minutes: 780, color: '#e5533d' },
      { name: '學習', minutes: 470, color: '#51758c' },
      { name: '生活', minutes: 220, color: '#c69650' },
      { name: '未分類', minutes: 120, color: '#a6adb7' }
    ],
    tags: [
      { name: '深度工作', minutes: 840 }, { name: '規劃', minutes: 560 },
      { name: '閱讀', minutes: 440 }, { name: '會議', minutes: 220 }
    ],
    recent: [
      { name: '設計系統盤點', category: '工作', duration: '2 小時 20 分', time: '週三 14:00 – 16:20' },
      { name: '閱讀技術文章', category: '學習', duration: '1 小時 30 分', time: '週二 09:10 – 10:40' },
      { name: '整理每週筆記', category: '生活', duration: '45 分', time: '週一 18:10 – 18:55' }
    ]
  },
  month: {
    label: '本月 · 2026 年 9 月',
    totalMinutes: 5640,
    change: '比上月多 6 小時 30 分',
    daily: [
      { label: '第 1 週', minutes: 1380 }, { label: '第 2 週', minutes: 1500 },
      { label: '第 3 週', minutes: 1590 }, { label: '目前', minutes: 1170 }
    ],
    categories: [
      { name: '工作', minutes: 2880, color: '#e5533d' }, { name: '學習', minutes: 1620, color: '#51758c' },
      { name: '生活', minutes: 720, color: '#c69650' }, { name: '未分類', minutes: 420, color: '#a6adb7' }
    ],
    tags: [
      { name: '深度工作', minutes: 3010 }, { name: '規劃', minutes: 2020 },
      { name: '閱讀', minutes: 1440 }, { name: '會議', minutes: 890 }
    ],
    recent: [
      { name: '月度回顧', category: '工作', duration: '1 小時 20 分', time: '9 月 16 日 16:00 – 17:20' },
      { name: '閱讀研究資料', category: '學習', duration: '2 小時', time: '9 月 15 日 10:00 – 12:00' }
    ]
  }
};

export function formatDuration(minutes) {
  const safeMinutes = Math.max(0, Math.round(Number(minutes) || 0));
  return `${Math.floor(safeMinutes / 60)} 小時 ${safeMinutes % 60} 分`;
}

export function getPresetSnapshot(preset) {
  const snapshot = snapshots[preset] ?? snapshots.today;
  return structuredClone(snapshot);
}

export function isValidDateRange(start, end) {
  return Boolean(start && end && start <= end);
}

export function addCompletedEntry(snapshot, entry) {
  const updated = structuredClone(snapshot);
  const minutes = Math.max(1, Math.round(Number(entry.minutes) || 0));
  updated.totalMinutes += minutes;
  updated.daily[updated.daily.length - 1].minutes += minutes;

  const category = updated.categories.find(item => item.name === entry.category);
  if (category) category.minutes += minutes;
  else updated.categories.push({ name: entry.category || '未分類', minutes, color: '#a6adb7' });

  entry.tags.forEach(name => {
    const tag = updated.tags.find(item => item.name === name);
    if (tag) tag.minutes += minutes;
    else updated.tags.push({ name, minutes });
  });

  updated.recent.unshift({
    name: entry.name || '未命名活動',
    category: entry.category || '未分類',
    duration: formatDuration(minutes),
    time: '剛剛結束'
  });
  return updated;
}
