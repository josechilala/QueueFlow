'use client';
import { configuredUrl } from '../../lib/configured-url';

import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useRouter } from 'next/navigation';
import { useEffect } from 'react';
const events = ['QueueUpdated', 'appointment.created', 'appointment.confirmed', 'appointment.cancelled', 'appointment.rescheduled', 'appointment.checked-in', 'appointment.completed', 'appointment.no-show'];
export function RealtimeRefresh({ queuePublicIds }: { queuePublicIds: string[] }) {
  const queueKey = JSON.stringify([...new Set(queuePublicIds)].sort());
  const router = useRouter();
  useEffect(() => {
    const connection = new HubConnectionBuilder().withUrl(`${configuredUrl(process.env.NEXT_PUBLIC_QUEUEFLOW_API_URL, 'http://localhost:5260', true)}/hubs/queue`).withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    let disposed = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const refresh = () => {
      if (disposed || timer) return;
      timer = setTimeout(() => { timer = undefined; if (!disposed) router.refresh(); }, 100);
    };
    const join = async () => {
      await Promise.all((JSON.parse(queueKey) as string[]).map(id => connection.invoke('JoinQueueGroup', id)));
      refresh();
    };
    const scheduleRetry = () => { if (!disposed && !retry) retry = setTimeout(() => { retry = undefined; void start(); }, 3000); };
    const start = async () => {
      try { await connection.start(); if (!disposed) await join(); }
      catch { await connection.stop(); scheduleRetry(); }
    };
    events.forEach(eventName => connection.on(eventName, refresh));
    connection.onreconnected(async () => { try { await join(); } catch { await connection.stop(); scheduleRetry(); } });
    connection.onclose(scheduleRetry);
    void start();
    return () => { disposed = true; clearTimeout(timer); clearTimeout(retry); void connection.stop(); };
  }, [queueKey, router]);
  return null;
}
