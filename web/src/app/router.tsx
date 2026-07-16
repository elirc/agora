import { createBrowserRouter } from 'react-router-dom';
import { HomePage } from '../pages/HomePage';
import { ProductPage } from '../pages/ProductPage';

// The router is composition only — pages compose features, features own their data. Admin routes
// (/admin) arrive with the back-office in Sprint 03; the storefront lives under /.
export const router = createBrowserRouter([
  { path: '/', element: <HomePage /> },
  { path: '/products/:slug', element: <ProductPage /> },
]);
