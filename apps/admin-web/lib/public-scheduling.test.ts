import { describe, expect, it, vi } from 'vitest';
import { buildPublicSchedulingUrl, canViewPublicSchedulingLink, copyPublicSchedulingUrl } from './public-scheduling';

describe('public scheduling link', () => {
  it.each(['Owner', 'Admin', 'Manager'] as const)('%s pode visualizar o card', role => {
    expect(canViewPublicSchedulingLink(role)).toBe(true);
  });

  it.each(['Attendant', 'Viewer'] as const)('%s não pode visualizar o card', role => {
    expect(canViewPublicSchedulingLink(role)).toBe(false);
  });

  it('usa a base configurada e o slug da organização autenticada', () => {
    expect(buildPublicSchedulingUrl('https://agenda.queueflow.com/', 'clinica-vida')).toBe('https://agenda.queueflow.com/empresa/clinica-vida');
    expect(buildPublicSchedulingUrl('https://agenda.queueflow.com', 'empresa-b')).toBe('https://agenda.queueflow.com/empresa/empresa-b');
  });

  it('não cria link quando base ou slug estão indisponíveis', () => {
    expect(buildPublicSchedulingUrl('', 'clinica-vida')).toBeNull();
    expect(buildPublicSchedulingUrl('https://agenda.queueflow.com', '')).toBeNull();
  });

  it('copia exatamente a URL pública', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    await copyPublicSchedulingUrl('https://agenda.queueflow.com/empresa/clinica-vida', { writeText });
    expect(writeText).toHaveBeenCalledWith('https://agenda.queueflow.com/empresa/clinica-vida');
  });

  it('propaga falha da Clipboard API para a interface exibir feedback', async () => {
    const writeText = vi.fn().mockRejectedValue(new Error('clipboard denied'));
    await expect(copyPublicSchedulingUrl('https://agenda.queueflow.com/empresa/clinica-vida', { writeText })).rejects.toThrow('clipboard denied');
  });
});
