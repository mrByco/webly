export interface FeatureItem {
  title: string;
  description?: string;
}

export interface FeaturesProps {
  title?: string;
  items: FeatureItem[];
}

/** Three to six things this business offers. Three or six read best; two looks unfinished. */
export function Features({ title, items }: FeaturesProps) {
  return (
    <section className="mx-auto max-w-5xl px-5 py-16">
      {title && <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{title}</h2>}

      <ul className="mt-8 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
        {items.map(item => (
          <li key={item.title} className="rounded-card border border-edge bg-white p-6">
            <h3 className="font-semibold">{item.title}</h3>
            {item.description && <p className="mt-2 text-ink-muted">{item.description}</p>}
          </li>
        ))}
      </ul>
    </section>
  );
}
