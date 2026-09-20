export function Footer() {
  return (
    <footer className="border-t border-edge">
      <div className="mx-auto max-w-5xl px-5 py-8 text-sm text-ink-muted">
        {/* Computed, not stored: a hard-coded year is wrong every January and nobody edits a footer on New
            Year's Day. */}
        © {new Date().getFullYear()} Your site
      </div>
    </footer>
  );
}
