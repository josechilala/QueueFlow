export const site = {
  name: 'QueueFlow',
  url: 'https://queueflow.com.br',
  adminUrl: 'https://app.queueflow.com.br',
  description: 'Filas digitais, agendamento online e gestão do atendimento em tempo real. Organize a operação da sua empresa com o QueueFlow.',
};

// Optional public address, supplied at build time. Never substitute a fictitious contact.
export function getContactUrl(email = process.env.NEXT_PUBLIC_QUEUEFLOW_CONTACT_EMAIL) {
  const address = email?.trim();
  if (!address || !/^[^\s@?&#]+@[^\s@?&#]+\.[^\s@?&#]+$/.test(address)) return null;
  return `mailto:${address}?subject=${encodeURIComponent('Quero conhecer o teste de 14 dias do QueueFlow')}`;
}

export const navigation = [
  ['Recursos', 'recursos'], ['Como funciona', 'como-funciona'],
  ['Soluções', 'solucoes'], ['Planos', 'planos'], ['FAQ', 'faq'],
] as const;
