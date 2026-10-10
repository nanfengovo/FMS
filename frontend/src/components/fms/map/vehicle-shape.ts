import type { Graphics } from 'pixi.js';
import { SCALE, VEHICLE_LENGTH_M, VEHICLE_WIDTH_M } from './constants';
import { batteryColor, vehicleColor } from './palette';
import type { Palette } from './palette';

export interface VehicleDrawContext {
  /** 每屏幕像素对应的局部像素 */
  u: number;
  pxPerMeter: number;
  tier: number;
  palette: Palette;
  selected: boolean;
  hovered: boolean;
}

export interface VehicleMetrics {
  /** 车体长宽（局部像素） */
  length: number;
  width: number;
  /** 相对真实尺寸的放大系数，低缩放时为了看得见会大于 1 */
  boost: number;
}

export function vehicleMetrics(pxPerMeter: number, tier: number): VehicleMetrics {
  const minScreenLength = tier <= 0 ? 8 : tier === 1 ? 13 : 15;
  const realScreenLength = VEHICLE_LENGTH_M * pxPerMeter;
  const boost = Math.max(1, minScreenLength / Math.max(realScreenLength, 0.001));
  return {
    length: VEHICLE_LENGTH_M * SCALE * boost,
    width: VEHICLE_WIDTH_M * SCALE * boost,
    boost
  };
}

/** 画车体。坐标原点在车体中心，车头朝 +X。 */
export function drawVehicleBody(g: Graphics, v: Fms.VehicleDto, ctx: VehicleDrawContext) {
  g.clear();
  const { u, tier, palette } = ctx;
  const color = vehicleColor(v.status, palette.dark);

  if (tier <= 0) {
    const r = u * 3.6;
    g.circle(0, 0, r + u * 1.3);
    g.fill({ color: palette.halo, alpha: 0.9 });
    g.circle(0, 0, r);
    g.fill({ color, alpha: 1 });
    g.moveTo(r * 0.4, 0);
    g.lineTo(r + u * 3.4, 0);
    g.stroke({ width: u * 1.6, color, alpha: 1, cap: 'round' });
    if (ctx.selected) {
      g.circle(0, 0, r + u * 5);
      g.stroke({ width: u * 1.4, color: palette.select, alpha: 1 });
    }
    return;
  }

  const m = vehicleMetrics(ctx.pxPerMeter, tier);
  const hl = m.length / 2;
  const hw = m.width / 2;
  const chamfer = Math.min(m.length * 0.2, m.width * 0.34);
  const round = m.width * 0.2;
  const outline = palette.dark ? 0x05080b : 0x0b1a2b;

  // 驱动轮先画，探出车体一点
  if (tier >= 3) {
    const wheelW = m.width * 0.15;
    const wheelL = m.length * 0.3;
    g.roundRect(-hl * 0.06, -hw - wheelW * 0.38, wheelL, wheelW, wheelW * 0.3);
    g.roundRect(-hl * 0.06, hw - wheelW * 0.62, wheelL, wheelW, wheelW * 0.3);
    g.fill({ color: 0x1d252e, alpha: 0.95 });
  }

  const body = [
    -hl + round,
    -hw,
    hl - chamfer,
    -hw,
    hl,
    -hw + chamfer * 0.9,
    hl,
    hw - chamfer * 0.9,
    hl - chamfer,
    hw,
    -hl + round,
    hw,
    -hl,
    hw - round,
    -hl,
    -hw + round
  ];
  g.poly(body);
  g.fill({ color, alpha: 1 });
  g.stroke({ width: u * 1.1, color: outline, alpha: palette.dark ? 0.7 : 0.5, join: 'round' });

  if (tier >= 2) {
    g.roundRect(-hl * 0.62, -hw * 0.6, hl * 1.0, hw * 1.2, m.width * 0.1);
    g.fill({ color: 0xffffff, alpha: 0.14 });
  }

  if (tier >= 3) {
    // 前灯与尾灯
    const lamp = m.width * 0.12;
    g.roundRect(hl - chamfer * 0.55, -hw + m.width * 0.1, lamp * 0.9, lamp, lamp * 0.3);
    g.roundRect(hl - chamfer * 0.55, hw - m.width * 0.1 - lamp, lamp * 0.9, lamp, lamp * 0.3);
    g.fill({ color: 0xfff3c4, alpha: 0.95 });
    g.roundRect(-hl + u * 0.6, -hw + m.width * 0.12, lamp * 0.6, lamp * 0.9, lamp * 0.2);
    g.roundRect(-hl + u * 0.6, hw - m.width * 0.12 - lamp * 0.9, lamp * 0.6, lamp * 0.9, lamp * 0.2);
    g.fill({ color: 0xff6b6b, alpha: 0.9 });
  }

  // 车头方向
  g.moveTo(hl * 0.2, -hw * 0.36);
  g.lineTo(hl * 0.56, 0);
  g.lineTo(hl * 0.2, hw * 0.36);
  g.stroke({ width: u * (tier >= 2 ? 1.7 : 1.4), color: 0xffffff, alpha: 0.96, cap: 'round', join: 'round' });

  if (v.holdingPayload) {
    const side = Math.min(m.length, m.width) * 0.54;
    const cx = -hl * 0.28;
    g.roundRect(cx - side / 2, -side / 2, side, side, side * 0.1);
    g.fill({ color: 0xece4d2, alpha: 1 });
    g.stroke({ width: u * 1, color: 0x5b4f3a, alpha: 0.9 });
    if (tier >= 2) {
      g.moveTo(cx - side / 2, 0);
      g.lineTo(cx + side / 2, 0);
      g.moveTo(cx, -side / 2);
      g.lineTo(cx, side / 2);
      g.stroke({ width: u * 0.8, color: 0x5b4f3a, alpha: 0.55 });
    }
  }

  const pad = u * 4;
  if (ctx.selected) {
    const arm = Math.min(u * 6, hw * 0.9);
    const x0 = -hl - pad;
    const x1 = hl + pad;
    const y0 = -hw - pad;
    const y1 = hw + pad;
    g.moveTo(x0, y0 + arm).lineTo(x0, y0).lineTo(x0 + arm, y0);
    g.moveTo(x1 - arm, y0).lineTo(x1, y0).lineTo(x1, y0 + arm);
    g.moveTo(x1, y1 - arm).lineTo(x1, y1).lineTo(x1 - arm, y1);
    g.moveTo(x0 + arm, y1).lineTo(x0, y1).lineTo(x0, y1 - arm);
    g.stroke({ width: u * 1.9, color: palette.select, alpha: 1, cap: 'round', join: 'round' });
  } else if (ctx.hovered) {
    g.roundRect(-hl - pad * 0.8, -hw - pad * 0.8, m.length + pad * 1.6, m.width + pad * 1.6, u * 3);
    g.stroke({ width: u * 1.2, color: palette.hover, alpha: 0.85 });
  }
}

/** 电量环：围绕车体的细弧，起点在屏幕正上方，因此需要放在反向旋转的容器里 */
export function drawBatteryRing(g: Graphics, v: Fms.VehicleDto, ctx: VehicleDrawContext) {
  g.clear();
  if (ctx.tier < 3) return;
  const m = vehicleMetrics(ctx.pxPerMeter, ctx.tier);
  const radius = m.length / 2 + ctx.u * 7;
  const fraction = Math.max(0, Math.min(1, (v.batteryPercent ?? 0) / 100));
  g.circle(0, 0, radius);
  g.stroke({ width: ctx.u * 1.6, color: ctx.palette.laneEdge, alpha: 0.35 });
  if (fraction > 0.004) {
    g.arc(0, 0, radius, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * fraction);
    g.stroke({ width: ctx.u * 1.9, color: batteryColor(v.batteryPercent, ctx.palette.dark), alpha: 0.95, cap: 'round' });
  }
}

/** 状态徽标：故障、充电、等待互斥区、暂停。画在车辆正上方，保持正向。 */
export function drawStatusBadge(g: Graphics, v: Fms.VehicleDto, ctx: VehicleDrawContext, phase: number) {
  g.clear();
  if (ctx.tier < 2) return;
  const status = v.status;
  const m = vehicleMetrics(ctx.pxPerMeter, ctx.tier);
  const u = ctx.u;
  const cy = -(m.length / 2 + u * (ctx.tier >= 3 ? 16 : 10));
  const p = ctx.palette;

  if (status === 'Error' || status === 'Fault' || status === 'Estop') {
    const pulse = 0.55 + 0.45 * Math.sin(phase * Math.PI * 2);
    g.circle(0, 0, m.length / 2 + u * (7 + pulse * 5));
    g.stroke({ width: u * 1.4, color: p.danger, alpha: 0.5 * (1 - pulse * 0.6) });
    g.poly([0, cy - u * 5.2, u * 5.4, cy + u * 4, -u * 5.4, cy + u * 4]);
    g.fill({ color: p.danger, alpha: 1 });
    g.stroke({ width: u * 1.2, color: p.halo, alpha: 0.95, join: 'round' });
    g.moveTo(0, cy - u * 2);
    g.lineTo(0, cy + u * 0.8);
    g.stroke({ width: u * 1.4, color: 0xffffff, alpha: 1, cap: 'round' });
    g.circle(0, cy + u * 2.5, u * 0.8);
    g.fill({ color: 0xffffff, alpha: 1 });
  } else if (status === 'Charging') {
    g.circle(0, cy, u * 5.2);
    g.fill({ color: vehicleColor('Charging', p.dark), alpha: 1 });
    g.stroke({ width: u * 1.2, color: p.halo, alpha: 0.95 });
    g.poly([u * 0.8, cy - u * 3.4, -u * 2, cy + u * 0.4, -u * 0.2, cy + u * 0.4, -u * 0.8, cy + u * 3.4, u * 2, cy - u * 0.6, u * 0.2, cy - u * 0.6]);
    g.fill({ color: 0xffffff, alpha: 1 });
  } else if (status === 'WaitingLock') {
    g.circle(0, cy, u * 5.2);
    g.fill({ color: vehicleColor('WaitingLock', p.dark), alpha: 1 });
    g.stroke({ width: u * 1.2, color: p.halo, alpha: 0.95 });
    g.moveTo(0, cy - u * 2.8);
    g.lineTo(0, cy);
    g.lineTo(u * 2.2, cy + u * 1.4);
    g.stroke({ width: u * 1.3, color: 0xffffff, alpha: 1, cap: 'round', join: 'round' });
  } else if (status === 'Paused') {
    g.circle(0, cy, u * 5.2);
    g.fill({ color: vehicleColor('Paused', p.dark), alpha: 1 });
    g.stroke({ width: u * 1.2, color: p.halo, alpha: 0.95 });
    g.roundRect(-u * 2.4, cy - u * 2.6, u * 1.7, u * 5.2, u * 0.4);
    g.roundRect(u * 0.7, cy - u * 2.6, u * 1.7, u * 5.2, u * 0.4);
    g.fill({ color: 0xffffff, alpha: 1 });
  }
}
