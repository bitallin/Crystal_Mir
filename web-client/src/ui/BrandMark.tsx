export function BrandMark({ compact = false }: { compact?: boolean }) {
  return (
    <div class={`brand-mark${compact ? " brand-mark--compact" : ""}`} aria-hidden="true">
      <span class="brand-mark__halo" />
      <span class="brand-mark__diamond brand-mark__diamond--a" />
      <span class="brand-mark__diamond brand-mark__diamond--b" />
      <span class="brand-mark__core">C</span>
    </div>
  );
}
