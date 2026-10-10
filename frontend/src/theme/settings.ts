/** Default theme settings */
export const themeSettings: App.Theme.ThemeSetting = {
  themeScheme: 'light',
  grayscale: false,
  colourWeakness: false,
  recommendColor: false,
  themeColor: '#0ea5e9',
  themeRadius: 8,
  otherColor: {
    info: '#0ea5e9',
    success: '#10b981',
    warning: '#f59e0b',
    error: '#ef4444'
  },
  isInfoFollowPrimary: true,
  layout: {
    mode: 'vertical',
    scrollMode: 'content'
  },
  page: {
    animate: true,
    animateMode: 'fade-slide'
  },
  header: {
    height: 56,
    breadcrumb: {
      visible: true,
      showIcon: true
    },
    multilingual: {
      visible: true
    },
    globalSearch: {
      visible: true
    }
  },
  tab: {
    visible: true,
    cache: true,
    height: 44,
    mode: 'chrome',
    closeTabByMiddleClick: false
  },
  fixedHeaderAndTab: true,
  sider: {
    inverted: false,
    width: 220,
    collapsedWidth: 64,
    mixWidth: 90,
    mixCollapsedWidth: 64,
    mixChildMenuWidth: 200,
    autoSelectFirstMenu: false
  },
  footer: {
    visible: true,
    fixed: false,
    height: 32,
    right: true
  },
  watermark: {
    visible: false,
    text: 'SIASUN FMS',
    enableUserName: false,
    enableTime: false,
    timeFormat: 'YYYY-MM-DD HH:mm'
  },
  tokens: {
    light: {
      colors: {
        container: 'rgb(255, 255, 255)',
        layout: 'rgb(248, 250, 252)',
        inverted: 'rgb(11, 15, 23)',
        'base-text': 'rgb(15, 23, 42)'
      },
      boxShadow: {
        header: '0 1px 3px rgb(0, 0, 0, 0.05)',
        sider: '1px 0 3px 0 rgb(0, 0, 0, 0.05)',
        tab: '0 1px 2px rgb(0, 0, 0, 0.05)'
      }
    },
    dark: {
      colors: {
        container: 'rgb(17, 21, 31)',
        layout: 'rgb(11, 15, 23)',
        inverted: 'rgb(255, 255, 255)',
        'base-text': 'rgb(241, 245, 249)'
      },
      boxShadow: {
        header: '0 1px 3px rgb(0, 0, 0, 0.3)',
        sider: '1px 0 3px 0 rgb(0, 0, 0, 0.3)',
        tab: '0 1px 2px rgb(0, 0, 0, 0.3)'
      }
    }
  }
};

/**
 * Override theme settings
 *
 * If publish new version, use `overrideThemeSettings` to override certain theme settings
 */
export const overrideThemeSettings: Partial<App.Theme.ThemeSetting> = {};
