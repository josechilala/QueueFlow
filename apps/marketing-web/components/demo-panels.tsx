import { Icon } from './icon';

export function QueueDemo() {
  return <div className="demo-panel"><div className="preview-title"><div><span className="tiny-label">FILA DIGITAL</span><h3>Seu lugar está aqui.</h3></div><span className="icon-box"><Icon name="layers" /></span></div><div className="demo-customer"><span className="status-pill">Aguardando</span><strong className="large-ticket">A025</strong><p>Atendimento geral</p><div className="position"><span>Sua posição na fila</span><strong>2ª</strong></div><p className="ticket-note">Acompanhe sua posição e aguarde a chamada.</p></div></div>;
}

export function SchedulingDemo() {
  return <div className="demo-panel"><div className="preview-title"><div><span className="tiny-label">AGENDAMENTO ONLINE</span><h3>Um horário para você.</h3></div><span className="icon-box"><Icon name="calendar" /></span></div><div className="calendar-days">{[['SEG', '12'], ['TER', '13'], ['QUA', '14'], ['QUI', '15'], ['SEX', '16']].map(([day, date]) => <div key={day} className={date === '14' ? 'selected' : ''}><span>{day}</span><strong>{date}</strong></div>)}</div><p className="tiny-label">EXEMPLOS DE HORÁRIOS</p><div className="time-slots"><span>09:00</span><span className="selected">10:30 <Icon name="check" /></span><span>14:00</span></div><div className="demo-confirmation"><Icon name="check" /><div><strong>Agendamento confirmado</strong><span>Quarta-feira, às 10:30 · exemplo ilustrativo</span></div></div></div>;
}

export function AttendantDemo() {
  return <div className="demo-panel"><div className="preview-title"><div><span className="tiny-label">PAINEL DO ATENDENTE</span><h3>Cada cliente no seu tempo.</h3></div><span className="icon-box"><Icon name="users" /></span></div><div className="demo-service"><span className="status-pill">Em atendimento</span><strong>A024</strong><span>Atendimento geral · Guichê 02</span></div><div className="preview-row"><span className="ticket">A025</span><div><strong>Próximo cliente</strong><span>Aguardando</span></div><span className="demo-call">Chamar <Icon name="arrow" /></span></div><p className="demo-footnote">Representação do painel · chamada ilustrativa</p></div>;
}

export function DisplayDemo() {
  return <div className="demo-panel"><div className="preview-title"><div><span className="tiny-label">DISPLAY DE CHAMADAS</span><h3>É a sua vez.</h3></div><span className="icon-box"><Icon name="display" /></span></div><div className="demo-display"><span>SENHA EM CHAMADA</span><strong>A024</strong><span>Dirija-se ao Guichê 02</span></div><div className="queue-heading"><strong>Próximas chamadas</strong><span>Exemplo</span></div><div className="demo-upcoming"><span>A025</span><span>A026</span><span>B008</span></div></div>;
}
