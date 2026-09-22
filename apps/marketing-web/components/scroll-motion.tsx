'use client';

import { useEffect } from 'react';
import { useReducedMotion } from './use-reduced-motion';

export function ScrollMotion() {
  const reduced = useReducedMotion();
  useEffect(() => {
    if (reduced || !('IntersectionObserver' in window)) return;
    const animations: Animation[] = [];
    const observer = new IntersectionObserver(entries => {
      entries.forEach((entry, index) => {
        if (!entry.isIntersecting) return;
        // Content remains visible without JS; animate only once as it enters the viewport.
        animations.push(entry.target.animate([{ opacity: 0, transform: 'translateY(12px)' }, { opacity: 1, transform: 'translateY(0)' }], { duration: 480, delay: (index % 3) * 55, easing: 'ease-out', fill: 'backwards' }));
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.12 });
    document.querySelectorAll('.benefit-card, .feature-card, .schedule-preview, .ticket-scene, .plan-card, .contact-panel, .process-flow').forEach(element => observer.observe(element));
    return () => { observer.disconnect(); animations.forEach(animation => animation.cancel()); };
  }, [reduced]);
  return null;
}
