'use client';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { useRouter } from 'next/navigation';
import { useEffect } from 'react';
export function RealtimeRefresh() { const router = useRouter(); useEffect(() => { const connection = new HubConnectionBuilder().withUrl(`${process.env.NEXT_PUBLIC_QUEUEFLOW_API_URL ?? 'http://localhost:5260'}/hubs/queue`).withAutomaticReconnect().build(); let timer: ReturnType<typeof setTimeout>; connection.on('queue.updated', () => { clearTimeout(timer); timer = setTimeout(() => router.refresh(), 150); }); connection.start().catch(() => undefined); return () => { clearTimeout(timer); void connection.stop(); }; }, [router]); return null; }
