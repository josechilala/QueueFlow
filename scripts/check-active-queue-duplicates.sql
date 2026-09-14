-- Read-only preflight. Resolve every returned row explicitly before migration.
SELECT q."Id", q."Name", q."OrganizationId", q."BranchId", q."ServiceId", q."Status"
FROM "Queues" q
JOIN (
    SELECT "OrganizationId", "BranchId", "ServiceId"
    FROM "Queues" WHERE "IsActive" = true
    GROUP BY "OrganizationId", "BranchId", "ServiceId" HAVING count(*) > 1
) duplicates USING ("OrganizationId", "BranchId", "ServiceId")
WHERE q."IsActive" = true;
