import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, describe, expect, it, vi } from 'vitest';
import HomePage from '../app/page';
import TermsPage from '../app/termos/page';
import PrivacyPage from '../app/privacidade/page';
import { Header } from '../components/header';
import { Footer } from '../components/footer';
import { navigation, site } from '../lib/site';
import { trialRequestEndpoint } from '../components/trial-request-form';

afterEach(() => vi.unstubAllEnvs());

describe('public marketing navigation', () => {
  it('provides a target for each menu anchor and routes trial CTAs to contact', () => {
    const html = renderToStaticMarkup(<HomePage />);
    for (const [, id] of navigation) expect(html).toContain(`id="${id}"`);
    expect(html).toContain('id="contato"');
    expect(html).toMatch(/href="#contato">Começar teste grátis/);
    expect(html).toContain('<form');
    expect(html).toContain('dados de exemplo');
  });
  it('links login to the official Admin and exposes both legal pages', () => {
    const header = renderToStaticMarkup(<Header />);
    const footer = renderToStaticMarkup(<Footer />);
    expect(header).toContain(`href="${site.adminUrl}"`);
    expect(footer).toContain(`href="${site.adminUrl}"`);
    expect(footer).toContain('href="/termos/"');
    expect(footer).toContain('href="/privacidade/"');
  });
});

describe('trial requests', () => {
  it('disables submission when the API is not configured', () => {
    vi.stubEnv('NEXT_PUBLIC_QUEUEFLOW_API_URL', '');
    const html = renderToStaticMarkup(<HomePage />);
    expect(html).toContain('Solicitações temporariamente indisponíveis');
    expect(html).toContain('type="submit" class="button button-light" disabled');
    expect(html).not.toContain('mailto:');
  });
  it('collects contact information and mandatory consent without credentials or payment', () => {
    vi.stubEnv('NEXT_PUBLIC_QUEUEFLOW_API_URL', 'https://api.example.test');
    const html = renderToStaticMarkup(<HomePage />);
    for (const name of ['name', 'email', 'companyName', 'phone', 'acceptedTerms']) expect(html).toContain(`name="${name}"`);
    expect(html).toContain('type="checkbox" required');
    expect(html).toContain('href="/termos"');
    expect(html).toContain('href="/privacidade"');
    expect(html).not.toContain('type="password"');
    expect(html).not.toContain('Solicitações temporariamente indisponíveis');
    expect(html).not.toContain('Canal comercial em breve');
    expect(html).toContain('Solicitar teste grátis');
  });
  it.each(['', '  ', 'invalid', 'https://api.example.test/path', 'https://user:password@api.example.test', 'https://api.example.test?other=value'])('rejects invalid API origin %j', origin => {
    expect(trialRequestEndpoint(origin)).toBeNull();
  });
  it('uses HTTPS in production', () => {
    vi.stubEnv('NODE_ENV', 'production');
    expect(trialRequestEndpoint('http://localhost:5260')).toBeNull();
    expect(trialRequestEndpoint(' https://api.example.test/ ')).toBe('https://api.example.test/api/v1/public/trial-requests');
  });
});

it('identifies both legal documents as drafts pending review', () => {
  for (const page of [<TermsPage key="terms" />, <PrivacyPage key="privacy" />]) {
    expect(renderToStaticMarkup(page)).toContain('Minuta — revisão jurídica obrigatória');
  }
});
