import { ActivationFlow } from './activation-flow';
export default async function ActivationPage({ params }: { params: Promise<{ token: string }> }) { const { token } = await params; return <main><div className="brand">QueueFlow</div><article><ActivationFlow token={token} /></article></main>; }
