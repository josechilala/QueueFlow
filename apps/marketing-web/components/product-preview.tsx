import { Icon } from './icon';
import { ProductCarousel } from './product-carousel';
import { QueueDemo, SchedulingDemo, AttendantDemo, DisplayDemo } from './demo-panels';

export function ProductPreview() {
  return <div className="preview-scene" aria-label="Exemplo ilustrativo do fluxo de atendimento">
    <div className="product-preview">
      <div className="preview-top"><span className="preview-brand">QueueFlow<span> / Sua operação</span></span><span className="live-pill"><i /> Ao vivo</span></div>
      <ProductCarousel>{[<QueueDemo key="queue" />, <SchedulingDemo key="schedule" />, <AttendantDemo key="attendant" />, <DisplayDemo key="display" />, <div className="preview-body" key="management"><div className="preview-title"><div><span className="tiny-label">VISÃO GERAL</span><h3>Um bom dia começa<br />com tudo no lugar.</h3></div><span className="preview-avatar">Q</span></div>
        <div className="preview-metrics"><div><span>Aguardando</span><strong>08</strong><small>na fila digital</small></div><div><span>Em atendimento</span><strong>03</strong><small>equipe em ação</small></div><div><span>Agendamentos</span><strong>12</strong><small>para hoje</small></div></div>
        <div className="queue-heading"><strong>Fluxo de atendimento</strong><span>Hoje</span></div>
        <div className="preview-row"><span className="ticket active">A024</span><div><strong>Atendimento geral</strong><span>Guichê 02</span></div><span className="status-pill">Em atendimento</span></div>
        <div className="preview-row"><span className="ticket">A025</span><div><strong>Atendimento geral</strong><span>Fila digital</span></div><span className="waiting-label">Aguardando</span></div>
        <div className="preview-row"><span className="ticket">B008</span><div><strong>Atendimento agendado</strong><span>Check-in realizado</span></div><span className="waiting-label">Aguardando</span></div>
        <div className="demo-open-queues"><span>Filas abertas</span><strong>04</strong></div>
      </div>]}</ProductCarousel>
    </div>
    <div className="floating-ticket demo-floating-ticket"><span className="icon-box"><Icon name="check" /></span><div><strong>Cada cliente no seu tempo.</strong><span>Da chegada ao atendimento.</span></div></div>
    <p className="preview-caption">Representação ilustrativa · dados de exemplo</p>
  </div>;
}
