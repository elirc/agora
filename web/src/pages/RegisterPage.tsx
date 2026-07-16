import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ApiError } from '../api/client';
import { useRegister } from '../features/auth/mutations';
import { MIN_PASSWORD_LENGTH, validateRegistration } from '../features/auth/validation';

export function RegisterPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [clientError, setClientError] = useState<string | null>(null);
  const register = useRegister();
  const navigate = useNavigate();

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();

    // Client mirrors the server rules for fast feedback. If this passes, the server STILL checks —
    // this is a convenience, not a gate.
    const problem = validateRegistration(email, password);
    if (problem) {
      setClientError(problem);
      return;
    }
    setClientError(null);

    register.mutate({ email, password }, { onSuccess: () => navigate('/account', { replace: true }) });
  };

  const serverError =
    register.error instanceof ApiError
      ? register.error.message
      : register.isError
        ? 'Something went wrong. Please try again.'
        : null;
  const errorMessage = clientError ?? serverError;

  return (
    <main className="mx-auto max-w-sm px-4 py-8">
      <h1 className="text-2xl font-semibold text-neutral-900">Create your account</h1>

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
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="mt-1 w-full rounded-md border border-neutral-300 px-3 py-2"
          />
          <span className="mt-1 block text-xs text-neutral-500">
            At least {MIN_PASSWORD_LENGTH} characters.
          </span>
        </label>

        {errorMessage && (
          <p role="alert" className="text-sm text-red-700">
            {errorMessage}
          </p>
        )}

        <button
          type="submit"
          disabled={register.isPending}
          className="w-full rounded-md bg-neutral-900 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
        >
          {register.isPending ? 'Creating…' : 'Create account'}
        </button>
      </form>

      <p className="mt-4 text-sm text-neutral-600">
        Already have an account?{' '}
        <Link to="/login" className="underline">
          Sign in
        </Link>
      </p>
    </main>
  );
}
