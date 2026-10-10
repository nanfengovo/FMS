import { defineConfig, transformerDirectives, transformerVariantGroup, presetWind3, presetIcons } from 'unocss';
import { presetSoybeanAdmin } from '@sa/uno-preset';
import carbonIcons from '@iconify/json/json/carbon.json';
import { themeVars } from './src/theme/vars';

export default defineConfig({
  content: {
    pipeline: {
      exclude: ['node_modules', 'dist']
    }
  },
  theme: {
    ...themeVars,
    fontSize: {
      'icon-xs': '0.875rem',
      'icon-small': '1rem',
      icon: '1.125rem',
      'icon-large': '1.5rem',
      'icon-xl': '2rem'
    }
  },
  shortcuts: {
    'card-wrapper': 'rd-8px shadow-sm'
  },
  transformers: [transformerDirectives(), transformerVariantGroup()],
  presets: [
    presetWind3({ dark: 'class' }),
    presetSoybeanAdmin(),
    presetIcons({
      scale: 1.2,
      warn: true,
      collections: {
        carbon: () => carbonIcons
      },
      extraProperties: {
        display: 'inline-block',
        'vertical-align': 'middle'
      }
    })
  ]
});
