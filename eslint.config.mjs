// ESLint 9 (flat config) для frontend FAIZA GOLD.
// Лежит в корне репозитория, т. к. каталог frontend/ — зона агента B.
// Зависимости не добавлены в frontend/package.json (тоже зона B); для запуска:
//   npm install --no-save --no-package-lock eslint@9 @eslint/js@9 globals@16 eslint-plugin-react@7 eslint-plugin-react-hooks@5
//   npx eslint frontend/src frontend/tests  (конфиг eslint.config.mjs подхватывается автоматически)
// В CI запускается в режиме предупреждения (continue-on-error).
import js from '@eslint/js';
import globals from 'globals';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';

export default [
  {
    ignores: ['**/node_modules/**', '**/dist/**', '**/bin/**', '**/obj/**', '.runtime/**', 'backend/**']
  },
  js.configs.recommended,
  {
    files: ['frontend/src/**/*.{js,jsx}'],
    languageOptions: {
      ecmaVersion: 2023,
      sourceType: 'module',
      parserOptions: { ecmaFeatures: { jsx: true } },
      globals: { ...globals.browser }
    },
    settings: { react: { version: '19.2' } },
    plugins: { react, 'react-hooks': reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      // Переменные, используемые только в JSX, не считаются неиспользуемыми
      'react/jsx-uses-vars': 'error',
      'react/jsx-uses-react': 'off',
      'react/react-in-jsx-scope': 'off',
      'react/jsx-key': 'warn',
      'react/no-danger': 'error',
      'react/jsx-no-target-blank': 'error',
      'no-unused-vars': ['warn', { argsIgnorePattern: '^_', varsIgnorePattern: '^(_|React$)' }],
      // Деньги: запрет float-разбора сумм (инвариант 4 CLAUDE.md) — использовать utils/format.js
      'no-restricted-globals': ['error', { name: 'parseFloat', message: 'Не используйте float для денег: см. utils/format.js (BigInt-копейки).' }],
      'no-restricted-properties': ['error', { object: 'Number', property: 'parseFloat', message: 'Не используйте float для денег: см. utils/format.js.' }],
      eqeqeq: ['warn', 'always', { null: 'ignore' }],
      'no-console': ['warn', { allow: ['warn', 'error'] }]
    }
  },
  {
    files: ['frontend/tests/**/*.js', 'frontend/vite.config.js', 'tests/**/*.mjs', 'scripts/**/*.mjs', 'eslint.config.mjs'],
    languageOptions: {
      ecmaVersion: 2023,
      sourceType: 'module',
      globals: { ...globals.node }
    },
    rules: {
      'no-unused-vars': ['warn', { argsIgnorePattern: '^_' }]
    }
  }
];
