import type { Metadata } from 'next';
import { ScrollMotion } from '../components/scroll-motion';
import { ProcessFlow } from '../components/process-flow';
import { Icon } from '../components/icon';
import { ProductPreview } from '../components/product-preview';
import { SectionHeading } from '../components/section-heading';
import { benefits, faq, features, segments, steps } from '../content/site-content';
import { TrialRequestForm } from '../components/trial-request-form';

export const metadata: Metadata = { alternates: { canonical: '/' }, openGraph: { url: '/' } };

export default function HomePage() {
  return <main id="conteudo">
    <ScrollMotion />
    <section className="hero"><div className="container hero-grid">
      <div className="hero-copy"><p className="eyebrow hero-eyebrow"><span /> MAIS FLUIDEZ. MENOS ESPERA.</p>
        <h1>O próximo passo<br />é um atendimento<br /><em>sem confusão.</em></h1>
        <p className="hero-description">Filas digitais, agendamento online e gestão em tempo real. O QueueFlow centraliza sua operação para você cuidar do que importa: <strong>atender bem.</strong></p>
        <div className="actions"><a className="button" href="#contato">Começar teste grátis <Icon name="arrow" /></a><a className="button button-outline" href="#recursos">Conhecer recursos</a></div>
        <p className="hero-note"><Icon name="check" /> Teste por 14 dias <span>·</span> Acesso pelo navegador</p>
      </div><ProductPreview />
    </div></section>

    <div className="value-strip"><div className="container"><span>Uma operação.<br /><strong>Muito mais organizada.</strong></span><span><Icon name="layers" /> Filas digitais</span><span><Icon name="calendar" /> Agendamento online</span><span><Icon name="activity" /> Visão em tempo real</span></div></div>

    <section className="section container" aria-labelledby="beneficios-titulo"><div className="section-heading centered"><p className="eyebrow">BOM PARA QUEM CHEGA. BOM PARA QUEM ATENDE.</p><h2 id="beneficios-titulo">Mais organização em cada encontro.</h2><p className="section-intro">Troque a incerteza por um fluxo claro, do primeiro acesso à conclusão do atendimento.</p></div><div className="benefit-grid">{benefits.map(([icon, title, description]) => <article className="benefit-card" key={title}><span className="icon-box"><Icon name={icon} /></span><h3>{title}</h3><p>{description}</p></article>)}</div></section>

    <section className="section soft-section"><div className="container split-section"><div><SectionHeading eyebrow="FILA DIGITAL" title="A fila anda. Seu cliente acompanha.">Um caminho simples para organizar quem chega e orientar quem espera, sem perder de vista o atendimento.</SectionHeading><ol className="queue-steps">{['Cliente entra na fila', 'Recebe sua posição', 'Acompanha a espera', 'Atendente chama', 'Atendimento é realizado'].map((step, index) => <li key={step}><span>{index + 1}</span>{step}</li>)}</ol></div><div className="ticket-scene"><div className="customer-ticket"><span className="ticket-brand">QueueFlow</span><span className="status-pill">Você está na fila</span><h3>Seu lugar está aqui.</h3><p>Atendimento geral</p><span className="large-ticket">A025</span><div className="position"><span>Sua posição na fila</span><strong>2ª</strong></div><p className="ticket-note">Acompanhe sua posição e aguarde a chamada.</p></div><span className="illustration-caption">Exemplo ilustrativo da experiência do cliente</span></div></div></section>

    <section className="section container split-section scheduling-section"><div className="schedule-preview"><div className="schedule-heading"><span className="icon-box"><Icon name="calendar" /></span><div><span className="tiny-label">AGENDAMENTO ONLINE</span><h3>Um horário para você.</h3></div></div><div className="calendar-days" aria-hidden="true">{[['SEG', '12'], ['TER', '13'], ['QUA', '14'], ['QUI', '15'], ['SEX', '16']].map(([day, date]) => <div className={date === '14' ? 'selected' : ''} key={day}><span>{day}</span><strong>{date}</strong></div>)}</div><p className="tiny-label">EXEMPLOS DE HORÁRIOS</p><div className="time-slots"><span>09:00</span><span className="selected">10:30 <Icon name="check" /></span><span>14:00</span></div><div className="schedule-note"><Icon name="check" /><span>Do agendamento ao check-in.<br /><strong>Tudo no fluxo de atendimento.</strong></span></div><p className="illustration-caption">Datas e horários ilustrativos</p></div><div><SectionHeading eyebrow="AGENDAMENTO ONLINE" title="A próxima visita começa antes da chegada.">Seu cliente consulta os horários disponíveis e agenda pelos links públicos da empresa. Na hora de ser atendido, realiza o check-in conforme as regras da reserva.</SectionHeading><ul className="check-list"><li><Icon name="check" /> Disponibilidade por unidade e serviço</li><li><Icon name="check" /> Agendamento pelo navegador</li><li><Icon name="check" /> Check-in integrado ao fluxo de atendimento</li></ul><a className="text-link" href="#contato">Conheça o QueueFlow <Icon name="arrow" /></a></div></section>

    <section className="section dark-section" id="como-funciona"><div className="container"><SectionHeading eyebrow="COMO FUNCIONA" title="Da configuração ao atendimento. Sem complicar." centered>Um fluxo conectado para sua empresa, sua equipe e seus clientes.</SectionHeading><ProcessFlow /><ol className="how-grid">{steps.map(([title, description], index) => <li key={title}><span className="step-number">0{index + 1}</span><h3>{title}</h3><p>{description}</p></li>)}</ol></div></section>

    <section className="section container" id="recursos"><SectionHeading eyebrow="OS RECURSOS CERTOS, NO MESMO LUGAR" title="Tudo se conecta a um bom atendimento." centered>Ferramentas para organizar a rotina e acompanhar o que acontece na operação.</SectionHeading><div className="feature-grid">{features.map(([icon, title, description]) => <article className="feature-card" key={title}><span className="feature-icon"><Icon name={icon} /></span><div><h3>{title}</h3><p>{description}</p></div></article>)}</div></section>

    <section className="section soft-section" id="solucoes"><div className="container solutions-layout"><SectionHeading eyebrow="DIFERENTES NEGÓCIOS. UM DESAFIO EM COMUM." title="Onde tem atendimento, tem espaço para organizar.">Para empresas que recebem pessoas por ordem de chegada ou horário marcado. Conheça algumas possibilidades de uso.</SectionHeading><ul className="segments">{segments.map(segment => <li key={segment}><Icon name="check" />{segment}</li>)}</ul></div></section>

    <section className="section container plans-layout" id="planos"><SectionHeading eyebrow="COMECE PELA EXPERIÊNCIA" title="Conheça na prática. Decida com clareza.">Explore como filas digitais e agendamentos podem fazer parte da sua operação. Converse com a equipe para conhecer o processo de ativação.</SectionHeading><article className="plan-card"><span className="plan-tag">SEU PRÓXIMO PASSO</span><h3>Experimente o QueueFlow<br />por <strong>14 dias.</strong></h3><p>Planos comerciais em breve</p><ul className="check-list"><li><Icon name="check" /> Conheça o fluxo de atendimento</li><li><Icon name="check" /> Explore filas e agendamento online</li><li><Icon name="check" /> Avalie a experiência com sua equipe</li></ul><a className="button" href="#contato">Começar teste grátis <Icon name="arrow" /></a><small>A ativação do teste é orientada pelo contato comercial.</small></article></section>

    <section className="section faq-section" id="faq"><div className="container faq-layout"><SectionHeading eyebrow="AINDA TEM DÚVIDAS?" title="Vamos deixar tudo mais claro.">As principais perguntas para começar a conhecer o QueueFlow.</SectionHeading><div className="faq-list">{faq.map(([question, answer]) => <details key={question}><summary>{question}<span aria-hidden="true">+</span></summary><p>{answer}</p></details>)}</div></div></section>

    <section className="section container" id="contato"><div className="contact-panel"><div><p className="eyebrow">SEU ATENDIMENTO PODE FLUIR MELHOR</p><h2>Mais organização para sua equipe.<br />Mais clareza para seus clientes.</h2><p>Conheça o QueueFlow e dê o próximo passo para testar por 14 dias.</p></div><div className="contact-action"><TrialRequestForm /></div></div></section>
  </main>;
}
