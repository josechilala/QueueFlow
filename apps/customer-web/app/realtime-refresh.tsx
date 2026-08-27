'use client';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useRouter } from 'next/navigation';
import { useEffect } from 'react';
const events = ['ticket.issued', 'ticket.called', 'ticket.recalled', 'ticket.started', 'ticket.completed', 'ticket.cancelled', 'ticket.no_show', 'appointment.created', 'appointment.confirmed', 'appointment.cancelled', 'appointment.rescheduled', 'appointment.checked-in', 'appointment.no-show', 'availability.updated', 'queue.updated', 'notification.created'];
export function RealtimeRefresh({ queuePublicId, ticketToken }: { queuePublicId?: string; ticketToken?: string }) {
  const router = useRouter();
  useEffect(() => {
    const connection = new HubConnectionBuilder().withUrl(`${process.env.NEXT_PUBLIC_QUEUEFLOW_API_URL ?? 'http://localhost:5260'}/hubs/queue`).withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const refresh = () => { clearTimeout(timer); timer = setTimeout(() => router.refresh(), 100); };
    events.forEach(eventName => connection.on(eventName, refresh));
    connection.start().then(async () => { if (queuePublicId) await connection.invoke('JoinQueueGroup', queuePublicId); if (ticketToken) await connection.invoke('JoinTicketGroup', ticketToken); }).catch(() => undefined);
    return () => { clearTimeout(timer); void connection.stop(); };
  }, [queuePublicId, ticketToken, router]);
  return null;
}
