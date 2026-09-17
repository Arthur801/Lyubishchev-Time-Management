# Responsive Web Design (RWD) Design

**Date:** 2026-09-17

**Scope:** Implement TODO item 14 across the authenticated application and the Login/Register pages. This work standardizes responsive layout, navigation, touch access, and viewport validation; it does not add business features, change APIs, or alter time-zone/statistical rules.

## Confirmed decisions

- The supported layout bands are desktop at `>= 901px`, tablet/narrow desktop from `721px` through `900px`, and mobile at `<= 720px`.
- `320px` CSS viewport width is the minimum supported width. No authenticated screen may require horizontal page scrolling at that width.
- Mobile navigation has five visible items: Dashboard, Time Entries, Report, Settings, and More.
- More opens an accessible bottom sheet containing Category, Tag, and Logout. It is the mobile route to resource management and account exit.
- The implementation uses shared responsive foundations plus small page-specific adjustments. It does not add a CSS framework or third-party menu/dialog library.

## Responsive foundation

The application will keep its existing design tokens and desktop visual language. A common stylesheet becomes the authority for shell-level behavior: page gutters, fixed/sidebar navigation, mobile header, mobile bottom navigation, bottom safe-area reservation, accessible focus styling, modal width, and minimum interactive dimensions. Page styles keep only the layout rules that are unique to their content.

The current `dashboard.css` already owns most shell styling and uses `900px`, `720px`, and `430px` media queries. The RWD implementation must consolidate duplicated shell selectors there (or in a clearly named `responsive.css` loaded after it), without changing the chosen band boundaries. A `430px` refinement remains permitted for padding and dense component adjustments, but it must not establish a fourth navigation mode.

| Band | Width | Application shell | Content behavior |
| --- | --- | --- | --- |
| Desktop | `>= 901px` | 238px fixed left sidebar; no mobile header/bottom bar | Existing multi-column grids may use available width up to the 1240px content maximum. |
| Tablet / narrow desktop | `721px` through `900px` | Sidebar hidden; mobile header and five-item bottom bar visible | Content keeps useful two-column arrangements where each column remains readable; gutters are 24px. |
| Mobile | `<= 720px` | Mobile header and five-item bottom bar visible | One primary content column; gutters are 16px at `<= 430px`, otherwise 24px. |

The authenticated main content must reserve at least the bottom-bar height plus `env(safe-area-inset-bottom)` whenever the mobile bar is visible. Fixed UI must never conceal the final list row, form submit button, dialog action, or Calendar timeline content.

## Shared authenticated navigation

### Desktop sidebar

The desktop sidebar retains direct links to Dashboard, Time Entries, Report, Category, Tag, Settings, and Logout. The active page uses both the existing active class and `aria-current="page"`. The brand remains a real navigation link to Dashboard; it must not use `href="#"` on authenticated pages.

### Tablet and mobile bottom navigation

Every authenticated page, including Category, Tag, and Settings, renders the same fixed five-item bottom navigation in the tablet and mobile bands:

1. Dashboard
2. Time Entries
3. Report
4. Settings
5. More

The current page has `aria-current="page"` when it is one of the first four destinations. Category and Tag are reached through More and do not add a sixth fixed item. The More trigger uses a `<button type="button">`, `aria-haspopup="dialog"`, and `aria-expanded`. It carries `aria-current="page"` while either Category or Tag is active, so the current information architecture remains perceptible.

The mobile header is visual context only: branded Dashboard link at the left and no placeholder/unused icon button at the right. If future functionality needs a header action it must have its own specification; RWD does not retain inert controls.

### More bottom sheet

More uses the platform `<dialog>` element with a dedicated `.more-sheet` panel. Opening it calls `showModal()` and moves focus to the sheet heading or close control. The panel contains visible links to Category and Tag plus a Logout button using the existing `.app-nav__link--logout` convention, so the existing delegated logout handler continues to send the antiforgery-protected POST.

The sheet provides all of the following:

- a visible close button with an accessible name;
- closing on Escape and backdrop click;
- focus containment supplied by modal dialog behavior;
- focus restoration to the More trigger after it closes;
- no body scroll behind the open sheet;
- a bottom-aligned panel with `max-height` leaving a visible backdrop and safe-area bottom padding.

Only one More sheet can be open. It is closed before normal route navigation. It does not contain settings, because Settings already has a primary bottom-bar destination.

To prevent six copies of the markup drifting, the implementation introduces a Razor partial for mobile navigation and More sheet (with an explicit active section input) and uses it in every authenticated view. Its behavior is implemented once in a small shared JavaScript module loaded from `_Layout.cshtml`. Existing per-page scripts remain responsible only for page data/actions.

## Page-specific layout rules

### Dashboard

- Desktop keeps the existing timer three-column card, three-card summary grid, analytics two-column grid, and three-column recent-entry list.
- Tablet hides the sidebar, retains the horizontal timer fields when they fit, changes analytics to one column, and changes recent entries to two columns.
- Mobile turns the timer card and summary cards into one column; timer form fields, custom-range inputs, and action button stack without clipping. The date range preset row may wrap but every option stays tappable. Recent entries become one column at `<= 430px`.
- Trend bars, donut legend, tag bars, and card headings remain legible. Charts may become vertically taller; labels must not overlap or disappear merely to preserve a fixed height.

### Time Entry History and CSV export

- List/Calendar view toggles remain before their content and wrap safely when necessary.
- Filter controls become a vertical, full-width stack at `<= 720px`; range presets remain one control group and may wrap to two lines instead of overflowing.
- Entry rows use the current compact grid: main information and duration first, then action buttons in a separate right-aligned row. The row never clips long names or tags; text wraps/breaks within its own column while time and duration stay readable.
- The Add Entry and Export CSV buttons remain reachable in the heading. On mobile, heading actions become full-width or a wrapping action row with a minimum 44px height; neither relies on hover.
- The Time Entry dialog and Category/Tag resource dialogs have `width: min(92vw, 520px)`, scroll internally if taller than the usable viewport, and keep actions visible above the keyboard/safe area. Date-time fields are one column on mobile.

### Calendar

- Desktop shows the existing Sunday-to-Saturday weekly time grid.
- Tablet keeps a horizontal weekly grid with deliberately scrollable calendar content; the overall page itself must not acquire horizontal overflow. The grid retains a visible affordance that more days can be scrolled to.
- Mobile uses the existing day-oriented mode. Its toolbar stacks with previous/today/next controls remaining in logical order; the day track fills the usable width and has enough bottom padding above navigation. Calendar blocks clip their own long text with an accessible title/label rather than expanding across other entries.
- Responsive layout must not alter the overlap query, account-time-zone conversion, read-only behavior, or event positioning calculations.

### Report

- Desktop uses the existing two-column category pie and tag-bar panels.
- At `<= 900px`, the panels are one column in the documented order: category first, tags second.
- Range controls use the shared responsive behavior. On mobile, legends and tag labels may wrap, but category percentages and tag durations remain associated with their labels; no tag pie chart is introduced.

### Category, Tag, and Settings

- Resource rows may wrap action buttons to a distinct right-aligned full-width action line at `<= 720px`; swatch/name remain visible and deletion controls retain an accessible label.
- The resource modal uses the shared mobile dialog rules; Category color input spans a readable full width.
- Settings card expands from its desktop 420px maximum to full available content width on mobile. Its save button is at least 44px tall and is visible above the bottom navigation.
- Category, Tag, and Settings all render the shared mobile navigation and therefore are no longer dead ends on touch devices.

### Authentication

Login and Register keep the existing auth layout and do not render authenticated navigation. At `320px` through `720px`, the auth card uses the available width minus 16px gutters, has no fixed minimum width, avoids horizontal scroll, and keeps password-toggle buttons inside their input rows without obscuring typed text. Form controls and submit actions are at least 44px high/tall in the mobile band.

## Interaction, accessibility, and resilience requirements

- Every button, link, input, select, list action, bottom-navigation item, and More-sheet action has a minimum 44px by 44px target in the mobile band. Compact icon buttons may use a visually smaller glyph only inside that hit area.
- Keyboard focus is visible in every band; focus order follows visual reading order after each grid reflow.
- CSS honors `prefers-reduced-motion: reduce` by removing non-essential navigation/menu/chart transitions. It must not remove state changes or focus feedback.
- No layout depends on hover alone. Existing hover styles remain desktop enhancements, with focus-visible and active states providing equivalent feedback.
- Long category/tag names, names containing CJK characters, error messages, and 200% text zoom must wrap inside their card/row instead of causing a viewport-width overflow.
- `dialog` fallback is limited to native browser support for the project's target modern browsers; no polyfill is added in V1. If a dialog cannot be shown, the More trigger remains keyboard focusable and no navigation link is removed from desktop.

## Non-goals

- No new desktop design, theme switcher, animation system, CSS framework, or component library.
- No redesign of the data model, APIs, time-zone behavior, Calendar interaction model, report calculations, or CSV contract.
- No hamburger navigation replacing the agreed five-item bottom bar.
- No route added solely for More; it is a local dialog panel.

## Validation and acceptance criteria

Automated tests should cover the shared More-sheet state module: open/close, Escape, backdrop close, `aria-expanded`, focus restoration, and Category/Tag active-state determination. Existing page-specific JavaScript tests must continue to pass.

Manual viewport verification uses DevTools (or equivalent) at the following minimum matrix:

| Viewport | Required checks |
| --- | --- |
| 320 by 568 | Dashboard, Time Entry list, one resource page, Login; no horizontal page overflow; controls remain reachable. |
| 375 by 667 with safe area simulation | Bottom navigation, More sheet, a long list/resource row, and an open dialog do not hide actions behind fixed UI. |
| 768 by 1024 | Tablet header/bottom nav, Report one-column panels, Calendar weekly horizontal content, and two-column content that remains readable. |
| 1024 by 768 | Desktop sidebar, Dashboard multi-column grids, Report two-column panels, and no mobile navigation. |

At each relevant viewport, also check keyboard-only navigation, More-sheet focus return, 200% browser zoom, a CJK/long-name dataset, and a no-data/error state. The final automated test run and `git diff --check` must pass.

## Consistency with current implementation

The project already has the correct high-level CSS directions: sidebar-to-mobile switch near `900px`, content stacking near `720px`, and compact spacing near `430px`; `calendar.css` already separates weekly and day grids, while `report.css`, `settings.css`, `history.css`, and `category-tag.css` contain page-specific adjustments. This design makes those decisions consistent rather than replacing them.

Current authenticated Razor views duplicate sidebar/header/bottom-nav markup, and Category, Tag, and Settings lack a mobile bottom bar. The RWD implementation explicitly remedies that duplication through a shared partial and supplies the missing reachable routes. It preserves the existing native entry/resource dialogs and the `_Layout.cshtml` delegated logout behavior.
