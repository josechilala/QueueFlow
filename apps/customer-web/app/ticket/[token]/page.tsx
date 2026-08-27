import { notFound } from 'next/navigation';
import { RealtimeRefresh } from '../../realtime-refresh';
import { RefreshTicketButton } from './refresh-ticket-button';
type TicketStatus = 'Waiting' | 'Called' | 'InService' | 'Completed' | 'Cancelled' | 'NoShow' | 'Transferred';
type PublicTicket = { ticketNumber: string; status: TicketStatus; issuedAt: string; position: number; ticketsAhead: number; estimatedMinutes: number; counterName: string | null };
type PublicNotification = { id: string; message: string; createdAt: string; isRead: boolean };
const labels: Record<TicketStatus, string> = { Waiting: 'Aguardando', Called: 'Chamado', InService: 'Em atendimento', Completed: 'Concluído', Cancelled: 'Cancelado', NoShow: 'Não compareceu', Transferred: 'Transferido' };
export default async function TicketPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params; const apiUrl = process.env.QUEUEFLOW_API_URL ?? 'http://localhost:5260';
  const [ticketResponse, notificationResponse] = await Promise.all([fetch(`${apiUrl}/api/v1/public/tickets/${encodeURIComponent(token)}`, { cache: 'no-store' }), fetch(`${apiUrl}/api/v1/public/tickets/${encodeURIComponent(token)}/notifications`, { cache: 'no-store' })]);
  if (ticketResponse.status === 404) notFound(); if (!ticketResponse.ok) throw new Error('Não foi possível consultar a senha.');
  const ticket = await ticketResponse.json() as PublicTicket; const notifications = notificationResponse.ok ? await notificationResponse.json() as PublicNotification[] : [];
  return <main><RealtimeRefresh ticketToken={token} /><div className="brand">QueueFlow</div>
    {notifications.length > 0 && <section className="notification-list">{notifications.map(item => <div className={item.isRead ? 'notification read' : 'notification'} key={item.id}>🔔 <strong>{item.message}</strong></div>)}</section>}
    <article><small>SUA SENHA</small><h1 className="ticket-number">{ticket.ticketNumber}</h1><div className={`availability ticket-${ticket.status.toLowerCase()}`}>{labels[ticket.status]}</div>
      {ticket.status === 'Waiting' && <section className="public-metrics"><div><strong>{ticket.position}º</strong><span>Posição</span></div><div><strong>{ticket.ticketsAhead}</strong><span>Pessoas à frente</span></div><div><strong>{ticket.estimatedMinutes} min</strong><span>Espera estimada</span></div></section>}
      {ticket.status === 'Called' && <section className="call-notice"><small>DIRIJA-SE AO ATENDIMENTO</small><strong>{ticket.counterName ?? 'Aguarde a indicação do guichê'}</strong></section>}
      {ticket.status === 'InService' && <p>Seu atendimento está em andamento.</p>}{ticket.status === 'Completed' && <p>Atendimento concluído. Obrigado!</p>}
      <p className="muted issued-at">Senha emitida em {new Date(ticket.issuedAt).toLocaleString('pt-BR')}</p><RefreshTicketButton />
    </article></main>;
}
