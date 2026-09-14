# Scheduling-only public link

## Routes

Share `/agendamento/{organizationSlug}`. The existing organization slug is reused. `/agendar/{servicePublicId}` already occupies the single-segment scheduling route, so the new namespace avoids ambiguous slug/public-ID resolution.

One active branch immediately displays its scheduling services. Multiple active branches show a location selection. The optional `?unidade={branchPublicId}` selects only a branch present in the organization's server-provided catalog. No active branches, or no scheduling services in a branch, show an explicit empty state.

Only active AppointmentOnly and Hybrid services are returned. Inactive scheduling settings disable the scheduling action. No queue data or join action is exposed. Hybrid services offer only booking in this portal.

`/agendamento/{slug}/{branchPublicId}/{servicePublicId}` validates the entire organization/branch/service path, then reuses AvailabilityView and BookingForm. Its back link stays inside the scheduling portal. The date, slots, customer details, review and confirmation flow are shared with existing service-specific links. Successful booking still opens `/meu-agendamento/{publicToken}` with the existing cancellation, rescheduling and time-gated appointment check-in rules; this is not an immediate walk-in ticket creation path.

`/unidade/{publicId}`, `/empresa/{slug}` and existing `/agendar` links retain their operational behavior.

## API

New: `GET /api/v1/public/organizations/{slug}/scheduling`, served by PublicAppointmentsController / PublicAppointmentService.GetCatalogAsync.

DTOs: PublicSchedulingOrganizationDto (slug, name, branches), PublicSchedulingBranchDto (publicId, name, address, timeZone, services), PublicSchedulingServiceDto (publicId, name, description, attendanceMode, canSchedule). Queries explicitly scope organization, active branches/services and scheduling settings. No queue identifiers or queue actions are included.

Reused: public branch/service availability, POST public appointments, appointment lookup/cancellation/rescheduling/check-in. The existing slot generator, settings lock, schedules, blocks, capacity, advance windows and timezone remain the authority. Public availability and booking now additionally reject mismatched ownership and inactive organizations.

No schema change or migration.

## Admin

The existing overview card now generates `{QUEUEFLOW_CUSTOMER_URL}/agendamento/{organizationSlug}` from the authenticated dashboard summary. Clipboard and open-link controls are retained; the open control reads "Abrir link". Existing Owner/Admin/Manager visibility is unchanged. Configure QUEUEFLOW_CUSTOMER_URL with the public Customer origin in production.

## Validation

.NET PublicSchedulingCatalogTests tests one/three active branches, inactive branches/services, excluded QueueOnly, included AppointmentOnly/Hybrid, foreign tenant rejection, scoped availability, saved ownership IDs, invalid times, full slots, and preserved operational catalog. Existing concurrency tests cover simultaneous overbooking.

After a Customer build, `npm run test:scheduling --workspace @queueflow/customer-web` runs a Playwright browser fixture test for single/multiple-branch navigation, hybrid-only booking actions, scheduling-only back navigation, form/review/confirmation submission, and invalid branch rejection. Set QUEUEFLOW_PLAYWRIGHT_MODULE and PLAYWRIGHT_BROWSERS_PATH when using an externally installed Playwright. The browser test uses a local mock API; .NET integration tests validate real PostgreSQL booking behavior independently.

Deploy API and Customer together before sharing the new Admin link. Verify production scheduling settings, schedules, timezone and public Customer URL. No production deployment or migration was performed.

Validation results: final dotnet build passed with zero warnings/errors. Domain 38, Application 33, Architecture 2 and Integration 27 tests passed across runs. One integration concurrency case lost its database connection in the combined run; both concurrency cases passed when repeated independently. The 17 web tests, Customer/Admin production builds and Playwright scheduling-flow test passed. The disposable PostgreSQL was removed after testing. No migration was created and no existing application database or production service was changed.
