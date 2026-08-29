# Secure the WebAssembly API with cookie sessions and antiforgery

The same-origin WebAssembly application uses the ASP.NET Core Identity HttpOnly cookie and keeps no access token. A non-cacheable session endpoint exposes only the current display identity, roles, and permissions; protected APIs remain authoritative and return Problem Details with stable error codes and 401 or 403 status rather than redirecting.

Every cookie-authenticated state change, including login, registration, logout, consent, and administration, requires an antiforgery request token held only in browser memory. Identity transitions invalidate that token. Explicit login and logout events synchronize tabs without polling, while visibility changes, protected navigation, and API 401 responses refresh the session summary.

Anonymous authentication failures do not distinguish unknown, invalid, locked, or disallowed accounts, and account endpoints are rate limited. Ordinary sessions use a browser-session cookie with a twelve-hour sliding server ticket; remembered sessions persist for at most fourteen days. Production security headers include an enforced, tested CSP after a report-only compatibility phase, and account or administration APIs do not enable cross-origin access.
