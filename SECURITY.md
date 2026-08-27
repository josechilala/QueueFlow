# Security and privacy baseline

- Production secrets are supplied through environment variables or a secret manager. `.env`, local settings and secret files are ignored by Git.
- Production CORS accepts only explicit HTTPS origins. Wildcards and HTTP origins stop application startup.
- TLS terminates at the application or a trusted reverse proxy. HSTS and HTTPS redirection are enabled outside development.
- Authentication and public endpoints have per-IP rate limits; all endpoints also have a global per-user/IP limit.
- Access and refresh tokens use `HttpOnly`, `Secure` production cookies and are never written to application logs.
- Ticket name and phone are optional and are not returned by public APIs. Closed tickets are anonymized after 90 days by default.
- Processed Outbox records are retained for 7 days, old notifications for at most 90 days, queue metric snapshots for 90 days, expired refresh tokens are removed, and audit records default to 5 years.
- Override retention with `Privacy__TicketPiiRetentionDays` and `Privacy__AuditRetentionDays` according to the organization's legal basis.
