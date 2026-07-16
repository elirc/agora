import { useState } from 'react';
import { Outlet } from 'react-router-dom';
import { CartDrawer } from '../features/cart/components/CartDrawer';
import { Header } from './Header';

// Composition only: the header on every page, the routed page below it. Layout routes are how
// React Router shares chrome without every page importing the Header itself.
//
// 📘 THE ONE PIECE OF STATE HERE IS "IS THE DRAWER OPEN", AND THAT IS THE POINT. It is the only kind
// of state this app keeps in `useState`: pure UI, local, meaningless to the server, gone on refresh —
// nobody's cart depends on whether a panel is showing. The cart's CONTENTS live in TanStack Query,
// where server state belongs, which is why the drawer and the header badge agree without either of
// them being handed anything. (CONTRIBUTING rule 5: UI state and server state are different things,
// managed differently. This file is the smallest possible demonstration of the difference.)
//
// WHY no context, no store, no library: two components need one boolean and they share a parent. A
// CartContext would be ceremony — and the moment it existed, someone would put the cart DATA in it,
// and then there would be two answers to "what is in the cart".
export function RootLayout() {
  const [cartOpen, setCartOpen] = useState(false);

  return (
    <div className="min-h-screen bg-white">
      <Header onOpenCart={() => setCartOpen(true)} />
      <Outlet />
      <CartDrawer open={cartOpen} onClose={() => setCartOpen(false)} />
    </div>
  );
}
