import '@testing-library/jest-dom';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

// Tear down the rendered tree between tests so one test's DOM can't leak into the next.
afterEach(() => {
  cleanup();
});
