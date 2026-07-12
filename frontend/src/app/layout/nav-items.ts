/** A single primary-navigation entry. Add an item here to add a sidebar link. */
export interface NavItem {
  label: string;
  route: string;
  /** Inline SVG path data (24x24 viewBox) for the item icon. */
  iconPath: string;
}

export const NAV_ITEMS: readonly NavItem[] = [
  {
    label: 'Dashboard',
    route: '/dashboard',
    iconPath: 'M3 13h8V3H3v10Zm0 8h8v-6H3v6Zm10 0h8V11h-8v10Zm0-18v6h8V3h-8Z',
  },
  {
    label: 'Portfolios',
    route: '/portfolios',
    iconPath:
      'M4 4h16a1 1 0 0 1 1 1v3H3V5a1 1 0 0 1 1-1Zm-1 6h18v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-9Zm6 3v2h6v-2H9Z',
  },
  {
    label: 'Accounts',
    route: '/accounts',
    iconPath:
      'M12 3 2 8l10 5 10-5-10-5Zm0 7.2L5.3 7 12 5l6.7 2L12 10.2ZM4 12v4c0 1.7 3.6 3 8 3s8-1.3 8-3v-4l-8 4-8-4Z',
  },
  {
    label: 'Performance',
    route: '/performance',
    iconPath:
      'M3 3h2v16h16v2H3V3Zm4 10 4-4 3 3 5-5 1.4 1.4L14 10l-3-3-4 4-1.4-1.4L7 13Z',
  },
  {
    label: 'Settings',
    route: '/settings',
    iconPath:
      'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm9 4c0-.7-.1-1.3-.2-2l2-1.6-2-3.4-2.4 1a7.6 7.6 0 0 0-1.7-1l-.4-2.6H9.7l-.4 2.6c-.6.3-1.2.6-1.7 1l-2.4-1-2 3.4L3.2 10a8 8 0 0 0 0 4l-2 1.6 2 3.4 2.4-1c.5.4 1.1.7 1.7 1l.4 2.6h4.6l.4-2.6c.6-.3 1.2-.6 1.7-1l2.4 1 2-3.4-2-1.6c.1-.7.2-1.3.2-2Z',
  },
];
