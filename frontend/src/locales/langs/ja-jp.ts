const local: App.I18n.Schema = {
  system: {
    title: 'AMR 知能配車・シミュレーション基盤',
    updateTitle: 'システムバージョン更新通知',
    updateContent: '新しいバージョンが検出されました。今すぐページを更新しますか？',
    updateConfirm: '今すぐ更新',
    updateCancel: '後で'
  },
  common: {
    action: '操作',
    add: '追加',
    addSuccess: '追加に成功しました',
    backToHome: 'ホームに戻る',
    batchDelete: '一括削除',
    cancel: 'キャンセル',
    close: '閉じる',
    check: 'チェック',
    selectAll: '全選択',
    expandColumn: '列を展開',
    columnSetting: '列設定',
    config: '設定',
    confirm: '確認',
    delete: '削除',
    deleteSuccess: '削除に成功しました',
    confirmDelete: '削除してもよろしいですか？',
    edit: '編集',
    warning: '警告',
    error: 'エラー',
    index: '連番',
    keywordSearch: 'キーワードを入力してください',
    logout: 'ログアウト',
    logoutConfirm: 'ログアウトしてもよろしいですか？',
    lookForward: '準備中',
    modify: '変更',
    modifySuccess: '変更に成功しました',
    noData: 'データなし',
    operate: '操作',
    pleaseCheckValue: '入力値が有効か確認してください',
    refresh: '更新',
    reset: 'リセット',
    search: '検索',
    switch: '切り替え',
    tip: 'ヒント',
    trigger: 'トリガー',
    update: '更新',
    updateSuccess: '更新に成功しました',
    userCenter: 'ユーザーセンター',
    yesOrNo: {
      yes: 'はい',
      no: 'いいえ'
    }
  },
  request: {
    logout: 'Logout user after request failed',
    logoutMsg: 'User status is invalid, please log in again',
    logoutWithModal: 'Pop up modal after request failed and then log out user',
    logoutWithModalMsg: 'User status is invalid, please log in again',
    refreshToken: 'The requested token has expired, refresh the token',
    tokenExpired: 'The requested token has expired'
  },
  theme: {
    themeDrawerTitle: 'Theme Configuration',
    tabs: {
      appearance: 'Appearance',
      layout: 'Layout',
      general: 'General',
      preset: 'Preset'
    },
    appearance: {
      themeSchema: {
        title: 'Theme Schema',
        light: 'Light',
        dark: 'Dark',
        auto: 'Follow System'
      },
      grayscale: 'Grayscale',
      colourWeakness: 'Colour Weakness',
      themeColor: {
        title: 'Theme Color',
        primary: 'Primary',
        info: 'Info',
        success: 'Success',
        warning: '警告',
        error: 'エラー',
        followPrimary: 'Follow Primary'
      },
      themeRadius: {
        title: 'Theme Radius'
      },
      recommendColor: 'Apply Recommended Color Algorithm',
      recommendColorDesc: 'The recommended color algorithm refers to',
      preset: {
        title: 'Theme Presets',
        apply: 'Apply',
        applySuccess: 'Preset applied successfully',
        default: {
          name: 'Default Preset',
          desc: 'Default theme preset with balanced settings'
        },
        dark: {
          name: 'Dark Preset',
          desc: 'Dark theme preset for night time usage'
        },
        compact: {
          name: 'Compact Preset',
          desc: 'Compact layout preset for small screens'
        },
        azir: {
          name: "Azir's Preset",
          desc: 'It is a cold and elegant preset that Azir likes'
        }
      }
    },
    layout: {
      layoutMode: {
        title: 'Layout Mode',
        vertical: 'Vertical Mode',
        horizontal: 'Horizontal Mode',
        'vertical-mix': 'Vertical Mix Mode',
        'vertical-hybrid-header-first': 'Left Hybrid Header-First',
        'top-hybrid-sidebar-first': 'Top-Hybrid Sidebar-First',
        'top-hybrid-header-first': 'Top-Hybrid Header-First',
        vertical_detail: 'Vertical menu layout, with the menu on the left and content on the right.',
        'vertical-mix_detail':
          'Vertical mix-menu layout, with the primary menu on the dark left side and the secondary menu on the lighter left side.',
        'vertical-hybrid-header-first_detail':
          'Left hybrid layout, with the primary menu at the top, the secondary menu on the dark left side, and the tertiary menu on the lighter left side.',
        horizontal_detail: 'Horizontal menu layout, with the menu at the top and content below.',
        'top-hybrid-sidebar-first_detail':
          'Top hybrid layout, with the primary menu on the left and the secondary menu at the top.',
        'top-hybrid-header-first_detail':
          'Top hybrid layout, with the primary menu at the top and the secondary menu on the left.'
      },
      tab: {
        title: 'Tab Settings',
        visible: 'Tab Visible',
        cache: 'Tag Bar Info Cache',
        cacheTip: 'Keep the tab bar information after leaving the page',
        height: 'Tab Height',
        mode: {
          title: 'Tab Mode',
          slider: 'Slider',
          chrome: 'Chrome',
          button: 'Button'
        },
        closeByMiddleClick: 'Close Tab by Middle Click',
        closeByMiddleClickTip: 'Enable closing tabs by clicking with the middle mouse button'
      },
      header: {
        title: 'Header Settings',
        height: 'Header Height',
        breadcrumb: {
          visible: 'Breadcrumb Visible',
          showIcon: 'Breadcrumb Icon Visible'
        }
      },
      sider: {
        title: 'Sider Settings',
        inverted: 'Dark Sider',
        width: 'Sider Width',
        collapsedWidth: 'Sider Collapsed Width',
        mixWidth: 'Mix Sider Width',
        mixCollapsedWidth: 'Mix Sider Collapse Width',
        mixChildMenuWidth: 'Mix Child Menu Width',
        autoSelectFirstMenu: 'Auto Select First Submenu',
        autoSelectFirstMenuTip:
          'When a first-level menu is clicked, the first submenu is automatically selected and navigated to the deepest level'
      },
      footer: {
        title: 'Footer Settings',
        visible: 'Footer Visible',
        fixed: 'Fixed Footer',
        height: 'Footer Height',
        right: 'Right Footer'
      },
      content: {
        title: 'Content Area Settings',
        scrollMode: {
          title: 'Scroll Mode',
          tip: 'The theme scroll only scrolls the main part, the outer scroll can carry the header and footer together',
          wrapper: 'Wrapper',
          content: 'Content'
        },
        page: {
          animate: 'Page Animate',
          mode: {
            title: 'Page Animate Mode',
            fade: 'Fade',
            'fade-slide': 'Slide',
            'fade-bottom': 'Fade Zoom',
            'fade-scale': 'Fade Scale',
            'zoom-fade': 'Zoom Fade',
            'zoom-out': 'Zoom Out',
            none: 'None'
          }
        },
        fixedHeaderAndTab: 'Fixed Header And Tab'
      }
    },
    general: {
      title: 'General Settings',
      watermark: {
        title: 'Watermark Settings',
        visible: 'Watermark Full Screen Visible',
        text: 'Custom Watermark Text',
        enableUserName: 'Enable User Name Watermark',
        enableTime: 'Show Current Time',
        timeFormat: 'Time Format'
      },
      multilingual: {
        title: 'Multilingual Settings',
        visible: 'Display multilingual button'
      },
      globalSearch: {
        title: 'Global Search Settings',
        visible: 'Display GlobalSearch button'
      }
    },
    configOperation: {
      copyConfig: 'Copy Config',
      copySuccessMsg: 'Copy Success, Please replace the variable "themeSettings" in "src/theme/settings.ts"',
      resetConfig: 'Reset Config',
      resetSuccessMsg: 'Reset Success'
    }
  },
  route: {
    login: 'ログイン',
    403: 'アクセス権限がありません',
    404: 'ページが見つかりません',
    500: 'サーバーエラー',
    'iframe-page': 'Iframe',
    home: '状況モニタ',
    fms: '配車管理',
    fms_twin: 'デジタルツイン',
    fms_screen: '大画面',
    fms_tasks: 'タスクセンター',
    fms_workflow: 'フローオーケストレーション',
    fms_simulation: 'シミュレーション',
    fms_replay: 'シーン再生',
    map: 'マップ・保管区',
    map_model: 'マップモデリング',
    map_zones: '保管区管制',
    fleet: '車両管理',
    fleet_vehicles: '車両台帳',
    fleet_traffic: '交通管制・退避',
    system: 'システムセキュリティ',
    system_permission: '権限管理',
    system_permission_roles: 'ロール管理',
    system_permission_users: 'ユーザー管理',
    system_permission_menus: 'メニュー管理',
    system_rcs: 'インターフェース連携',
    system_audit: '監査ログ',
    logs: 'ログセンター',
    logs_operations: '操作ログ',
    logs_inbound: '受信ログ',
    logs_outbound: '送信ログ',
    logs_api: 'APIログ',
    logs_runtime: '実行ログ',
    logs_text: 'テキストログ',
    logs_structured: '構造化ログ',
    logs_ai: 'AI分析',
    logs_openapi: 'APIキー'
  },
  page: {
    login: {
      common: {
        loginOrRegister: 'Login / Register',
        userNamePlaceholder: 'Please enter user name',
        phonePlaceholder: 'Please enter phone number',
        codePlaceholder: 'Please enter verification code',
        passwordPlaceholder: 'Please enter password',
        confirmPasswordPlaceholder: 'Please enter password again',
        codeLogin: 'Verification code login',
        confirm: '確認',
        back: 'Back',
        validateSuccess: 'Verification passed',
        loginSuccess: 'Login successfully',
        welcomeBack: 'Welcome back, {userName} !'
      },
      pwdLogin: {
        title: 'Password Login',
        rememberMe: 'Remember me',
        forgetPassword: 'Forget password?',
        register: 'Register',
        otherAccountLogin: 'Other Account Login',
        otherLoginMode: 'Other Login Mode',
        superAdmin: 'Super Admin',
        admin: 'Admin',
        operator: 'Operator',
        safety: 'Safety',
        viewer: 'Viewer',
        user: 'User'
      },
      console: {
        entry: 'Console access',
        fleetStatus: 'Fleet status',
        simCount: 'Simulation counts',
        running: 'Running',
        idle: 'Idle',
        charging: 'Charging',
        fault: 'Fault',
        statusTotal: 'By status',
        inTransit: 'In-transit tasks',
        inTransitNote: 'Task details appear after login. This page shows the in-transit count only.',
        factoryMap: 'Factory map',
        topoNodes: 'Topology nodes',
        pathSegments: 'Path segments',
        onlineVehicles: 'Online vehicles',
        alarms: 'Alarms',
        alarmNone: 'No vehicles in fault',
        alarmFault: '{count} vehicles in fault. Details appear after login.',
        featureDispatch: 'Dispatch',
        featureDispatchDesc: 'Multi-vehicle sync',
        featureTraffic: 'Traffic control',
        featureTrafficDesc: 'Paths and mutex',
        featureTask: 'Task management',
        featureTaskDesc: 'Orders and steps',
        featureDevice: 'Equipment link',
        featureDeviceDesc: 'Stations and signals',
        featureAnalytics: 'Analytics',
        featureAnalyticsDesc: 'Simulation replay',
        footerTag: 'Simulation · Fleet · Traffic',
        demoAccounts: 'Demo accounts',
        unusedHint: 'This entry is not enabled. Use password login.',
        backToPwd: 'Back to password login',
        unusedCode: 'Code login is not enabled',
        unusedRegister: 'Registration is not enabled',
        unusedForgot: 'Forgot password is not enabled',
        unusedCodeRegister: 'Code login and registration are not enabled',
        forgotUnused: 'Forgot password is not enabled',
        loginFailed: 'Incorrect username or password',
        demoFailed: 'Demo account login failed'
      },
      codeLogin: {
        title: 'Verification Code Login',
        getCode: 'Get verification code',
        reGetCode: 'Reacquire after {time}s',
        sendCodeSuccess: 'Verification code sent successfully',
        imageCodePlaceholder: 'Please enter image verification code'
      },
      register: {
        title: 'Register',
        agreement: 'I have read and agree to',
        protocol: '《User Agreement》',
        policy: '《Privacy Policy》'
      },
      resetPwd: {
        title: 'Reset Password'
      },
      bindWeChat: {
        title: 'Bind WeChat'
      }
    },
    home: {
      branchDesc:
        'For the convenience of everyone in developing and updating the merge, we have streamlined the code of the main branch, only retaining the homepage menu, and the rest of the content has been moved to the example branch for maintenance. The preview address displays the content of the example branch.',
      greeting: 'Good morning, {userName}, today is another day full of vitality!',
      weatherDesc: 'Today is cloudy to clear, 20℃ - 25℃!',
      projectCount: 'Project Count',
      todo: 'Todo',
      message: 'Message',
      downloadCount: 'Download Count',
      registerCount: 'Register Count',
      schedule: 'Work and rest Schedule',
      study: 'Study',
      work: 'Work',
      rest: 'Rest',
      entertainment: 'Entertainment',
      visitCount: 'Visit Count',
      turnover: 'Turnover',
      dealCount: 'Deal Count',
      projectNews: {
        title: 'Project News',
        moreNews: 'More News',
        desc1: 'Soybean created the open source project soybean-admin on May 28, 2021!',
        desc2: 'Yanbowe submitted a bug to soybean-admin, the multi-tab bar will not adapt.',
        desc3: 'Soybean is ready to do sufficient preparation for the release of soybean-admin!',
        desc4: 'Soybean is busy writing project documentation for soybean-admin!',
        desc5: 'Soybean just wrote some of the workbench pages casually, and it was enough to see!'
      },
      creativity: 'Creativity'
    }
  },
  form: {
    required: 'Cannot be empty',
    userName: {
      required: 'Please enter user name',
      invalid: 'User name format is incorrect'
    },
    phone: {
      required: 'Please enter phone number',
      invalid: 'Phone number format is incorrect'
    },
    pwd: {
      required: 'Please enter password',
      invalid: '6-18 characters, including letters, numbers, and underscores'
    },
    confirmPwd: {
      required: 'Please enter password again',
      invalid: 'The two passwords are inconsistent'
    },
    code: {
      required: 'Please enter verification code',
      invalid: 'Verification code format is incorrect'
    },
    email: {
      required: 'Please enter email',
      invalid: 'Email format is incorrect'
    }
  },
  dropdown: {
    closeCurrent: 'Close Current',
    closeOther: 'Close Other',
    closeLeft: 'Close Left',
    closeRight: 'Close Right',
    closeAll: 'Close All',
    pin: 'Pin Tab',
    unpin: 'Unpin Tab'
  },
  icon: {
    themeConfig: 'Theme Configuration',
    themeSchema: 'Theme Schema',
    lang: 'Switch Language',
    fullscreen: 'Fullscreen',
    fullscreenExit: 'Exit Fullscreen',
    reload: 'Reload Page',
    collapse: 'Collapse Menu',
    expand: 'Expand Menu',
    pin: 'Pin',
    unpin: 'Unpin'
  },
  datatable: {
    itemCount: 'Total {total} items',
    fixed: {
      left: 'Left Fixed',
      right: 'Right Fixed',
      unFixed: 'Unfixed'
    }
  }
};

export default local;
