'use client';
import { configuredUrl } from '../lib/configured-url';

import { HubConnectionBuilder } from '@microsoft/signalr';
import { useRouter } from 'next/navigation';
import { useEffect } from 'react';
export function RealtimeRefresh() { const router = useRouter(); useEffect(() => { const connection = new HubConnectionBuilder().withUrl(`${configuredUrl(process.env.NEXT_PUBLIC_QUEUEFLOW_API_URL, 'http://localhost:5260', true)}/hubs/queue`).withAutomaticReconnect().build(); let timer: ReturnType<typeof setTimeout>; connection.on('queue.updated', () => { clearTimeout(timer); timer = setTimeout(() => router.refresh(), 150); }); connection.onreconnected(() => router.refresh()); connection.start().catch(() => undefined); return () => { clearTimeout(timer); void connection.stop(); }; }, [router]); return null; }
