import type { Metadata } from 'next';
import { LegalPage } from '../../components/legal-page';

export const metadata: Metadata = { title: 'Termos de uso — minuta em revisão', description: 'Estrutura preliminar dos Termos de uso do QueueFlow, pendente de revisão jurídica.', alternates: { canonical: '/termos/' }, robots: { index: false, follow: true } };

export default function TermsPage() {
  return <LegalPage title="Termos de uso">
    <section><h2>1. Apresentação e escopo</h2><p>O QueueFlow reúne ferramentas para filas digitais, agendamento online e gestão do atendimento. Este site apresenta a plataforma e orienta empresas interessadas em conhecer o produto.</p><p><strong>Pendente de revisão:</strong> identificar a pessoa jurídica responsável, seus dados cadastrais, endereço e canais oficiais de atendimento.</p></section>
    <section><h2>2. Acesso e utilização da plataforma</h2><p>Os painéis da plataforma são acessados separadamente deste site institucional. Este site não realiza cadastro, contratação ou pagamento.</p><p><strong>Pendente de revisão:</strong> definir critérios de elegibilidade, responsabilidades pelas contas, regras de uso e condições de suspensão ou encerramento de acesso.</p></section>
    <section><h2>3. Teste e condições comerciais</h2><p>A apresentação comercial prevê um teste de 14 dias e informa que os planos comerciais serão divulgados posteriormente.</p><p><strong>Pendente de revisão:</strong> validar o início e o término do teste, os recursos disponibilizados, o tratamento da conta após o período e as condições de eventual contratação. Esta minuta não estabelece cobrança nem renovação automática.</p></section>
    <section><h2>4. Responsabilidades e disponibilidade</h2><p><strong>Pendente de revisão:</strong> detalhar as responsabilidades da empresa usuária e do fornecedor, as condições de suporte, manutenção, disponibilidade e os limites contratuais aplicáveis. Nenhum nível de serviço é prometido nesta minuta.</p></section>
    <section><h2>5. Marca, conteúdo e propriedade intelectual</h2><p><strong>Pendente de revisão:</strong> confirmar a titularidade e as condições de uso da marca, dos materiais do site e do software, assim como os direitos sobre conteúdos inseridos pelos usuários.</p></section>
    <section><h2>6. Privacidade e dados</h2><p>O documento de <a href="/privacidade/">Privacidade</a> reúne os tópicos que ainda precisam ser mapeados e aprovados para a versão definitiva.</p></section>
    <section><h2>7. Contato, atualizações e vigência</h2><p><strong>Pendente de revisão:</strong> informar o canal oficial para questões contratuais, a data de vigência, o procedimento de atualização e as disposições jurídicas aplicáveis. Não há data de vigência definida para esta minuta.</p></section>
  </LegalPage>;
}
