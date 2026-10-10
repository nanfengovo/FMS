import type { MapBookmark } from './constants';

const PREFIX = 'fms.map.bookmarks.v1:';
const LIMIT = 24;

function storageKey(mapName: string) {
  return `${PREFIX}${mapName || 'default'}`;
}

export function loadBookmarks(mapName: string): MapBookmark[] {
  try {
    const raw = window.localStorage.getItem(storageKey(mapName));
    if (!raw) return [];
    const parsed = JSON.parse(raw) as MapBookmark[];
    return Array.isArray(parsed)
      ? parsed.filter(item => item && typeof item.x === 'number' && typeof item.y === 'number' && typeof item.scale === 'number')
      : [];
  } catch {
    return [];
  }
}

export function saveBookmarks(mapName: string, list: MapBookmark[]) {
  try {
    window.localStorage.setItem(storageKey(mapName), JSON.stringify(list.slice(0, LIMIT)));
  } catch {
    // 存储空间不足或被禁用时忽略，书签只在本次会话内有效
  }
}

export function makeBookmarkId() {
  return `bm-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
}

export const BOOKMARK_LIMIT = LIMIT;
