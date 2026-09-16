START TRANSACTION;
ALTER TABLE "Users" DROP CONSTRAINT "CK_Users_CanonicalEmail";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20260916010353_EnforceCanonicalUserEmail';

COMMIT;

START TRANSACTION;
ALTER TABLE "Organizations" DROP COLUMN "OnboardingCompletedAt";

ALTER TABLE "OrganizationInvitations" DROP COLUMN "ActivationAuthorizationConsumedAt";

ALTER TABLE "OrganizationInvitations" DROP COLUMN "ActivationAuthorizationExpiresAt";

ALTER TABLE "OrganizationInvitations" DROP COLUMN "ActivationAuthorizationHash";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20260915201718_SecureActivationAndOnboarding';

COMMIT;

START TRANSACTION;
DROP FUNCTION IF EXISTS queueflow_normalize_email(text);

DROP TABLE "OrganizationInvitations";

DROP TABLE "PlatformAuditLogs";

DROP TABLE "PlatformRefreshTokens";

DROP TABLE "PlatformUsers";

DROP INDEX "IX_Users_Email";

CREATE UNIQUE INDEX "IX_Users_OrganizationId_Email" ON "Users" ("OrganizationId", "Email");

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20260915060310_AddPlatformAdministrationAndInvitations';

COMMIT;
