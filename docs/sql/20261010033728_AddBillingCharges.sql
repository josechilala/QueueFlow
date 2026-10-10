START TRANSACTION;
CREATE TABLE "BillingCharges" (
    "Id" uuid NOT NULL,
    "OrganizationId" uuid NOT NULL,
    "PlanCode" character varying(40) NOT NULL,
    "Cycle" character varying(20) NOT NULL,
    "PaymentMethod" character varying(30) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character varying(3) NOT NULL,
    "IdempotencyKey" character varying(120) NOT NULL,
    "AsaasPaymentId" character varying(120),
    "AsaasCustomerId" character varying(120),
    "AsaasSubscriptionId" character varying(120),
    "Status" character varying(20) NOT NULL,
    "PaidAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_BillingCharges" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_BillingCharges_AmountCurrency" CHECK ("Amount" > 0 AND "Currency" = 'BRL'),
    CONSTRAINT "CK_BillingCharges_Selection" CHECK ("PlanCode" IN ('professional','enterprise') AND "Cycle" IN ('Monthly','Yearly') AND "PaymentMethod" IN ('Pix','CreditCard')),
    CONSTRAINT "CK_BillingCharges_Status" CHECK ("Status" IN ('Pending','Uncertain','Paid','Cancelled','Refunded') AND ("Status" <> 'Paid' OR ("PaidAt" IS NOT NULL AND "AsaasPaymentId" IS NOT NULL))),
    CONSTRAINT "FK_BillingCharges_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "BillingWebhookEvents" (
    "Id" uuid NOT NULL,
    "ProviderEventId" character varying(200) NOT NULL,
    "ChargeId" uuid NOT NULL,
    "EventType" character varying(60) NOT NULL,
    "ProcessedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_BillingWebhookEvents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_BillingWebhookEvents_BillingCharges_ChargeId" FOREIGN KEY ("ChargeId") REFERENCES "BillingCharges" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_BillingWebhookEvents_ChargeId" ON "BillingWebhookEvents" ("ChargeId");

CREATE UNIQUE INDEX "IX_BillingWebhookEvents_ProviderEventId" ON "BillingWebhookEvents" ("ProviderEventId");

CREATE UNIQUE INDEX "IX_BillingCharges_AsaasPaymentId" ON "BillingCharges" ("AsaasPaymentId") WHERE "AsaasPaymentId" IS NOT NULL;

CREATE INDEX "IX_BillingCharges_AsaasSubscriptionId" ON "BillingCharges" ("AsaasSubscriptionId");

CREATE INDEX "IX_BillingCharges_OrganizationId_CreatedAt" ON "BillingCharges" ("OrganizationId", "CreatedAt");

CREATE UNIQUE INDEX "IX_BillingCharges_OrganizationId_IdempotencyKey" ON "BillingCharges" ("OrganizationId", "IdempotencyKey");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261010033728_AddBillingCharges', '10.0.0');

COMMIT;
