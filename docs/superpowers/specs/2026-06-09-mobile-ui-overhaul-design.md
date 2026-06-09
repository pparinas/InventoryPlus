# InventoryPlus Mobile-Native UI Overhaul — Design Spec

**Date:** 2026-06-09
**Status:** Approved direction — native-app feel (dark + lime DNA, mobile-native patterns)

## Goals

1. Make every workflow genuinely great on phones — users do everything from mobile (POS, stock, products, expenses, reports, admin, settings).
2. Refresh the UI across the app with a native-app feel while keeping the existing brand identity: dark default, lime accent, Space Grotesk, 7 color schemes, light mode.
3. UI-driven code cleanup: extract inline styles into the design system, split oversized pages into child components as they are redesigned, and fix the offline pending-write persistence bug.

## Decisions made

| Decision | Choice |
|---|---|
| Visual direction | Native-app feel (greeting header, quick actions, horizontal stat scroller, list rows, FAB tab bar) |
| Mobile POS cart | Sticky cart summary bar + bottom sheet (Square/Loyverse pattern) |
| Styling system | Keep and extend the existing custom CSS token system — no Tailwind, no component library |
| Cleanup depth | UI-driven only — refactor what the overhaul touches; no service architecture rework |
| Mobile priority | All pages equally |

## 1. App shell & navigation

### Mobile (<768px)

- Rebuild the mobile bottom bar as a native-style tab bar: **Home · Sales · [ + ] · Products · More**.
- Center slot is a raised lime floating action button (FAB). Tapping it opens a quick-action bottom sheet: New Sale, Add Stock, New Product, New Expense (filtered by feature toggles).
- **More** opens a bottom sheet listing remaining destinations: Stocks, Expenses, Reports, Admin, Settings — respecting `ShowInventoryTab`, `ShowOpexTab`, `IsPosMode`, and admin role gates.
- Remove the hamburger/off-canvas sidebar on phones. Topbar slims to page title + notifications + avatar.
- Safe-area insets respected (notch devices, PWA standalone).

### Desktop (≥768px)

- Keep the fixed sidebar shell, restyled with new tokens: cleaner active states, grouped nav sections.
- Content area gets a max-width container so wide screens don't sprawl.
- Tab bar and FAB hidden on desktop.

## 2. Design system

Extend `wwwroot/css/app.css` (CSS variables remain the theming backbone so dark/light + 7 schemes keep working):

- **Spacing scale** (4/8/12/16/20/24/32) and **type scale** tokens.
- **Touch targets:** 44px minimum on all interactive elements.
- **Component classes:** `card`, `list-row`, `stat-card`, `sheet`, `fab`, `seg-control`, `chip`, plus a small utility layer (`flex`, `gap-*`, `mt-*`, etc.).
- Inline styles (50+ per page in Sales/Dashboard) are replaced with these classes as each page is redesigned.

### New shared Blazor components

| Component | Purpose |
|---|---|
| `BottomSheet` | Slide-up panel: POS cart, More menu, quick actions, mobile forms |
| `ListRow` | Icon + title + meta + trailing value; replaces tables on mobile |
| `StatCard` | KPI card used by Dashboard/Stocks/Reports |
| `Fab` | Floating action button in tab bar |
| `SegmentedControl` | Period/filter switcher sized for thumbs |

Existing `AppModal` / `AppOffCanvas` remain for desktop flows.

## 3. Page redesigns

- **Dashboard** — greeting header with hero revenue number + trend badge, two quick-action buttons, horizontally scrollable stat cards, recent sales as icon list rows.
- **Sales/POS** — product grid with category chips and search; sticky cart summary bar pinned above the tab bar (item count + total + Charge button); tap/swipe opens cart bottom sheet with quantity steppers, payment method selection, and charge. Desktop keeps side-by-side cart panel.
- **Products / Stocks / Expenses / Admin** — mobile: ListRow card lists with key numbers visible without horizontal scrolling; desktop: real tables. Add/edit forms open as bottom sheets on mobile, modals on desktop.
- **Reports** — stat summary cards + segmented period control; charts/tables stack vertically on mobile. Pro gating unchanged.
- **Settings** — grouped settings list (iOS-style sections) instead of the current grid.
- **Login / Register / Landing** — visual polish to match the new look; no structural changes.

## 4. Code cleanup (UI-driven scope)

- Extract inline styles into design-system classes per page as redesigned.
- Split oversized pages (Sales ~845 lines, Reports ~790, Settings ~867, Dashboard) into focused child components.
- **Bug fix:** persist the offline pending-write queue in `Services/InventoryService.cs` to localStorage so queued mutations survive app close/crash; restore and flush on startup/reconnect.

## 5. Verification

After each phase:

1. `dotnet build` passes.
2. Visual verification of affected pages at mobile (~390px) and desktop widths, in dark and light themes (spot-check at least one alternate color scheme).
3. Feature toggles exercised (inventory/opex tabs off, POS mode on/off) where the phase touches navigation.

Phases land incrementally; the app remains usable after each phase.

## Out of scope

- Tailwind / Blazor component libraries.
- Service architecture rework (singleton→scoped, SettingsService split, feature folders).
- New features, backend/Supabase schema changes.
- Test project scaffolding (no test infrastructure exists; verification is build + visual).
