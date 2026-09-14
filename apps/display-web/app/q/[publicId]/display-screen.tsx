'use client';
import { useQueueDisplay, type DisplayCall } from '../../../lib/use-queue-display';
export function DisplayScreen({ publicId, queueName, branchName, initialCalls }: { publicId: string; queueName: string; branchName: string; initialCalls: DisplayCall[] }) {
  const { current, history, connected } = useQueueDisplay([publicId], initialCalls, 5);
  return <main><header className="display-header"><b>QueueFlow · {queueName}</b><span>{branchName} <i className="connection">{connected ? '● conectado' : '○ reconectando'}</i></span></header><section className="display-grid"><article className="current-call">{current ? <><small>SENHA CHAMADA</small><h1>{current.ticketNumber}</h1><h2>{current.counterName ?? 'Aguarde o guichê'}</h2></> : <div className="waiting-call"><h2>Aguardando próxima chamada</h2></div>}</article><aside className="call-history"><h3>Últimas chamadas</h3>{history.length ? history.map(call => <p key={call.ticketId}><span>{call.ticketNumber}</span><b>{call.counterName}</b></p>) : <p>Nenhuma chamada ainda</p>}</aside></section></main>;
}
