// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { ProductCarousel } from '../components/product-carousel';

let container: HTMLDivElement;
let root: Root;
let motion: MediaQueryList;
let visibility: (entries: { isIntersecting: boolean }[]) => void;

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  motion = Object.assign(new EventTarget(), { matches: false, media: '(prefers-reduced-motion: reduce)' }) as MediaQueryList;
  vi.stubGlobal('matchMedia', () => motion);
  vi.stubGlobal('IntersectionObserver', class {
    constructor(callback: typeof visibility) { visibility = callback; }
    observe() { visibility([{ isIntersecting: true }]); }
    disconnect() {}
  });
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});
afterEach(() => { act(() => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); });
const mount = () => act(() => root.render(<ProductCarousel>{Array.from({ length: 5 }, (_, i) => <div key={i}>Preview {i}</div>)}</ProductCarousel>));
const active = () => container.querySelector('.demo-slide.is-active')?.textContent;
const advance = (ms: number) => act(() => vi.advanceTimersByTime(ms));
function click(label: string) { act(() => (container.querySelector(`[aria-label="${label}"]`) as HTMLButtonElement).click()); }

it('rotates every five seconds and wraps without resetting on an unrelated render', () => {
  mount(); advance(4000); mount(); advance(1000);
  expect(active()).toBe('Preview 1');
  for (let i = 0; i < 4; i++) advance(5000);
  expect(active()).toBe('Preview 0');
});

it('supports both directions, dots and a temporary pause after manual navigation', () => {
  mount(); click('Demonstração anterior');
  expect(active()).toBe('Preview 4');
  click('Próxima demonstração');
  expect(active()).toBe('Preview 0');
  click('Mostrar Painel do Atendente');
  advance(14999); expect(active()).toBe('Preview 2');
  advance(1); advance(4999); expect(active()).toBe('Preview 2');
  advance(1); expect(active()).toBe('Preview 3');
});

it('supports keyboard navigation and excludes inactive slides from accessibility', () => {
  mount();
  act(() => container.querySelector('.demo-carousel')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true })));
  expect(active()).toBe('Preview 4');
  expect(container.querySelectorAll('.demo-slide[aria-hidden="true"][inert]')).toHaveLength(4);
  expect(container.querySelector('[role="status"]')?.textContent).toContain('Gestão da Operação');
  act(() => container.querySelector('.demo-carousel')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true })));
  expect(active()).toBe('Preview 0');
});

it('pauses while focus remains inside and resumes after it leaves', () => {
  mount();
  act(() => (container.querySelector('button') as HTMLButtonElement).focus());
  advance(20000); expect(active()).toBe('Preview 0');
  act(() => (document.activeElement as HTMLElement).blur());
  advance(5000); expect(active()).toBe('Preview 1');
});

it('pauses offscreen and supports an explicit persistent pause', () => {
  mount(); act(() => visibility([{ isIntersecting: false }]));
  advance(10000); expect(active()).toBe('Preview 0');
  act(() => visibility([{ isIntersecting: true }]));
  act(() => (container.querySelector('.demo-pause') as HTMLButtonElement).click());
  advance(20000); expect(active()).toBe('Preview 0');
  act(() => (container.querySelector('.demo-pause') as HTMLButtonElement).click());
  advance(5000); expect(active()).toBe('Preview 1');
});

it('disables automatic rotation for reduced motion, including preference changes', () => {
  Object.defineProperty(motion, 'matches', { value: true, configurable: true });
  mount(); advance(20000); expect(active()).toBe('Preview 0');
  expect(container.querySelector('.demo-pause')).toBeNull();
  click('Próxima demonstração'); expect(active()).toBe('Preview 1');
  advance(15000);
  act(() => { Object.defineProperty(motion, 'matches', { value: false }); motion.dispatchEvent(new Event('change')); });
  advance(5000); expect(active()).toBe('Preview 2');
  act(() => { Object.defineProperty(motion, 'matches', { value: true }); motion.dispatchEvent(new Event('change')); });
  advance(20000); expect(active()).toBe('Preview 2');
});

it('cleans up timers on unmount', () => {
  mount(); click('Próxima demonstração');
  act(() => root.render(null));
  expect(vi.getTimerCount()).toBe(0);
});

it('pauses on hover and when the browser tab is hidden', () => {
  mount();
  act(() => container.querySelector('.demo-carousel')!.dispatchEvent(new MouseEvent('mouseover', { bubbles: true })));
  advance(10000); expect(active()).toBe('Preview 0');
  act(() => container.querySelector('.demo-carousel')!.dispatchEvent(new MouseEvent('mouseout', { bubbles: true })));
  advance(5000); expect(active()).toBe('Preview 1');
  const hidden = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
  act(() => document.dispatchEvent(new Event('visibilitychange')));
  advance(10000); expect(active()).toBe('Preview 1');
  hidden.mockReturnValue(false);
  act(() => document.dispatchEvent(new Event('visibilitychange')));
  advance(5000); expect(active()).toBe('Preview 2');
  hidden.mockRestore();
});
