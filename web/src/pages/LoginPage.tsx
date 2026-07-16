import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { useLogin } from '../features/auth/mutations';

export function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const login = useLogin();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const returnTo = params.get('returnTo') ?? '/account';

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    login.mutate(
      { email, password },
      // On success, honor returnTo (RequireAuth set it) and replace so Back doesn't return here.
      { onSuccess: () => navigate(returnTo, { replace: true }) },
    );
  };

  // WHY show the SERVER's message verbatim on failure: login errors are deliberately uniform
  // ("Invalid email or password") so the page cannot become a user-enumeration oracle. The client
  // does not second-guess which field was wrong — the server already decided, on purpose.
  const errorMessage =
    login.error instanceof ApiError
      ? login.error.message
      : login.isError
        ? 'Something went wrong. Please try again.'
        : null;

  return (
    <main className="mx-auto max-w-sm px-4 py-8">
      <h1 className="text-2xl font-semibold text-neutral-900">Sign in</h1>

      <form className="mt-6 space-y-4" onSubmit={handleSubmit} noValidate>
        <label className="block">
          <span className="text-sm font-medium text-neutral-700">Email</span>
          <input
            type="email"
            name="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="mt-1 w-full rounded-md border border-neutral-300 px-3 py-2"
          />
        </label>

        <label className="block">
          <span className="text-sm font-medium text-neutral-700">Password</span>
          <input
            type="password"
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="mt-1 w-full rounded-md border border-neutral-300 px-3 py-2"
          />
        </label>

        {errorMessage && (
          <p role="alert" className="text-sm text-red-700">
            {errorMessage}
          </p>
        )}

        <button
          type="submit"
          disabled={login.isPending}
          className="w-full rounded-md bg-neutral-900 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
        >
          {login.isPending ? 'Signing in…' : 'Sign in'}
        </button>
      </form>

      <p className="mt-4 text-sm text-neutral-600">
        New here?{' '}
        <Link to="/register" className="underline">
          Create an account
        </Link>
      </p>
    </main>
  );
}
