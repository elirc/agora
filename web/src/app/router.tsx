import { createBrowserRouter } from 'react-router-dom';
import { HomePage } from '../pages/HomePage';
import { ProductPage } from '../pages/ProductPage';
import { LoginPage } from '../pages/LoginPage';
import { RegisterPage } from '../pages/RegisterPage';
import { AccountPage } from '../pages/AccountPage';
import { AdminProductsPage } from '../pages/admin/AdminProductsPage';
import { AdminProductNewPage } from '../pages/admin/AdminProductNewPage';
import { AdminProductEditPage } from '../pages/admin/AdminProductEditPage';
import { RequireAuth } from '../features/auth/RequireAuth';
import { RequireRole } from '../features/auth/RequireRole';
import { RootLayout } from './RootLayout';

// The router is composition only — pages compose features, features own their data. Everything
// hangs off one layout route so the Header renders on every page. The storefront lives under /;
// the Store Admin back-office (S03) lives under /admin, gated by RequireRole('StoreAdmin').
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
      // /admin/* is StoreAdmin-only. The guard is UX; the server re-checks every request (the real
      // 403 lives in AuthorizationMatrixTests). A non-admin sees a friendly denied page, not a login
      // bounce — logging in as themselves wouldn't change the answer.
      {
        path: '/admin/products',
        element: (
          <RequireRole role="StoreAdmin">
            <AdminProductsPage />
          </RequireRole>
        ),
      },
      {
        path: '/admin/products/new',
        element: (
          <RequireRole role="StoreAdmin">
            <AdminProductNewPage />
          </RequireRole>
        ),
      },
      {
        path: '/admin/products/:id',
        element: (
          <RequireRole role="StoreAdmin">
            <AdminProductEditPage />
          </RequireRole>
        ),
      },
    ],
  },
]);
