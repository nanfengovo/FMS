const local: App.I18n.Schema = {
  system: {
    title: 'AMR 지능형 디스패치 시뮬레이션 플랫폼',
    updateTitle: '시스템 버전 업데이트 알림',
    updateContent: '새 버전이 감지되었습니다. 지금 새로고침하시겠습니까?',
    updateConfirm: '즉시 새로고침',
    updateCancel: '나중에'
  },
  common: {
    action: '작업',
    add: '추가',
    addSuccess: '추가 성공',
    backToHome: '홈으로 돌아가기',
    batchDelete: '일괄 삭제',
    cancel: '취소',
    close: '닫기',
    check: '선택',
    selectAll: '전체 선택',
    expandColumn: '열 펼치기',
    columnSetting: '열 설정',
    config: '설정',
    confirm: '확인',
    delete: '삭제',
    deleteSuccess: '삭제 성공',
    confirmDelete: '정말 삭제하시겠습니까?',
    edit: '수정',
    warning: '경고',
    error: '오류',
    index: '번호',
    keywordSearch: '키워드를 입력하세요',
    logout: '로그아웃',
    logoutConfirm: '로그아웃하시겠습니까?',
    lookForward: '준비 중',
    modify: '수정',
    modifySuccess: '수정 성공',
    noData: '데이터 없음',
    operate: '작업',
    pleaseCheckValue: '입력값이 올바른지 확인하세요',
    refresh: '새로고침',
    reset: '초기화',
    search: '검색',
    switch: '전환',
    tip: '안내',
    trigger: '트리거',
    update: '업데이트',
    updateSuccess: '업데이트 성공',
    userCenter: '사용자 센터',
    yesOrNo: {
      yes: '예',
      no: '아니오'
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
        warning: '경고',
        error: '오류',
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
    login: '로그인',
    403: '권한 없음',
    404: '페이지를 찾을 수 없음',
    500: '서버 오류',
    'iframe-page': 'Iframe',
    home: '현황 대시보드',
    fms: '디스패치 관리',
    fms_twin: '디지털 트윈',
    fms_screen: '관제 화면',
    fms_tasks: '작업 센터',
    fms_workflow: '워크플로 오케스트레이션',
    fms_simulation: '시뮬레이션',
    fms_replay: '장면 재생',
    map: '맵 및 스토리지',
    map_model: '맵 모델링',
    map_zones: '구역 통제',
    fleet: '차량 관리',
    fleet_vehicles: '차량 원장',
    fleet_traffic: '교통 회피 통제',
    system: '시스템 보안',
    system_permission: '권한 관리',
    system_permission_roles: '역할 관리',
    system_permission_users: '사용자 관리',
    system_permission_menus: '메뉴 관리',
    system_rcs: '인터페이스 통합',
    system_audit: '보안 감사',
    logs: '로그 센터',
    logs_operations: '사용자 작업',
    logs_inbound: '인바운드',
    logs_outbound: '아웃바운드',
    logs_api: 'API 로그',
    logs_runtime: '런타임',
    logs_text: '텍스트 로그',
    logs_structured: '구조화 로그',
    logs_ai: 'AI 분석',
    logs_openapi: 'API 키'
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
        confirm: '확인',
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
