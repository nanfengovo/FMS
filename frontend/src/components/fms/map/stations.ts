import type { Graphics } from 'pixi.js';
import { stationColor } from './palette';
import type { Palette, StationKind } from './palette';

export type { StationKind } from './palette';

export function stationKindOf(type?: string | null): StationKind {
  const t = (type || '').toLowerCase();
  if (t === 'charge' || t === 'chargepoint') return 'charge';
  if (t === 'pick' || t === 'drop' || t === 'jobpoint') return 'job';
  if (t === 'park' || t === 'rest' || t === 'standbypoint') return 'standby';
  if (t === 'pullover' || t === 'pulloverpoint') return 'pullover';
  return 'landmark';
}

export const STATION_LABELS: Record<StationKind, string> = {
  charge: '充电点',
  job: '作业工位',
  standby: '待命点',
  pullover: '避让点',
  landmark: '地标'
};

/** 图标的屏幕半径（像素），用于描边、悬停、选中圈的尺寸换算 */
export function stationRadius(kind: StationKind, tier: number) {
  if (kind === 'landmark') return tier >= 3 ? 2.8 : 2.2;
  if (kind === 'charge') return 6.6;
  if (kind === 'pullover') return 6.4;
  return 6;
}

function shapePath(g: Graphics, kind: StationKind, x: number, y: number, r: number) {
  switch (kind) {
    case 'job':
      g.roundRect(x - r * 0.92, y - r * 0.92, r * 1.84, r * 1.84, r * 0.3);
      break;
    case 'standby':
      g.poly([x, y - r * 1.12, x + r * 1.12, y, x, y + r * 1.12, x - r * 1.12, y]);
      break;
    case 'pullover':
      g.poly([x, y - r * 1.1, x + r * 1.08, y + r * 0.86, x - r * 1.08, y + r * 0.86]);
      break;
    default:
      g.circle(x, y, r);
  }
}

/**
 * 在 (x, y) 画一个站点图标。u 为每屏幕像素对应的局部像素，保证缩放后图标大小不变。
 * 先画一圈与底色一致的外框把线条隔开，再画色块和内部符号。
 */
export function drawStation(g: Graphics, kind: StationKind, x: number, y: number, u: number, palette: Palette, tier: number) {
  const r = stationRadius(kind, tier) * u;
  const color = stationColor(kind, palette.dark);
  const ink = palette.dark ? 0x0d1116 : 0xffffff;

  if (kind === 'landmark') {
    if (tier >= 3) {
      g.circle(x, y, r + u * 1.4);
      g.fill({ color: palette.halo, alpha: 0.9 });
    }
    g.circle(x, y, r);
    g.fill({ color, alpha: tier >= 3 ? 1 : 0.85 });
    return;
  }

  shapePath(g, kind, x, y, r);
  g.stroke({ width: u * 3.2, color: palette.halo, alpha: 0.92, join: 'round' });
  shapePath(g, kind, x, y, r);
  g.fill({ color, alpha: 1 });

  if (tier < 2 && r < u * 5) return;

  switch (kind) {
    case 'charge':
      g.poly([
        x + r * 0.22,
        y - r * 0.68,
        x - r * 0.42,
        y + r * 0.1,
        x - r * 0.04,
        y + r * 0.1,
        x - r * 0.2,
        y + r * 0.68,
        x + r * 0.44,
        y - r * 0.12,
        x + r * 0.05,
        y - r * 0.12
      ]);
      g.fill({ color: ink, alpha: 0.96 });
      break;
    case 'job':
      g.roundRect(x - r * 0.38, y - r * 0.38, r * 0.76, r * 0.76, r * 0.12);
      g.stroke({ width: u * 1.3, color: ink, alpha: 0.95 });
      break;
    case 'standby':
      g.circle(x, y, r * 0.3);
      g.fill({ color: ink, alpha: 0.96 });
      break;
    case 'pullover':
      g.moveTo(x - r * 0.42, y + r * 0.42);
      g.lineTo(x, y - r * 0.12);
      g.lineTo(x + r * 0.42, y + r * 0.42);
      g.stroke({ width: u * 1.3, color: ink, alpha: 0.96, cap: 'round', join: 'round' });
      break;
    default:
      break;
  }
}
