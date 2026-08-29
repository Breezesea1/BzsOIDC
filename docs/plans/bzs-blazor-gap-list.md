# Bzs.Blazor gaps for the BzsOIDC WASM migration

This list separates reusable UI-framework gaps from BzsOIDC application concerns. Authentication, antiforgery, permission evaluation, API clients, OIDC consent transactions, and preference persistence belong to BzsOIDC and must not be added to Bzs.Blazor.

The current assessment targets the released `Bzs.Blazor` 0.6.0 package from
nuget.org. No local project reference to `D:\Coding\Bzs.Blazor` is used.

## T01 framework-gate verification (2026-08-29)

NuGet metadata lists released versions 0.3.0, 0.4.0, 0.4.1, 0.5.0, and
0.6.0. The package inspection checked each `lib/net10.0/Bzs.Blazor.dll` and
XML documentation, and expanded the static-web-assets directory. The latest
package is consumable by a standalone WebAssembly project (`dotnet add package
Bzs.Blazor --version 0.6.0` followed by `dotnet build --no-restore`, 0 warnings,
0 errors). Every inspected release contains the fingerprinted CSS, library
module, and component assets required for WASM static-web-assets delivery.

| Migration gate | Result | Evidence in the released package |
| --- | --- | --- |
| Password input | Pass | `BzsPasswordInput` is present with `Revealable`, localized show/hide labels, and EditForm input members in 0.3.0–0.6.0. The 0.3.0 release notes document focus/caret-preserving reveal and browser, trimming, and AOT coverage. |
| Text/email/search immediate binding | Blocked | `BzsTextInputType` and `BzsInputUpdateMode` (`Change`/`Input`) are present, including IME-safe handling. However, `BzsTextInput` still derives from `InputBase<T>` and the published component contract/docs only exercise it inside `EditForm`; there is no documented or tested standalone (outside-`EditForm`) binding contract required by the migration. |
| DataGrid provider refresh | Pass | `BzsDataGrid<TItem>.RefreshAsync()` is present from 0.3.0 onward. Its XML contract describes latest-state-wins provider refresh, accepted-row retention, failure propagation, selection reconciliation, and disposal-safe pre-interactive calls. |
| NavItem route activation | **Blocked** | `BzsNavItem` exposes `Href`, `Match`, `Active`, and `Disabled`, but no typed activation callback (`Activated`, `Click`, or equivalent) in any inspected release through 0.6.0. `BzsMenuItem.Activated` is a separate command API and does not satisfy route-item activation. |
| Temporary navigation-drawer focus lifecycle | Pass | `CloseOnEscape` and `InitialFocusSelector` are public from 0.3.0; 0.3.0 release notes explicitly cover initial focus, Tab containment, Escape, background isolation, scroll locking, viewport changes, disposal, and close-focus restoration. |

The framework prerequisite remains **blocked**: no released version satisfies
all five gates. The concrete upstream work still required is (1) a standalone
`BzsTextInput` binding contract (or a documented wrapper contract) and tests,
and (2) a typed route-item activation callback that preserves normal
`NavLink` behavior and excludes disclosure toggles. BzsOIDC project/package
configuration is intentionally unchanged until those capabilities ship.

## Required before migration

### 1. Password input

`BzsTextInput` always renders `type="text"`. BzsOIDC needs password fields for login, registration, password confirmation, and user administration.

Add a dedicated password input rather than allowing arbitrary input types on `BzsTextInput`.

Acceptance criteria:

- Integrates with `EditForm`, `EditContext`, validation, `Value`, and `ValueChanged` in the same way as the existing Bzs inputs.
- Supports `current-password` and `new-password` autocomplete values through normal attributes.
- Supports an optional reveal control with localized accessible labels for show and hide.
- Keeps focus and caret position when visibility changes.
- Uses `type="password"` by default and never sends the password value through JavaScript.
- Supports disabled, read-only, required, description, validation error, and unmatched input attributes.
- Works under Interactive WebAssembly and remains usable as native markup under static rendering.
- Includes bUnit coverage for validation, attribute forwarding, controlled value changes, and reveal behavior.

### 2. Text and search input modes

`BzsTextInput` updates `CurrentValueAsString` only on `change`. The administration screens require live search and several editors currently react on every `input` event.

Add an explicit, backward-compatible update mode, such as `Change` and `Input`, with `Change` remaining the default. Also provide supported native text, email, and search semantics without allowing the password behavior to leak into the general text component.

Acceptance criteria:

- Input mode updates `Value`, invokes `ValueChanged`, and notifies the active `EditContext` on each input event.
- Change mode preserves the current behavior.
- Text, email, and search modes render the corresponding native input type.
- The component has a documented, tested binding contract when used outside an `EditForm`, as required by search toolbars.
- `Id`, `Name`, `Placeholder`, autocomplete, input mode, `aria-*`, and unmatched attributes reach the native input.
- The component does not wire competing `input` and `change` handlers that produce duplicate updates.
- IME composition used for Chinese text does not commit broken intermediate values.
- Tests cover both modes, EditContext field notification, and Chinese IME-safe behavior where it can be exercised.

### 3. Explicit DataGrid provider refresh

`BzsDataGrid<TItem>` can retry a failed provider request internally, but it exposes no public way to reload the current provider request after a successful create, update, delete, role assignment, or bulk operation.

Expose a supported refresh contract, preferably an async component method or a controlled refresh token.

Acceptance criteria:

- Reloads the current page, page size, sort, and filters without requiring the consumer to replace the provider instance.
- Uses the existing request coordinator so a newer refresh supersedes stale in-flight results.
- Preserves the last accepted rows while a background refresh is running.
- Reconciles selected items by `ItemKey` after new rows are accepted.
- Surfaces failures through the existing error state and `ProviderFailed` callback.
- Is safe when called before first interactive render, during disposal, or more than once concurrently.
- Includes tests for refresh-after-mutation, stale result suppression, error/retry, selection reconciliation, and disposal.

### 4. Navigation-item activation

`BzsNavigationDrawer` exposes controlled open state, but `BzsNavItem` cannot notify the application when a route link is activated. The mobile administration drawer must close after navigation without replacing Bzs navigation items with application-specific links.

Acceptance criteria:

- A route item exposes a typed activation callback that runs for pointer and keyboard activation.
- The normal `NavLink` navigation and active-state behavior remain intact.
- Disabled items never invoke the callback.
- The callback does not fire for disclosure toggles unless that behavior is explicitly modeled separately.
- Tests cover router links, controlled active links, keyboard activation, and disabled items.

### 5. Responsive navigation-drawer focus lifecycle

The responsive navigation drawer supports backdrop closure but has no Escape handling, initial focus, focus containment, or focus restoration. Those behaviors are required when the responsive drawer becomes a modal mobile overlay.

Acceptance criteria:

- The temporary/overlay presentation can close on Escape and reports a controlled open-state request.
- Opening moves focus to a configured target or the first suitable control.
- Tab focus remains within the open modal drawer.
- Closing by navigation, Escape, or backdrop restores focus to the invoking control when it still exists.
- Persistent desktop presentation does not trap or redirect focus.
- Behavior is lifecycle-safe under WebAssembly navigation and disposal.
- Tests cover Escape, backdrop, focus entry, focus containment, restoration, variant changes, and disposal.

## Recommended for administration ergonomics

### 6. DataGrid footer visibility

The current DataGrid always renders the page-size selector and pagination footer. Some topology and compact administration views need a deliberately unpaged table or an externally controlled footer.

Acceptance criteria:

- Consumers can independently hide the page-size selector and pagination controls without CSS overrides.
- Hiding visual controls does not remove paging information from provider requests unless an explicit unpaged mode is introduced.
- Accessible row and table semantics remain intact when the footer is hidden.
- Existing defaults remain unchanged.

### 7. Current-page select all

Multiple selection renders one checkbox per row, but the selection header has no select-all control. User administration has bulk operations where selecting all visible rows is expected.

Acceptance criteria:

- Offers an opt-in current-page select-all checkbox for multiple-selection mode.
- Supports checked, unchecked, and indeterminate states.
- Respects `ItemKey` and the configured comparer.
- Does not silently select rows on pages the user has not viewed.
- Reports the complete controlled selection through `SelectedItemsChanged`.
- Has an explicit accessible label and keyboard behavior.

### 8. Busy dialog dismissal

The application can currently bind `ShowCloseButton`, `CloseOnEscape`, and `CloseOnBackdropClick` separately while a save is running. A single controlled busy or dismissal-disabled contract would make that invariant harder to violate.

Acceptance criteria:

- While dismissal is disabled, the close button, Escape, and backdrop cannot close the dialog.
- Focus containment, initial focus, footer content, and eventual focus restoration continue to work.
- Existing dismissal options and defaults remain backward compatible.

### 9. DataGrid column sizing and resize

The current BootstrapBlazor administration tables expose minimum widths and pointer resizing. This is not required for the redesigned responsive UI, but it is a real parity gap if adjustable columns are retained.

Acceptance criteria:

- Columns can declare width and minimum width without consumer CSS selectors.
- Any opt-in resize interaction supports keyboard as well as pointer input.
- Width changes preserve horizontal overflow behavior and can be observed by the consumer when persistence is required.
- Existing columns remain non-resizable by default.

## Optional framework polish

### 10. Link-styled actions

`BzsButton` renders a native button and `BzsNavItem` is navigation-menu-specific. A link action with the button variant, size, icon, disabled, target, rel, and enhanced-navigation contracts would remove repeated styled anchors from dashboards, account pages, and recovery pages.

This is not a blocker because BzsOIDC can compose a native anchor with application-owned styling.

### 11. Identity and administration icon coverage

The built-in icon set covers generic actions such as search, menu, status, and chevrons, but not the common identity and administration vocabulary needed by this application.

Consider adding icons for password visibility, user, users, role/shield, key, lock, client/application, scope, topology, dashboard, settings, language, theme, logout, add, edit, delete, save, refresh, copy, and external navigation.

This is not a migration blocker because BzsOIDC can define application-owned `BzsIconData` values. Icons added to the framework should be broadly reusable and follow the existing visual and accessibility conventions.

### 12. Panel and validation-summary conveniences

A titled panel composed from `BzsSurface`, and a form-level validation summary composed from `BzsMessage`, would reduce repeated markup. Neither is a missing primitive, so they should be added only if the framework wants standardized convenience components.

## Confirmed existing capabilities

The following are not gaps in Bzs.Blazor 0.2.2:

- WebAssembly compatibility and browser-safe JS interop.
- App shell, app bar, responsive navigation drawer, navigation menu, and breadcrumbs.
- Dialog, drawer, popover, menu, tooltip, toast, and overlay host.
- DataGrid provider mode, typed columns, templates, sorting, filtering, paging, loading, empty, error, retry, and controlled row selection.
- Text area, checkbox, toggle, radio group, select with descriptions, multi-select, autocomplete, date input, number input, and file selection.
- Buttons with submit/reset semantics, loading state, variants, icons, and accessible names.
- Message, empty state, skeleton, badge, chip, avatar, surface, stack, grid, tabs, and theme provider.

## Application-owned work

Do not add these to Bzs.Blazor:

- Identity cookie login, registration, logout, and external-login redirects.
- Antiforgery token acquisition and HTTP request decoration.
- Authentication-state and permission-state providers.
- BzsOIDC administration API clients and DTOs.
- OIDC authorization-consent transaction storage or protocol handling.
- Theme and culture persistence.
- Domain-specific editors for OIDC clients, release scopes, roles, permissions, users, or permission topology.
- BzsOIDC-specific responsive page composition and branding.
