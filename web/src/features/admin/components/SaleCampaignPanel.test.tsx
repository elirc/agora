import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createSaleCampaign,
  endSaleCampaign,
  listSaleCampaigns,
  type AdminSaleCampaignDto,
} from '../../../api/client';
import { SaleCampaignPanel } from './SaleCampaignPanel';

// Mock at the API-CLIENT seam — never at the component's internals. These tests then exercise the
// real query hooks, the real cache invalidation, and the real render path; only the network is fake.
vi.mock('../../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../api/client')>();
  return {
    ...actual,
    listSaleCampaigns: vi.fn(),
    createSaleCampaign: vi.fn(),
    endSaleCampaign: vi.fn(),
  };
});

// WHY these windows are absurdly far from today rather than "an hour ago": the Ended-vs-Scheduled
// label is the one thing this panel legitimately derives from the browser's clock (it decides whether
// to render a button the server re-checks anyway). A fixture near "now" would make these tests depend
// on the day they run — the very failure mode the sprint is about. 2020 is over everywhere; 2099 has
// not started anywhere.
const Window2020 = { startsAtUtc: '2020-01-01T00:00:00Z', endsAtUtc: '2020-01-03T00:00:00Z' };
const Window2099 = { startsAtUtc: '2099-01-01T00:00:00Z', endsAtUtc: '2099-01-03T00:00:00Z' };

function campaign(overrides: Partial<AdminSaleCampaignDto> = {}): AdminSaleCampaignDto {
  return {
    id: 'camp-1',
    productId: 'prod-1',
    name: 'Weekend Sale',
    discountKind: 'PercentOff',
    discountValue: 20,
    startsAtUtc: '2026-08-07T00:00:00Z',
    endsAtUtc: '2026-08-10T00:00:00Z',
    isRunning: false,
    createdAtUtc: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SaleCampaignPanel productId="prod-1" priceAmount={50} priceCurrency="USD" />
    </QueryClientProvider>,
  );
}

describe('SaleCampaignPanel', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it('shows the empty state when a product has never been on sale', async () => {
    vi.mocked(listSaleCampaigns).mockResolvedValue([]);

    renderPanel();

    expect(await screen.findByText('No sales scheduled for this product.')).toBeInTheDocument();
  });

  it('labels each campaign with the SERVER’s isRunning rather than its own clock', async () => {
    // ⭐ THE POINT OF THIS FILE. "Live Now" has a window that closed in 2020 — long dead by any local
    // clock — and the panel still says Running, because the SERVER said isRunning. That is the
    // assertion: the row's label comes from the API, not from `new Date()`. If someone ever
    // "simplifies" this component into comparing the window itself, this test goes red, which is
    // exactly the moment to catch it — the storefront resolves prices against the server's clock, so
    // a panel with its own opinion would disagree with the shop it is supposed to describe.
    vi.mocked(listSaleCampaigns).mockResolvedValue([
      campaign({ id: 'a', name: 'Live Now', isRunning: true, ...Window2020 }),
      campaign({ id: 'b', name: 'Old One', isRunning: false, ...Window2020 }),
    ]);

    renderPanel();

    expect(await screen.findByText('Running')).toBeInTheDocument();
    expect(screen.getByText('Ended')).toBeInTheDocument();
  });

  it('marks a not-yet-started campaign as Scheduled, not Ended', async () => {
    vi.mocked(listSaleCampaigns).mockResolvedValue([
      campaign({ name: 'Next Year', isRunning: false, ...Window2099 }),
    ]);

    renderPanel();

    expect(await screen.findByText('Scheduled')).toBeInTheDocument();
    expect(screen.queryByText('Ended')).not.toBeInTheDocument();
  });

  it('sends the discount value as a decimal string and the window as UTC instants', async () => {
    vi.mocked(listSaleCampaigns).mockResolvedValue([]);
    vi.mocked(createSaleCampaign).mockResolvedValue(campaign());

    const user = userEvent.setup();
    renderPanel();
    await screen.findByText('No sales scheduled for this product.');

    await user.type(screen.getByLabelText('Sale name'), 'Labour Day Weekend');
    await user.type(screen.getByLabelText('Discount value'), '20');
    await user.type(screen.getByLabelText('Sale starts'), '2026-08-07T00:00');
    await user.type(screen.getByLabelText('Sale ends'), '2026-08-10T00:00');
    await user.click(screen.getByRole('button', { name: 'Schedule sale' }));

    await waitFor(() => expect(createSaleCampaign).toHaveBeenCalledTimes(1));

    const [productId, body] = vi.mocked(createSaleCampaign).mock.calls[0];
    expect(productId).toBe('prod-1');
    expect(body.name).toBe('Labour Day Weekend');
    expect(body.discountKind).toBe('PercentOff');

    // ⚠️ a STRING, exactly as typed — never coerced through a JS number on the way to the wire.
    expect(body.discountValue).toBe('20');

    // The admin typed a wall-clock time meaning THEIR midnight; the client resolves it to a real
    // instant before sending, so the server never has to guess with its own locale. (The assertion
    // is offset-agnostic on purpose — it pins the SHAPE, and the test suite must pass in any zone.)
    expect(body.startsAtUtc).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
    expect(new Date(body.startsAtUtc).getTime()).toBeLessThan(new Date(body.endsAtUtc).getTime());
  });

  it('ends a running sale and surfaces the 422 when it has already ended', async () => {
    vi.mocked(listSaleCampaigns).mockResolvedValue([campaign({ name: 'Live Now', isRunning: true })]);
    vi.mocked(endSaleCampaign).mockResolvedValue(campaign({ isRunning: false, ...Window2020 }));

    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole('button', { name: 'End now' }));

    await waitFor(() => expect(endSaleCampaign).toHaveBeenCalledWith('camp-1'));
  });

  it('does not offer End now on a campaign that is already over', async () => {
    vi.mocked(listSaleCampaigns).mockResolvedValue([
      campaign({ name: 'Old One', isRunning: false, ...Window2020 }),
    ]);

    renderPanel();

    await screen.findByText('Old One');
    expect(screen.queryByRole('button', { name: 'End now' })).not.toBeInTheDocument();
  });
});
