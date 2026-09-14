'use client';
import { useQueueDisplay, type DisplayCall } from '../../../lib/use-queue-display';
export function BranchDisplayScreen({ queuePublicIds, organizationName, branchName, initialCalls }: { queuePublicIds: string[]; organizationName: string; branchName: string; initialCalls: DisplayCall[] }) {
  const { current, history, connected } = useQueueDisplay(queuePublicIds, initialCalls, 10);
  return <main><header className="display-header"><b>{organizationName}</b><span>{branchName} <i className="connection">{connected ? '● conectado' : '○ reconectando'}</i></span></header><section className="display-grid"><article className="current-call">{current ? <><small>SENHA CHAMADA</small><h1>{current.ticketNumber}</h1><h2>GUICHÊ {current.counterName}</h2></> : <div className="waiting-call"><h2>Aguardando próxima chamada</h2></div>}</article><aside className="call-history"><h3>ÚLTIMAS CHAMADAS</h3>{history.length ? history.map(call => <p key={call.ticketId}><span>{call.ticketNumber}</span><b>{call.counterName}</b></p>) : <p>Nenhuma chamada ainda</p>}</aside></section></main>;
}
