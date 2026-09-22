'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useReducedMotion } from './use-reduced-motion';

export const demoLabels = ['Fila Digital', 'Agendamento Online', 'Painel do Atendente', 'Display de Chamadas', 'Gestão da Operação'];

export function ProductCarousel({ children }: { children: ReactNode[] }) {
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const [visible, setVisible] = useState(false);
  const [hidden, setHidden] = useState(false);
  const [holding, setHolding] = useState(false);
  const [announcement, setAnnouncement] = useState('');
  const reduced = useReducedMotion();
  const root = useRef<HTMLDivElement>(null);
  const holdTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const onVisibility = () => setHidden(document.hidden);
    onVisibility();
    document.addEventListener('visibilitychange', onVisibility);
    const observer = new IntersectionObserver(([entry]) => setVisible(entry.isIntersecting), { threshold: 0.15 });
    if (root.current) observer.observe(root.current);
    return () => {
      document.removeEventListener('visibilitychange', onVisibility);
      observer.disconnect();
      if (holdTimer.current) clearTimeout(holdTimer.current);
    };
  }, []);

  useEffect(() => {
    if (reduced || paused || hovered || focused || hidden || !visible || holding) return;
    const timer = setTimeout(() => setIndex(current => (current + 1) % demoLabels.length), 5000);
    return () => clearTimeout(timer);
  }, [index, reduced, paused, hovered, focused, hidden, visible, holding]);

  function navigate(next: number) {
    const target = (next + demoLabels.length) % demoLabels.length;
    setIndex(target);
    setAnnouncement(`${target + 1} de ${demoLabels.length}: ${demoLabels[target]}. Dados ilustrativos.`);
    setHolding(true);
    if (holdTimer.current) clearTimeout(holdTimer.current);
    holdTimer.current = setTimeout(() => setHolding(false), 15000);
  }

  return <div ref={root} className="demo-carousel" role="region" aria-roledescription="carrossel" aria-label="Demonstração ilustrativa do QueueFlow"
    onMouseEnter={() => setHovered(true)} onMouseLeave={() => setHovered(false)}
    onFocusCapture={() => setFocused(true)} onBlurCapture={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setFocused(false); }}
    onKeyDown={event => {
      const target = event.key === 'ArrowRight' ? index + 1 : event.key === 'ArrowLeft' ? index - 1 : event.key === 'Home' ? 0 : event.key === 'End' ? demoLabels.length - 1 : null;
      if (target !== null) { event.preventDefault(); navigate(target); }
    }}>
    <div className="demo-slides" id="product-demo-slides">{children.map((slide, position) => <div key={demoLabels[position]} className={`demo-slide${position === index ? ' is-active' : ''}`} role="group" aria-roledescription="slide" aria-label={`${position + 1} de 5: ${demoLabels[position]}`} aria-hidden={position !== index} inert={position !== index}>{slide}</div>)}</div>
    <div className="demo-toolbar">
      <div className="demo-current"><span className="tiny-label">DEMONSTRAÇÃO</span><strong>{demoLabels[index]}</strong></div>
      <div className="demo-buttons"><button type="button" aria-label="Demonstração anterior" aria-controls="product-demo-slides" onClick={() => navigate(index - 1)}>←</button><button type="button" aria-label="Próxima demonstração" aria-controls="product-demo-slides" onClick={() => navigate(index + 1)}>→</button></div>
    </div>
    <div className="demo-navigation"><div className="demo-dots" role="group" aria-label="Escolher demonstração">{demoLabels.map((label, position) => <button type="button" key={label} aria-label={`Mostrar ${label}`} aria-current={position === index ? 'true' : undefined} onClick={() => navigate(position)}><span /></button>)}</div>
      {!reduced && <button type="button" className="demo-pause" aria-pressed={paused} onClick={() => setPaused(value => !value)}>{paused ? 'Retomar rotação' : 'Pausar rotação'}</button>}
    </div>
    <span className="sr-only" role="status" aria-live="polite" aria-atomic="true">{announcement}</span>
  </div>;
}
