import { configuredUrl } from '../../../lib/configured-url';
import type { DisplayCall } from '../../../lib/use-queue-display';
import { notFound } from 'next/navigation';
import { BranchDisplayScreen } from './branch-display-screen';
type PublicDisplay = { branchPublicId: string; organizationName: string; branchName: string; queuePublicIds: string[]; latestCalls: DisplayCall[] };
export default async function BranchDisplayPage({ params }: { params: Promise<{ branchPublicId: string }> }) { const { branchPublicId } = await params; const apiUrl = configuredUrl(process.env.QUEUEFLOW_API_URL, 'http://localhost:5260'); const response = await fetch(`${apiUrl}/api/v1/public/displays/${encodeURIComponent(branchPublicId)}`, { cache: 'no-store' }); if (response.status === 404) notFound(); if (!response.ok) throw new Error('Não foi possível carregar o painel público.'); const display = (await response.json()) as PublicDisplay; return <BranchDisplayScreen queuePublicIds={display.queuePublicIds} organizationName={display.organizationName} branchName={display.branchName} initialCalls={display.latestCalls} />; }
