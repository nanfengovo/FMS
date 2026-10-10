import { locale } from 'dayjs';
import 'dayjs/locale/zh-cn';
import 'dayjs/locale/zh-tw';
import 'dayjs/locale/en';
import 'dayjs/locale/ja';
import 'dayjs/locale/ko';
import { localStg } from '@/utils/storage';

/**
 * Set dayjs locale
 *
 * @param lang
 */
export function setDayjsLocale(lang: App.I18n.LangType = 'zh-CN') {
  const localMap: Record<App.I18n.LangType, string> = {
    'zh-CN': 'zh-cn',
    'zh-TW': 'zh-tw',
    'en-US': 'en',
    'ja-JP': 'ja',
    'ko-KR': 'ko'
  };

  const l = lang || localStg.get('lang') || 'zh-CN';

  locale(localMap[l] || 'zh-cn');
}
