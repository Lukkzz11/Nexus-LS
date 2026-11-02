// No Nexus LS quem abre o editor já está logado no programa (e tem a permissão de Configurações).
export function useAuth() {
  return { isAuthenticated: true, userProfile: { nome: "Nexus LS" } };
}
