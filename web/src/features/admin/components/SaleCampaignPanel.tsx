import { useState } from 'react';
import { ApiError, type AdminSaleCampaignDto } from '../../../api/client';
import { formatPrice, toInt } from '../../catalog/format';
import { useCreateSaleCampaign, useEndSaleCampaign, useSaleCampaigns } from '../pricing';

/**
 * The pricing panel on a product's edit page: what is running, what is queued, what ran — plus a
 * form to schedule the next one and a button to stop the current one.
 */
// 📘 THE SHAPE OF THIS UI IS THE DOMAIN'S ARGUMENT, IN PIXELS. There is no "on sale" toggle here,
// because there is no such field to toggle. An admin schedules a WINDOW and walks away; the sale
// starts on Friday whether or not anyone is at a keyboard, and stops on Monday for the same reason.
// The only button that touches a running sale is "End now", and even that is an edit to the window
// rather than a switch — which is why the row stays in the list afterwards, showing when it ran.
//
// ⚠️ note where "is it live?" comes from: `campaign.isRunning`, computed on the SERVER against its
// injected clock. The obvious alternative — `new Date() >= new Date(c.startsAtUtc)` right here — puts
// a second answer to that question in a second place with a second clock, and the day an admin's
// laptop is twelve minutes fast, the panel and the storefront disagree and neither is obviously
// wrong. One question, one answer, one clock.
export function SaleCampaignPanel({
  productId,
  priceAmount,
  priceCurrency,
}: {
  productId: string;
  priceAmount: number | string;
  priceCurrency: string;
}) {
  const campaigns = useSaleCampaigns(productId);

  return (
    <section className="mt-10 rounded-lg border border-neutral-200 bg-white p-4">
      <h2 className="text-lg font-semibold text-neutral-900">Sales</h2>
      <p className="mt-1 text-sm text-neutral-600">
        Schedule a discount for a window of time. It starts and stops on its own.
      </p>

      <ScheduleForm productId={productId} priceCurrency={priceCurrency} />

      <div className="mt-6">
        {campaigns.isPending && <p className="text-sm text-neutral-500">Loading sales…</p>}
        {campaigns.isError && (
          <p className="text-sm text-red-700" role="alert">
            Couldn’t load this product’s sales.
          </p>
        )}
        {campaigns.data?.length === 0 && (
          <p className="text-sm text-neutral-500">No sales scheduled for this product.</p>
        )}
        {campaigns.data && campaigns.data.length > 0 && (
          <ul className="divide-y divide-neutral-100">
            {campaigns.data.map((campaign) => (
              <li key={campaign.id}>
                <CampaignRow
                  campaign={campaign}
                  productId={productId}
                  priceAmount={priceAmount}
                  priceCurrency={priceCurrency}
                />
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}

function CampaignRow({
  campaign,
  productId,
  priceAmount,
  priceCurrency,
}: {
  campaign: AdminSaleCampaignDto;
  productId: string;
  priceAmount: number | string;
  priceCurrency: string;
}) {
  const endCampaign = useEndSaleCampaign(productId);

  // A campaign is over once its window has closed. WHY this one date comparison is fine while the
  // "is it running?" one was not: this decides whether to render a BUTTON, and the server re-checks
  // and returns a 422 if we get it wrong. A wrong guess here is a button that shouldn't be there; a
  // wrong guess about isRunning would be a lie about the price.
  const ended = !campaign.isRunning && new Date(campaign.endsAtUtc) <= new Date();

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 py-3">
      <div>
        <p className="text-sm font-medium text-neutral-900">
          {campaign.name} <Status campaign={campaign} ended={ended} />
        </p>
        <p className="mt-0.5 text-xs text-neutral-600">
          {describeDiscount(campaign, priceAmount, priceCurrency)} · {formatWindow(campaign)}
        </p>
      </div>

      {!ended && (
        <button
          type="button"
          onClick={() => endCampaign.mutate(campaign.id)}
          disabled={endCampaign.isPending}
          className="rounded border border-neutral-300 px-2.5 py-1 text-xs font-medium text-neutral-800 hover:border-neutral-500 disabled:opacity-50"
        >
          {endCampaign.isPending ? 'Ending…' : 'End now'}
        </button>
      )}

      {endCampaign.isError && (
        <p className="w-full text-xs text-red-700" role="alert">
          {endCampaign.error instanceof ApiError && endCampaign.error.status === 422
            ? 'This sale has already ended — refresh to see the current state.'
            : 'Couldn’t end this sale.'}
        </p>
      )}
    </div>
  );
}

function Status({ campaign, ended }: { campaign: AdminSaleCampaignDto; ended: boolean }) {
  if (campaign.isRunning) {
    return (
      <span className="ml-1 rounded bg-emerald-50 px-1.5 py-0.5 text-xs font-medium text-emerald-700">
        Running
      </span>
    );
  }

  return ended ? (
    <span className="ml-1 rounded bg-neutral-100 px-1.5 py-0.5 text-xs font-medium text-neutral-600">
      Ended
    </span>
  ) : (
    <span className="ml-1 rounded bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-800">
      Scheduled
    </span>
  );
}

function ScheduleForm({ productId, priceCurrency }: { productId: string; priceCurrency: string }) {
  const [name, setName] = useState('');
  const [kind, setKind] = useState('PercentOff');
  // ⚠️ a STRING, like every other money-adjacent input in this app. `type="number"` would coerce the
  // admin's "5.00" through a JS double on its way to state; the string goes to the wire untouched and
  // System.Text.Json binds it straight to a C# decimal. See features/catalog/format.ts.
  const [value, setValue] = useState('');
  const [starts, setStarts] = useState('');
  const [ends, setEnds] = useState('');

  const createCampaign = useCreateSaleCampaign(productId);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();

    createCampaign.mutate(
      {
        name,
        discountKind: kind,
        discountValue: value,
        // 📘 THE TIMEZONE DECISION, AND IT IS MADE RIGHT HERE. `<input type="datetime-local">` gives
        // a wall-clock string with no offset ("2026-08-07T00:00") — an ambiguous instant. `new Date()`
        // resolves it in the BROWSER's zone, and .toISOString() then sends a real UTC instant. So the
        // admin types midnight, means THEIR midnight, and the server is told exactly which moment that
        // was. If we sent the naive string instead, the server would resolve it in ITS locale — and a
        // sale set for midnight in Chicago would open at 6pm because a container in Frankfurt guessed.
        // The client is the only party that knows what the admin meant; it converts before sending.
        startsAtUtc: new Date(starts).toISOString(),
        endsAtUtc: new Date(ends).toISOString(),
      },
      {
        onSuccess: () => {
          setName('');
          setValue('');
          setStarts('');
          setEnds('');
        },
      },
    );
  };

  const canSubmit = name.trim() !== '' && value.trim() !== '' && starts !== '' && ends !== '';

  return (
    <form onSubmit={submit} className="mt-4 grid gap-3 sm:grid-cols-2">
      {/* "Sale name", not "Name": this panel lives BELOW the product form on the same page, and two
          controls both labelled "Name" is an accessibility failure before it is a test failure — a
          screen-reader user tabbing here hears "Name, edit text" twice with no way to tell which
          thing they are naming. Labels are for humans; uniqueness is part of being one. */}
      <label className="text-sm">
        <span className="block text-neutral-700">Sale name</span>
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Labour Day Weekend"
          className="mt-1 w-full rounded border border-neutral-300 px-2 py-1"
        />
      </label>

      <label className="text-sm">
        <span className="block text-neutral-700">Discount</span>
        <div className="mt-1 flex gap-2">
          <select
            value={kind}
            onChange={(e) => setKind(e.target.value)}
            aria-label="Discount kind"
            className="rounded border border-neutral-300 px-2 py-1"
          >
            <option value="PercentOff">% off</option>
            <option value="AmountOff">{priceCurrency} off</option>
          </select>
          <input
            type="text"
            inputMode="decimal"
            value={value}
            onChange={(e) => setValue(e.target.value)}
            aria-label="Discount value"
            placeholder={kind === 'PercentOff' ? '20' : '5.00'}
            className="w-full rounded border border-neutral-300 px-2 py-1"
          />
        </div>
      </label>

      <label className="text-sm">
        <span className="block text-neutral-700">Sale starts</span>
        <input
          type="datetime-local"
          value={starts}
          onChange={(e) => setStarts(e.target.value)}
          className="mt-1 w-full rounded border border-neutral-300 px-2 py-1"
        />
      </label>

      <label className="text-sm">
        <span className="block text-neutral-700">Sale ends</span>
        <input
          type="datetime-local"
          value={ends}
          onChange={(e) => setEnds(e.target.value)}
          className="mt-1 w-full rounded border border-neutral-300 px-2 py-1"
        />
      </label>

      <div className="sm:col-span-2">
        <button
          type="submit"
          disabled={!canSubmit || createCampaign.isPending}
          className="rounded bg-neutral-900 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
        >
          {createCampaign.isPending ? 'Scheduling…' : 'Schedule sale'}
        </button>

        {createCampaign.isError && (
          <p className="mt-2 text-sm text-red-700" role="alert">
            {createCampaign.error instanceof ApiError && createCampaign.error.problem?.detail
              ? createCampaign.error.problem.detail
              : 'Couldn’t schedule this sale.'}
          </p>
        )}

        <p className="mt-2 text-xs text-neutral-500">
          Times are in your local timezone. The sale starts and ends on its own — no need to come back.
        </p>
      </div>
    </form>
  );
}

// The panel shows what the discount MEANS at today's list price ("20% off — $40.00") because "20%"
// alone is a number an admin has to do arithmetic on to sanity-check. Note it is a PREVIEW: the real
// price is always resolved server-side, and if the list price changes tomorrow this line changes with
// it — which is exactly why the campaign stores the RULE and not a precomputed sale price.
function describeDiscount(
  campaign: AdminSaleCampaignDto,
  priceAmount: number | string,
  priceCurrency: string,
): string {
  const value = toInt(campaign.discountValue);

  return campaign.discountKind === 'PercentOff'
    ? `${toDecimalText(campaign.discountValue)}% off`
    : `${formatPrice(value, priceCurrency)} off ${formatPrice(priceAmount, priceCurrency)}`;
}

function toDecimalText(value: number | string): string {
  return typeof value === 'string' ? value : String(value);
}

function formatWindow(campaign: AdminSaleCampaignDto): string {
  const format = (iso: string) =>
    new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    }).format(new Date(iso));

  return `${format(campaign.startsAtUtc)} → ${format(campaign.endsAtUtc)}`;
}
