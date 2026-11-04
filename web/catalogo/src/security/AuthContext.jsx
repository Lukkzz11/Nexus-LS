// Dentro do Nexus LS quem abre o editor já está logado no programa (e tem a permissão de Configurações).
// No site publicado (publico.html) ninguém é administrador: a barra de administração do catálogo não aparece.
export function useAuth() {
  const publico = typeof window !== "undefined" && window.__nexusPublico === true;
  return { isAuthenticated: !publico, userProfile: { nome: "Nexus LS" } };
}
