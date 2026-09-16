import { describe, expect, it } from 'vitest';
import { accessLinkAnchorProps, copyAccessLink, createAccessOrigins, getBranchAccessLinks, getOrganizationAccessLinks, normalizePublicOrigin } from './access-links';
import { vi } from 'vitest';

describe('central de links de acesso', () => {
  const origins = createAccessOrigins({
    QUEUEFLOW_PUBLIC_URL: 'https://admin.queueflow.test/',
    QUEUEFLOW_CUSTOMER_URL: 'https://customer.queueflow.test/',
    NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL: 'https://attendant.queueflow.test/',
    NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL: 'https://display.queueflow.test/',
  }, true);

  it('gera os links gerais usando o slug correto', () => {
    expect(getOrganizationAccessLinks(origins, 'minha empresa').map(link => link.url)).toEqual([
      'https://attendant.queueflow.test',
      'https://customer.queueflow.test/agendamento/minha%20empresa',
    ]);
  });

  it('gera os links da unidade usando o PublicId correto e sem barras duplicadas', () => {
    const links = getBranchAccessLinks(origins, 'empresa-a', 'branch/123');
    expect(links.map(link => link.url)).toEqual([
      'https://attendant.queueflow.test',
      'https://display.queueflow.test/display/branch%2F123',
      'https://customer.queueflow.test/unidade/branch%2F123',
      'https://customer.queueflow.test/agendamento/empresa-a',
    ]);
    expect(links.every(link => !link.url?.replace('https://', '').includes('//'))).toBe(true);
  });

  it('rejeita localhost e URLs inválidas em produção', () => {
    expect(normalizePublicOrigin('http://localhost:3001/', true)).toBeNull();
    expect(normalizePublicOrigin('http://127.0.0.1:3001', true)).toBeNull();
    expect(normalizePublicOrigin('not-a-url', true)).toBeNull();
    expect(normalizePublicOrigin('https://user:password@customer.queueflow.test', true)).toBeNull();
    expect(origins.admin).toBe('https://admin.queueflow.test');
  });

  it('permite localhost apenas durante desenvolvimento', () => {
    expect(normalizePublicOrigin('http://localhost:3001/', false)).toBe('http://localhost:3001');
  });

  it('copia o link e define abertura segura em nova aba', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    await copyAccessLink('https://customer.queueflow.test/unidade/branch-id', { writeText });
    expect(writeText).toHaveBeenCalledWith('https://customer.queueflow.test/unidade/branch-id');
    expect(accessLinkAnchorProps).toEqual({ target: '_blank', rel: 'noopener noreferrer' });
  });
});
