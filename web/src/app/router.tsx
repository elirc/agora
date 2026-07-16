import { createBrowserRouter } from 'react-router-dom';
import { HomePage } from '../pages/HomePage';
import { ProductPage } from '../pages/ProductPage';
import { LoginPage } from '../pages/LoginPage';
import { RegisterPage } from '../pages/RegisterPage';
import { AccountPage } from '../pages/AccountPage';
import { RequireAuth } from '../features/auth/RequireAuth';
import { RootLayout } from './RootLayout';

// The router is composition only — pages compose features, features own their data. Everything
// hangs off one layout route so the Header renders on every page. Admin routes (/admin) arrive
// with the back-office in Sprint 03; the storefront lives under /.
export const router = createBrowserRouter([
  {
    element: <RootLayout />,
    children: [
      { path: '/', element: <HomePage /> },
      { path: '/products/:slug', element: <ProductPage /> },
      { path: '/login', element: <LoginPage /> },
      { path: '/register', element: <RegisterPage /> },
      // /account is behind the guard: anonymous shoppers get bounced to /login with a returnTo.
      {
        path: '/account',
        element: (
          <RequireAuth>
            <AccountPage />
          </RequireAuth>
        ),
      },
    ],
  },
]);
