import { Outlet } from 'react-router-dom';
import { Header } from './Header';

// Composition only: the header on every page, the routed page below it. Layout routes are how
// React Router shares chrome without every page importing the Header itself.
export function RootLayout() {
  return (
    <div className="min-h-screen bg-white">
      <Header />
      <Outlet />
    </div>
  );
}
