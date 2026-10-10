import { dateEnUS, dateJaJP, dateKoKR, dateZhCN, dateZhTW, enUS, jaJP, koKR, zhCN, zhTW } from 'naive-ui';
import type { NDateLocale, NLocale } from 'naive-ui';

export const naiveLocales: Record<App.I18n.LangType, NLocale> = {
  'zh-CN': zhCN,
  'zh-TW': zhTW,
  'en-US': enUS,
  'ja-JP': jaJP,
  'ko-KR': koKR
};

export const naiveDateLocales: Record<App.I18n.LangType, NDateLocale> = {
  'zh-CN': dateZhCN,
  'zh-TW': dateZhTW,
  'en-US': dateEnUS,
  'ja-JP': dateJaJP,
  'ko-KR': dateKoKR
};
