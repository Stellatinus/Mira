import type { ItemType } from './types';

const base = {
  width: 16,
  height: 16,
  viewBox: '0 0 16 16',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.5,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
};

export function ItemIcon({ type }: { type: ItemType }) {
  switch (type) {
    case 'pr':
      return (
        <svg {...base}>
          <circle cx="4.5" cy="3.5" r="1.5" />
          <circle cx="4.5" cy="12.5" r="1.5" />
          <circle cx="11.5" cy="12.5" r="1.5" />
          <path d="M4.5 5v6M11.5 11V7.5a2 2 0 0 0-2-2H8" />
        </svg>
      );
    case 'issue':
      return (
        <svg {...base}>
          <circle cx="8" cy="8" r="6" />
          <circle cx="8" cy="8" r="1" fill="currentColor" />
        </svg>
      );
    case 'release':
      return (
        <svg {...base}>
          <path d="M2.5 2.5h5.2l6 6-5.2 5.2-6-6z" />
          <circle cx="5.5" cy="5.5" r="0.8" fill="currentColor" />
        </svg>
      );
    default:
      return (
        <svg {...base}>
          <circle cx="8" cy="8" r="2" fill="currentColor" />
        </svg>
      );
  }
}

export function Star({ size = 22 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden>
      <path fill="var(--star)" d="M12 1l2.4 8.6L23 12l-8.6 2.4L12 23l-2.4-8.6L1 12l8.6-2.4z" />
    </svg>
  );
}
