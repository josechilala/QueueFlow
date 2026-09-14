'use client';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';

export type DisplayCall = { ticketId: string; queueId: string; ticketNumber: string; counterName: string | null; calledAt: string };

export function useQueueDisplay(queuePublicIds: string[], initialCalls: DisplayCall[], limit: number) {
  const router = useRouter();
  const queueKey = JSON.stringify([...new Set(queuePublicIds)].sort());
  const [history, setHistory] = useState(initialCalls.slice(0, limit));
  const [connected, setConnected] = useState(false);
  useEffect(() => { setHistory(initialCalls.slice(0, limit)); }, [initialCalls, limit]);
  useEffect(() => {
    const connection = new HubConnectionBuilder().withUrl(`${process.env.NEXT_PUBLIC_QUEUEFLOW_API_URL ?? 'http://localhost:5260'}/hubs/queue`).withAutomaticReconnect().build();
    let disposed = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let retry: ReturnType<typeof setTimeout> | undefined;
    const refresh = () => {
      if (disposed || timer) return;
      timer = setTimeout(() => { timer = undefined; if (!disposed) router.refresh(); }, 100);
    };
    const join = async () => {
      await Promise.all((JSON.parse(queueKey) as string[]).map(id => connection.invoke('JoinQueueGroup', id)));
      if (!disposed) { setConnected(true); refresh(); }
    };
    const scheduleRetry = () => { if (!disposed && !retry) retry = setTimeout(() => { retry = undefined; void start(); }, 3000); };
    const start = async () => {
      try { await connection.start(); if (!disposed) await join(); }
      catch { await connection.stop(); scheduleRetry(); }
    };
    connection.on('TicketCalled', (call: DisplayCall) => {
      if (!disposed) setHistory(items => [call, ...items.filter(item => item.ticketId !== call.ticketId)]
        .sort((a, b) => Date.parse(b.calledAt) - Date.parse(a.calledAt)).slice(0, limit));
    });
    connection.on('QueueUpdated', refresh);
    connection.onreconnecting(() => { if (!disposed) setConnected(false); });
    connection.onreconnected(async () => { try { await join(); } catch { await connection.stop(); scheduleRetry(); } });
    connection.onclose(() => { if (!disposed) setConnected(false); scheduleRetry(); });
    void start();
    return () => { disposed = true; clearTimeout(timer); clearTimeout(retry); void connection.stop(); };
  }, [queueKey, router, limit]);
  return { current: history[0], history, connected };
}
