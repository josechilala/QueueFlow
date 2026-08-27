export function LogoutButton() {
  return <form action="/api/auth/logout" method="post"><button type="submit" className="secondary">Sair</button></form>;
}
