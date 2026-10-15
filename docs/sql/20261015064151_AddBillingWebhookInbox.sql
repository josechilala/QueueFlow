START TRANSACTION;
CREATE TABLE "BillingWebhookInbox" (
    "Id" uuid NOT NULL,
    "ProviderEventId" character varying(200) NOT NULL,
    "EventType" character varying(60) NOT NULL,
    "PaymentId" character varying(100),
    "Status" character varying(20) NOT NULL,
    "Attempts" integer NOT NULL,
    "ReceivedAt" timestamp with time zone NOT NULL,
    "NextAttemptAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "LastErrorCode" character varying(80),
    CONSTRAINT "PK_BillingWebhookInbox" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_BillingWebhookInbox_Attempts" CHECK ("Attempts" >= 0 AND "Attempts" <= 12),
    CONSTRAINT "CK_BillingWebhookInbox_Status" CHECK ("Status" IN ('Pending','Processed','Ignored','Review'))
);

CREATE UNIQUE INDEX "IX_BillingWebhookInbox_ProviderEventId" ON "BillingWebhookInbox" ("ProviderEventId");

CREATE INDEX "IX_BillingWebhookInbox_Status_NextAttemptAt" ON "BillingWebhookInbox" ("Status", "NextAttemptAt");

ALTER TABLE "BillingWebhookInbox" ENABLE ROW LEVEL SECURITY;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261015064151_AddBillingWebhookInbox', '10.0.0');

COMMIT;

