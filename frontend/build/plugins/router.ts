import type { RouteMeta } from 'vue-router';
import ElegantVueRouter from '@elegant-router/vue/vite';
import type { RouteKey } from '@elegant-router/types';

export function setupElegantRouter() {
  return ElegantVueRouter({
    layouts: {
      base: 'src/layouts/base-layout/index.vue',
      blank: 'src/layouts/blank-layout/index.vue'
    },
    routePathTransformer(routeName, routePath) {
      const key = routeName as RouteKey;

      if (key === 'login') {
        const modules: UnionKey.LoginModule[] = ['pwd-login', 'code-login', 'register', 'reset-pwd', 'bind-wechat'];

        const moduleReg = modules.join('|');

        return `/login/:module(${moduleReg})?`;
      }

      return routePath;
    },
    defaultLayout: 'base',
    onRouteMetaGen(routeName) {
      const key = routeName as RouteKey;

      const constantRoutes: RouteKey[] = ['login', '403', '404', '500'];

      const meta: Partial<RouteMeta> = {
        title: key,
        i18nKey: `route.${key}` as App.I18n.I18nKey
      };

      if (key === 'home') {
        meta.icon = 'carbon:dashboard';
        meta.order = 1;
      } else if (key === 'fms') {
        meta.icon = 'carbon:delivery-parcel';
        meta.order = 2;
      } else if (key === 'fms_twin') {
        meta.icon = 'carbon:screen';
        meta.order = 1;
      } else if (routeName === 'fms_screen') {
        meta.icon = 'carbon:dashboard';
        meta.order = 0;
      } else if (key === 'fms_tasks') {
        meta.icon = 'carbon:task';
        meta.order = 2;
      } else if (key === 'fms_workflow') {
        meta.icon = 'carbon:flow-stream';
        meta.order = 3;
      } else if (key === 'fms_simulation') {
        meta.icon = 'carbon:game-console';
        meta.order = 4;
      } else if (key === 'fms_replay') {
        meta.icon = 'carbon:video';
        meta.order = 5;
      } else if (key === 'map') {
        meta.icon = 'carbon:map';
        meta.order = 3;
      } else if (key === 'map_model') {
        meta.icon = 'carbon:road';
        meta.order = 1;
      } else if (key === 'map_zones') {
        meta.icon = 'carbon:grid';
        meta.order = 2;
      } else if (key === 'fleet') {
        meta.icon = 'carbon:car';
        meta.order = 4;
      } else if (key === 'fleet_vehicles') {
        meta.icon = 'carbon:delivery-truck';
        meta.order = 1;
      } else if (key === 'fleet_traffic') {
        meta.icon = 'carbon:network-4';
        meta.order = 2;
      } else if (key === 'system') {
        meta.icon = 'carbon:security';
        meta.order = 5;
      } else if (routeName === 'system_permission') {
        meta.icon = 'carbon:user-identification';
        meta.order = 1;
      } else if (routeName === 'system_permission_roles') {
        meta.icon = 'carbon:user-role';
        meta.order = 1;
      } else if (routeName === 'system_permission_users') {
        meta.icon = 'carbon:user-multiple';
        meta.order = 2;
      } else if (routeName === 'system_permission_menus') {
        meta.icon = 'carbon:tree-view';
        meta.order = 3;
      } else if (key === 'system_rcs') {
        meta.icon = 'carbon:connect';
        meta.order = 2;
      } else if (key === 'system_audit') {
        meta.icon = 'carbon:event-schedule';
        meta.order = 3;
      } else if (routeName === 'logs') {
        meta.icon = 'carbon:catalog';
        meta.order = 6;
      } else if (routeName === 'logs_operations') {
        meta.icon = 'carbon:user-activity';
        meta.order = 1;
      } else if (routeName === 'logs_inbound') {
        meta.icon = 'carbon:arrow-down';
        meta.order = 2;
      } else if (routeName === 'logs_outbound') {
        meta.icon = 'carbon:arrow-up';
        meta.order = 3;
      } else if (routeName === 'logs_api') {
        meta.icon = 'carbon:api';
        meta.order = 4;
      } else if (routeName === 'logs_runtime') {
        meta.icon = 'carbon:terminal';
        meta.order = 5;
      } else if (routeName === 'logs_text') {
        meta.icon = 'carbon:document';
        meta.order = 6;
      } else if (routeName === 'logs_structured') {
        meta.icon = 'carbon:data-table';
        meta.order = 7;
      } else if (routeName === 'logs_ai') {
        meta.icon = 'carbon:machine-learning-model';
        meta.order = 8;
      } else if (routeName === 'logs_openapi') {
        meta.icon = 'carbon:password';
        meta.order = 9;
      }

      const routePermissions: Record<string, string[]> = {
        home: ['Fms.Dashboard'],
        fms_twin: ['Fms.Dashboard'],
        fms_screen: ['Fms.Dashboard'],
        fms_tasks: ['Fms.Orders'],
        fms_workflow: ['Fms.Workflow'],
        fms_simulation: ['Fms.Simulation'],
        fms_replay: ['Fms.Simulation'],
        map_model: ['Fms.Map'],
        map_zones: ['Fms.Storage'],
        fleet_vehicles: ['Fms.Fleet'],
        fleet_traffic: ['Fms.Traffic'],
        system_permission: ['Fms.System'],
        system_permission_roles: ['Fms.System.Roles'],
        system_permission_users: ['Fms.System.Users'],
        system_permission_menus: ['Fms.System.Roles'],
        system_audit: ['Fms.System.Audit'],
        system_rcs: ['Fms.System'],
        logs_operations: ['Fms.Logs'],
        logs_inbound: ['Fms.Logs'],
        logs_outbound: ['Fms.Logs'],
        logs_api: ['Fms.Logs'],
        logs_runtime: ['Fms.Logs'],
        logs_text: ['Fms.Logs'],
        logs_structured: ['Fms.Logs'],
        logs_ai: ['Fms.Logs.Analyze'],
        logs_openapi: ['Fms.Logs.Settings']
      };

      if (routePermissions[routeName]) {
        meta.permissions = routePermissions[routeName];
      }

      if (constantRoutes.includes(key)) {
        meta.constant = true;
      }

      return meta;
    }
  });
}
