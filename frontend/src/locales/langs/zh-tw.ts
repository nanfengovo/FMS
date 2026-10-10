const local: App.I18n.Schema = {
  system: {
    title: 'AMR 智慧調度模擬平台',
    updateTitle: '系统版本更新通知',
    updateContent: '检测到系统有新版本发布，是否立即重新整理页面？',
    updateConfirm: '立即重新整理',
    updateCancel: '稍后再说'
  },
  common: {
    action: '操作',
    add: '新增',
    addSuccess: '新增成功',
    backToHome: '返回首頁',
    batchDelete: '批量刪除',
    cancel: '取消',
    close: '關閉',
    check: '勾选',
    selectAll: '全選',
    expandColumn: '展开列',
    columnSetting: '列设置',
    config: '配置',
    confirm: '確認',
    delete: '刪除',
    deleteSuccess: '刪除成功',
    confirmDelete: '確認刪除吗？',
    edit: '編輯',
    warning: '警告',
    error: '錯誤',
    index: '序號',
    keywordSearch: '请输入关键词搜尋',
    logout: '退出登入',
    logoutConfirm: '確認退出登入吗？',
    lookForward: '敬请期待',
    modify: '修改',
    modifySuccess: '修改成功',
    noData: '無資料',
    operate: '操作',
    pleaseCheckValue: '请检查输入的值是否合法',
    refresh: '重新整理',
    reset: '重設',
    search: '搜尋',
    switch: '切換',
    tip: '提示',
    trigger: '触发',
    update: '更新',
    updateSuccess: '更新成功',
    userCenter: '個人中心',
    yesOrNo: {
      yes: '是',
      no: '否'
    }
  },
  request: {
    logout: '请求失败后登出用户',
    logoutMsg: '用户状态失效，请重新登入',
    logoutWithModal: '请求失败后弹出模态框再登出用户',
    logoutWithModalMsg: '用户状态失效，请重新登入',
    refreshToken: '请求的token已过期，重新整理token',
    tokenExpired: 'token已过期'
  },
  theme: {
    themeDrawerTitle: '主题配置',
    tabs: {
      appearance: '外观',
      layout: '布局',
      general: '通用',
      preset: '预设'
    },
    appearance: {
      themeSchema: {
        title: '主題模式',
        light: '亮色模式',
        dark: '暗黑模式',
        auto: '跟隨系統'
      },
      grayscale: '灰色模式',
      colourWeakness: '色弱模式',
      themeColor: {
        title: '主題顏色',
        primary: '主色',
        info: '信息色',
        success: '成功色',
        warning: '警告色',
        error: '錯誤色',
        followPrimary: '跟随主色'
      },
      themeRadius: {
        title: '主题圆角'
      },
      recommendColor: '应用推荐算法的颜色',
      recommendColorDesc: '推荐颜色的算法参照',
      preset: {
        title: '主题预设',
        apply: '应用',
        applySuccess: '预设应用成功',
        default: {
          name: '默认预设',
          desc: 'Soybean 默认主题预设'
        },
        dark: {
          name: '暗色预设',
          desc: '适用于夜间使用的暗色主题预设'
        },
        compact: {
          name: '紧凑型',
          desc: '适用于小屏幕的紧凑布局预设'
        },
        azir: {
          name: 'Azir的预设',
          desc: '是 Azir 比较喜欢的莫兰迪色系冷淡风'
        }
      }
    },
    layout: {
      layoutMode: {
        title: '版面模式',
        vertical: '左側選單模式',
        'vertical-mix': '左侧菜单混合模式',
        'vertical-hybrid-header-first': '左侧混合-顶部优先',
        horizontal: '頂部選單模式',
        'top-hybrid-sidebar-first': '顶部混合-侧边优先',
        'top-hybrid-header-first': '顶部混合-顶部优先',
        vertical_detail: '左侧菜单布局，菜单在左，内容在右。',
        'vertical-mix_detail': '左侧双菜单布局，一级菜单在左侧深色区域，二级菜单在左侧浅色区域。',
        'vertical-hybrid-header-first_detail':
          '左侧混合布局，一级菜单在顶部，二级菜单在左侧深色区域，三级菜单在左侧浅色区域。',
        horizontal_detail: '顶部菜单布局，菜单在顶部，内容在下方。',
        'top-hybrid-sidebar-first_detail': '顶部混合布局，一级菜单在左侧，二级菜单在顶部。',
        'top-hybrid-header-first_detail': '顶部混合布局，一级菜单在顶部，二级菜单在左侧。'
      },
      tab: {
        title: '标签栏设置',
        visible: '显示标签栏',
        cache: '标签栏信息缓存',
        cacheTip: '离开页面后仍然保留标签栏信息',
        height: '标签栏高度',
        mode: {
          title: '标签栏风格',
          slider: '滑块风格',
          chrome: '谷歌风格',
          button: '按钮风格'
        },
        closeByMiddleClick: '鼠标中键關閉标签页',
        closeByMiddleClickTip: '启用后可以使用鼠标中键点击标签页进行關閉'
      },
      header: {
        title: '头部设置',
        height: '头部高度',
        breadcrumb: {
          visible: '显示面包屑',
          showIcon: '显示面包屑图标'
        }
      },
      sider: {
        title: '侧边栏设置',
        inverted: '深色侧边栏',
        width: '侧边栏宽度',
        collapsedWidth: '侧边栏折叠宽度',
        mixWidth: '混合布局侧边栏宽度',
        mixCollapsedWidth: '混合布局侧边栏折叠宽度',
        mixChildMenuWidth: '混合布局子菜单宽度',
        autoSelectFirstMenu: '自动选择第一个子菜单',
        autoSelectFirstMenuTip: '点击一级菜单时，自动选择并导航到第一个子菜单的最深层级'
      },
      footer: {
        title: '底部设置',
        visible: '显示底部',
        fixed: '固定底部',
        height: '底部高度',
        right: '底部居右'
      },
      content: {
        title: '内容区域设置',
        scrollMode: {
          title: '滚动模式',
          tip: '主题滚动仅 main 部分滚动，外层滚动可携带头部底部一起滚动',
          wrapper: '外层滚动',
          content: '主体滚动'
        },
        page: {
          animate: '页面切換动画',
          mode: {
            title: '页面切換动画类型',
            'fade-slide': '滑动',
            fade: '淡入淡出',
            'fade-bottom': '底部消退',
            'fade-scale': '缩放消退',
            'zoom-fade': '渐变',
            'zoom-out': '闪现',
            none: '无'
          }
        },
        fixedHeaderAndTab: '固定头部和标签栏'
      }
    },
    general: {
      title: '通用设置',
      watermark: {
        title: '水印设置',
        visible: '显示全屏水印',
        text: '自定义水印文本',
        enableUserName: '启用用户名水印',
        enableTime: '显示当前时间',
        timeFormat: '时间格式'
      },
      multilingual: {
        title: '多语言设置',
        visible: '显示多语言按钮'
      },
      globalSearch: {
        title: '全局搜尋设置',
        visible: '显示全局搜尋按钮'
      }
    },
    configOperation: {
      copyConfig: '复制配置',
      copySuccessMsg: '复制成功，请替换 src/theme/settings.ts 中的变量 themeSettings',
      resetConfig: '重設配置',
      resetSuccessMsg: '重設成功'
    }
  },
  route: {
    login: '登入',
    403: '無權限',
    404: '頁面不存在',
    500: '服务器錯誤',
    'iframe-page': '外链页面',
    home: '態勢大盤',
    fms: '調度管理',
    fms_twin: '數位孿生',
    fms_screen: '運行大屏',
    fms_tasks: '任務中心',
    fms_workflow: '流程編排',
    fms_simulation: '場景模擬',
    fms_replay: '監控回放',
    map: '地圖庫區',
    map_model: '地圖建模',
    map_zones: '庫區管控',
    fleet: '車隊管理',
    fleet_vehicles: '車輛台帳',
    fleet_traffic: '交通避讓',
    system: '系統安全',
    system_permission: '權限管理',
    system_permission_roles: '角色管理',
    system_permission_users: '使用者管理',
    system_permission_menus: '選單管理',
    system_rcs: '介面整合',
    system_audit: '安全審計',
    logs: '日誌中心',
    logs_operations: '使用者操作日誌',
    logs_inbound: '入站日誌',
    logs_outbound: '出站日誌',
    logs_api: '介面日誌',
    logs_runtime: '系統運行日誌',
    logs_text: '文字日誌',
    logs_structured: '結構化日誌',
    logs_ai: 'AI分析',
    logs_openapi: '介面金鑰'
  },
  page: {
    login: {
      common: {
        loginOrRegister: '登入 / 注册',
        userNamePlaceholder: '请输入用户名',
        phonePlaceholder: '请输入手机号',
        codePlaceholder: '请输入验证码',
        passwordPlaceholder: '请输入密码',
        confirmPasswordPlaceholder: '请再次输入密码',
        codeLogin: '验证码登入',
        confirm: '确定',
        back: '返回',
        validateSuccess: '验证成功',
        loginSuccess: '登入成功',
        welcomeBack: '欢迎回来，{userName} ！'
      },
      pwdLogin: {
        title: '密码登入',
        rememberMe: '记住我',
        forgetPassword: '忘记密码？',
        register: '注册账号',
        otherAccountLogin: '其他账号登入',
        otherLoginMode: '其他登入方式',
        superAdmin: '超级管理员',
        admin: '管理員',
        operator: '操作員',
        safety: '安全員',
        viewer: '訪客',
        user: '普通用户'
      },
      console: {
        entry: '控制台入口',
        fleetStatus: '車隊狀態',
        simCount: '仿真計數',
        running: '運行',
        idle: '待命',
        charging: '充電',
        fault: '異常',
        statusTotal: '分態合計',
        inTransit: '在途任務',
        inTransitNote: '登入後查看任務明細。本頁僅展示在途計數。',
        factoryMap: '工廠地圖',
        topoNodes: '拓撲節點',
        pathSegments: '路徑段',
        onlineVehicles: '在線車輛',
        alarms: '告警資訊',
        alarmNone: '當前無異常車輛',
        alarmFault: '異常車輛 {count} 臺，登入後查看明細',
        featureDispatch: '智能調度',
        featureDispatchDesc: '多車協同',
        featureTraffic: '交通管控',
        featureTrafficDesc: '路徑與互斥',
        featureTask: '任務管理',
        featureTaskDesc: '工單與工步',
        featureDevice: '設備聯動',
        featureDeviceDesc: '站點與信號',
        featureAnalytics: '數據分析',
        featureAnalyticsDesc: '仿真回放',
        footerTag: '仿真調度 · 車隊協同 · 交通管控',
        demoAccounts: '演示帳號',
        unusedHint: '該入口未啟用，請使用帳號密碼登入。',
        backToPwd: '返回密碼登入',
        unusedCode: '驗證碼登入未啟用',
        unusedRegister: '註冊帳號未啟用',
        unusedForgot: '忘記密碼未啟用',
        unusedCodeRegister: '驗證碼登入、註冊帳號未啟用',
        forgotUnused: '忘記密碼未啟用',
        loginFailed: '使用者名稱或密碼錯誤',
        demoFailed: '演示帳號登入失敗'
      },
      codeLogin: {
        title: '验证码登入',
        getCode: '获取验证码',
        reGetCode: '{time}秒后重新获取',
        sendCodeSuccess: '验证码发送成功',
        imageCodePlaceholder: '请输入图片验证码'
      },
      register: {
        title: '注册账号',
        agreement: '我已经仔细阅读并接受',
        protocol: '《用户协议》',
        policy: '《隐私权政策》'
      },
      resetPwd: {
        title: '重設密码'
      },
      bindWeChat: {
        title: '绑定微信'
      }
    },
    home: {
      branchDesc:
        '为了方便大家开发和更新合并，我们对main分支的代码进行了精简，只保留了首页菜单，其余内容已移至example分支进行维护。预览地址显示的内容即为example分支的内容。',
      greeting: '早安，{userName}, 今天又是充满活力的一天!',
      weatherDesc: '今日多云转晴，20℃ - 25℃!',
      projectCount: '项目数',
      todo: '待办',
      message: '消息',
      downloadCount: '下载量',
      registerCount: '注册量',
      schedule: '作息安排',
      study: '学习',
      work: '工作',
      rest: '休息',
      entertainment: '娱乐',
      visitCount: '访问量',
      turnover: '成交额',
      dealCount: '成交量',
      projectNews: {
        title: '项目动态',
        moreNews: '更多动态',
        desc1: 'Soybean 在2021年5月28日创建了开源项目 soybean-admin!',
        desc2: 'Yanbowe 向 soybean-admin 提交了一个bug，多标签栏不会自适应。',
        desc3: 'Soybean 准备为 soybean-admin 的发布做充分的准备工作!',
        desc4: 'Soybean 正在忙于为soybean-admin写项目说明文档！',
        desc5: 'Soybean 刚才把工作台页面随便写了一些，凑合能看了！'
      },
      creativity: '创意'
    }
  },
  form: {
    required: '不能为空',
    userName: {
      required: '请输入用户名',
      invalid: '用户名格式不正确'
    },
    phone: {
      required: '请输入手机号',
      invalid: '手机号格式不正确'
    },
    pwd: {
      required: '请输入密码',
      invalid: '密码格式不正确，6-18位字符，包含字母、数字、下划线'
    },
    confirmPwd: {
      required: '请输入確認密码',
      invalid: '两次输入密码不一致'
    },
    code: {
      required: '请输入验证码',
      invalid: '验证码格式不正确'
    },
    email: {
      required: '请输入邮箱',
      invalid: '邮箱格式不正确'
    }
  },
  dropdown: {
    closeCurrent: '關閉',
    closeOther: '關閉其它',
    closeLeft: '關閉左侧',
    closeRight: '關閉右侧',
    closeAll: '關閉所有',
    pin: '固定标签',
    unpin: '取消固定'
  },
  icon: {
    themeConfig: '主题配置',
    themeSchema: '主題模式',
    lang: '切換语言',
    fullscreen: '全屏',
    fullscreenExit: '退出全屏',
    reload: '重新整理页面',
    collapse: '折叠菜单',
    expand: '展开菜单',
    pin: '固定',
    unpin: '取消固定'
  },
  datatable: {
    itemCount: '共 {total} 条',
    fixed: {
      left: '左固定',
      right: '右固定',
      unFixed: '取消固定'
    }
  }
};

export default local;
