import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { Header } from '../components/header';
import { Footer } from '../components/footer';
import { site } from '../lib/site';
import './styles.css';

export const metadata: Metadata = {
  metadataBase: new URL(site.url),
  title: { default: 'QueueFlow — Filas digitais e agendamento online', template: '%s | QueueFlow' },
  description: site.description,
  openGraph: { title: 'QueueFlow — Mais fluidez para cada atendimento', description: site.description, siteName: site.name, type: 'website', locale: 'pt_BR' },
  icons: { icon: '/favicon.svg' },
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return <html lang="pt-BR"><body><a className="skip-link" href="#conteudo">Pular para o conteúdo</a><Header />{children}<Footer /></body></html>;
}
