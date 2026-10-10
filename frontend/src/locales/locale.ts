import zhCN from './langs/zh-cn';
import zhTW from './langs/zh-tw';
import enUS from './langs/en-us';
import jaJP from './langs/ja-jp';
import koKR from './langs/ko-kr';

const locales: Record<App.I18n.LangType, App.I18n.Schema> = {
  'zh-CN': zhCN,
  'zh-TW': zhTW,
  'en-US': enUS,
  'ja-JP': jaJP,
  'ko-KR': koKR
};

export default locales;
