import type { ReactNode } from 'react';
import type { ItemType } from './types';

interface IconProps {
  size?: number;
}

function Svg({ size = 16, children, fill = 'none' }: { size?: number; children: ReactNode; fill?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill={fill}
      stroke="currentColor"
      strokeWidth={1.4}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      {children}
    </svg>
  );
}

export function ItemIcon({ type, size = 16 }: { type: ItemType; size?: number }) {
  switch (type) {
    case 'pr':
      return <PullRequestIcon size={size} />;
    case 'issue':
      return (
        <Svg size={size}>
          <circle cx="8" cy="8" r="6" />
          <circle cx="8" cy="8" r="1" fill="currentColor" />
        </Svg>
      );
    case 'repo':
      return <RepoIcon size={size} />;
    case 'release':
      return (
        <Svg size={size}>
          <path d="M2.5 2.5h5.2l6 6-5.2 5.2-6-6z" />
          <circle cx="5.5" cy="5.5" r="0.8" fill="currentColor" />
        </Svg>
      );
    default:
      return (
        <Svg size={size}>
          <circle cx="8" cy="8" r="2" fill="currentColor" />
        </Svg>
      );
  }
}

export const PullRequestIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <circle cx="4.5" cy="3.5" r="1.5" />
    <circle cx="4.5" cy="12.5" r="1.5" />
    <circle cx="11.5" cy="12.5" r="1.5" />
    <path d="M4.5 5v6M11.5 11V7.5a2 2 0 0 0-2-2H8" />
  </Svg>
);

export const RepoIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M3 12.5V3.5a1.5 1.5 0 0 1 1.5-1.5H13v10H4.5A1.5 1.5 0 0 0 3 13.5a1.5 1.5 0 0 0 1.5 1.5H13" />
  </Svg>
);

export const BellIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M3.5 11.5h9l-1-1.5V7a3.5 3.5 0 0 0-7 0v3z" />
    <path d="M6.5 13.5a1.6 1.6 0 0 0 3 0" />
  </Svg>
);

export const HomeIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M2.5 7.5 8 2.5l5.5 5V13a.5.5 0 0 1-.5.5H3a.5.5 0 0 1-.5-.5z" />
    <path d="M6.5 13.5V9.5h3v4" />
  </Svg>
);

export const AssignedIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <circle cx="8" cy="5.5" r="2.5" />
    <path d="M3 13.5c.6-2.4 2.5-3.5 5-3.5s4.4 1.1 5 3.5" />
  </Svg>
);

export const UsersIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <circle cx="6" cy="5.5" r="2.2" />
    <path d="M1.8 13c.5-2.2 2.2-3.2 4.2-3.2s3.7 1 4.2 3.2" />
    <path d="M10.5 3.6a2.2 2.2 0 0 1 0 4.2M11.6 9.9c1.5.4 2.3 1.4 2.6 3.1" />
  </Svg>
);

export const StarIcon = ({ size = 16, filled = false }: IconProps & { filled?: boolean }) => (
  <Svg size={size} fill={filled ? 'currentColor' : 'none'}>
    <path d="m8 1.8 1.9 4 4.3.6-3.1 3 .8 4.3L8 11.6l-3.9 2.1.8-4.3-3.1-3 4.3-.6z" />
  </Svg>
);

export const SettingsIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <circle cx="8" cy="8" r="2" />
    <path d="M8 1.5v1.8M8 12.7v1.8M1.5 8h1.8M12.7 8h1.8M3.4 3.4l1.3 1.3M11.3 11.3l1.3 1.3M12.6 3.4l-1.3 1.3M4.7 11.3l-1.3 1.3" />
  </Svg>
);

export const SearchIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <circle cx="7" cy="7" r="4.5" />
    <path d="m10.5 10.5 3 3" />
  </Svg>
);

export const RefreshIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M13.5 8a5.5 5.5 0 1 1-1.7-4M13.5 2.5v3h-3" />
  </Svg>
);

export const ExternalIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M9 2.5h4.5V7M13.5 2.5 7.5 8.5M11.5 9v3.5a1 1 0 0 1-1 1h-7a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1H7" />
  </Svg>
);

export const CopyIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <rect x="5.5" y="5.5" width="8" height="8" rx="1.5" />
    <path d="M10.5 5.5v-2a1 1 0 0 0-1-1h-6a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2" />
  </Svg>
);

export const CloseIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="m3.5 3.5 9 9M12.5 3.5l-9 9" />
  </Svg>
);

export const DetailIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M3.5 1.8h5.6l3.4 3.4v8.2a.8.8 0 0 1-.8.8H3.5a.8.8 0 0 1-.8-.8V2.6a.8.8 0 0 1 .8-.8z" />
    <path d="M5.5 8h5M5.5 10.5h5" />
  </Svg>
);

export const GridIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <rect x="2.5" y="2.5" width="4.5" height="4.5" rx="1" />
    <rect x="9" y="2.5" width="4.5" height="4.5" rx="1" />
    <rect x="2.5" y="9" width="4.5" height="4.5" rx="1" />
    <rect x="9" y="9" width="4.5" height="4.5" rx="1" />
  </Svg>
);

export const ListIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M2.5 4h11M2.5 8h11M2.5 12h11" />
  </Svg>
);

export const CompactIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    {[3, 8, 13].flatMap((x) => [3, 8, 13].map((y) => <circle key={`${x}-${y}`} cx={x} cy={y} r="1" fill="currentColor" />))}
  </Svg>
);

export const DotsIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <circle cx="3.5" cy="8" r="1" fill="currentColor" />
    <circle cx="8" cy="8" r="1" fill="currentColor" />
    <circle cx="12.5" cy="8" r="1" fill="currentColor" />
  </Svg>
);

export const FilterIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M2 3.5h12L9.5 9v4l-3-1.5V9z" />
  </Svg>
);

export const SortIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="M4.5 13V3M2.5 5l2-2 2 2M11.5 3v10M9.5 11l2 2 2-2" />
  </Svg>
);

export const ChevronDownIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="m4 6 4 4 4-4" />
  </Svg>
);

export const CheckIcon = ({ size = 16 }: IconProps) => (
  <Svg size={size}>
    <path d="m3.5 8.5 3 3 6-7" />
  </Svg>
);
