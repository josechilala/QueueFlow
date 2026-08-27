import { notFound } from 'next/navigation';
import { DisplayScreen } from './display-screen';
type PublicQueue = { publicId: string; name: string; branchName: string };
export default async function DisplayPage({ params }: { params: Promise<{ publicId: string }> }) { const { publicId } = await params; const apiUrl = process.env.QUEUEFLOW_API_URL ?? 'http://localhost:5260'; const response = await fetch(`${apiUrl}/api/v1/public/queues/${encodeURIComponent(publicId)}`, { cache: 'no-store' }); if (response.status === 404) notFound(); if (!response.ok) throw new Error('Não foi possível carregar o painel.'); const queue = (await response.json()) as PublicQueue; return <DisplayScreen publicId={queue.publicId} queueName={queue.name} branchName={queue.branchName} />; }
