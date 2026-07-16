import { useCurrentUser } from '../features/auth/queries';

// A stub for now: the account page proves the guarded route + current-user query work end to end.
// Orders, addresses, and the cart move in here as they arrive (S05, S07) — each one owned by THIS
// user, which is the whole reason S02 exists.
export function AccountPage() {
  const { data: user } = useCurrentUser();

  return (
    <main className="mx-auto max-w-3xl px-4 py-8">
      <h1 className="text-2xl font-semibold text-neutral-900">Your account</h1>
      <dl className="mt-6 space-y-2 text-sm">
        <div className="flex gap-2">
          <dt className="w-24 text-neutral-500">Email</dt>
          <dd className="text-neutral-900">{user?.email}</dd>
        </div>
        <div className="flex gap-2">
          <dt className="w-24 text-neutral-500">Role</dt>
          <dd className="text-neutral-900">{user?.role}</dd>
        </div>
      </dl>
      <p className="mt-6 text-sm text-neutral-500">
        Your orders and saved addresses will live here as the shop grows.
      </p>
    </main>
  );
}
