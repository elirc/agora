import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'coverage', 'src/api/schema.d.ts'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2022,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],

      // BOUNDARY (CONTRIBUTING rule 5, from S01): components don't fetch. Server state flows
      // through the typed API client (src/api) and TanStack Query hooks — never a raw fetch or
      // an HTTP library in a component. This ESLint rule is the frontend's fitness function,
      // the mirror of the backend's dependency-rule architecture tests. src/api/ is exempted
      // below — it is the one place allowed to touch the network.
      'no-restricted-globals': [
        'error',
        {
          name: 'fetch',
          message:
            'Components must not call fetch directly — use the typed client in src/api (CONTRIBUTING rule 5).',
        },
      ],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: 'axios',
              message: 'No direct HTTP libraries in the app — route calls through src/api.',
            },
          ],
        },
      ],
    },
  },
  {
    // The API client is the ONE place allowed to touch the network.
    files: ['src/api/**/*.ts'],
    rules: {
      'no-restricted-globals': 'off',
      'no-restricted-imports': 'off',
    },
  },
  {
    // Node scripts + Vite config run outside the browser.
    files: ['scripts/**/*.mjs', 'vite.config.ts'],
    languageOptions: {
      globals: globals.node,
    },
  },
);
