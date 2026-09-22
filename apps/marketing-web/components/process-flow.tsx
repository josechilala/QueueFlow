import { Icon, type IconName } from './icon';

const flow: { label: string; icon: IconName }[] = [
  { label: 'Cliente', icon: 'users' }, { label: 'Fila ou Agendamento', icon: 'calendar' },
  { label: 'Check-in', icon: 'check' }, { label: 'Atendente', icon: 'users' },
  { label: 'Display', icon: 'display' }, { label: 'Atendimento concluído', icon: 'shield' },
];

export function ProcessFlow() {
  return <div className="process-flow"><ol aria-label="Fluxo ilustrativo do atendimento">{flow.map(({ label, icon }) => <li key={label}><span className="flow-icon"><Icon name={icon} /></span><strong>{label}</strong></li>)}</ol><p>Check-in para agendamentos, conforme as regras da reserva. Display para apresentar as chamadas no local.</p></div>;
}
