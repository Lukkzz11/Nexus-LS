using Xunit;

// As telas de teste dependem de um único Application do WPF (o Capturador usa uma thread de
// interface compartilhada) e de estado estático do programa (sessão/operador). Rodando os testes
// em paralelo, alguns deles falhavam de vez em quando no run completo e passavam isolados.
// Com a suíte em sequência o resultado é sempre o mesmo. O custo é alguns minutos a mais no CI.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
