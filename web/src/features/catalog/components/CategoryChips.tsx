import type { CategoryDto } from '../../../api/client';

interface CategoryChipsProps {
  categories: CategoryDto[];
  selected: string | null;
  onSelect: (slug: string | null) => void;
}

export function CategoryChips({ categories, selected, onSelect }: CategoryChipsProps) {
  const chipClass = (active: boolean) =>
    [
      'rounded-full border px-3 py-1 text-sm transition',
      active
        ? 'border-neutral-900 bg-neutral-900 text-white'
        : 'border-neutral-300 text-neutral-700 hover:border-neutral-500',
    ].join(' ');

  return (
    <div className="flex flex-wrap gap-2" role="group" aria-label="Filter by category">
      <button type="button" className={chipClass(selected === null)} onClick={() => onSelect(null)}>
        All
      </button>
      {categories.map((category) => (
        <button
          key={category.slug}
          type="button"
          className={chipClass(selected === category.slug)}
          onClick={() => onSelect(category.slug)}
        >
          {category.name}
        </button>
      ))}
    </div>
  );
}
