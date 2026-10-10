export type SearchKind = 'vehicle' | 'order' | 'node';

export interface SearchItem {
  kind: SearchKind;
  id: string;
  title: string;
  subtitle: string;
  /** 用来匹配的字段，已转小写 */
  keys: string[];
}

export interface SearchHit extends SearchItem {
  score: number;
}

export const SEARCH_KIND_LABELS: Record<SearchKind, string> = {
  vehicle: '车辆',
  order: '任务',
  node: '站点'
};

function scoreKey(key: string, query: string, digitsOnly: boolean): number {
  if (!key) return 0;
  if (key === query) return 100;
  if (key.startsWith(query)) return 80;
  if (digitsOnly) {
    // 车辆 25 匹配 bench-amr-25，站点 146 不应被 1146 抢先
    if (key.endsWith(`-${query}`) || key.endsWith(`_${query}`)) return 90;
    const parts = key.split(/[^0-9]+/).filter(Boolean);
    if (parts.length && parts[parts.length - 1] === query) return 76;
  }
  const at = key.indexOf(query);
  if (at > 0) return 50 - Math.min(at, 20) * 0.5;
  return 0;
}

export function searchItems(items: SearchItem[], rawQuery: string, limitPerKind = 5): SearchHit[] {
  const query = rawQuery.trim().toLowerCase();
  if (!query) return [];
  const digitsOnly = /^\d+$/.test(query);
  const hits: SearchHit[] = [];
  for (const item of items) {
    let best = 0;
    for (const key of item.keys) {
      const score = scoreKey(key, query, digitsOnly);
      if (score > best) best = score;
    }
    if (best > 0) hits.push({ ...item, score: best });
  }
  const order: SearchKind[] = ['vehicle', 'order', 'node'];
  const out: SearchHit[] = [];
  for (const kind of order) {
    const group = hits
      .filter(hit => hit.kind === kind)
      .sort((a, b) => b.score - a.score || a.title.localeCompare(b.title, 'zh-CN', { numeric: true }))
      .slice(0, limitPerKind);
    out.push(...group);
  }
  return out;
}
