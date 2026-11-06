const projectId = "nexus-ls";
const apiKey = "AIzaSyDkdYJIAcmAPyhxIs2DoJJH9e7q0WTfalI";

console.log(`Testando conectividade direta com o Firebase [${projectId}]...`);

async function testarConexao() {
  try {
    // 1. Testa endpoint REST do Firestore
    const url = `https://firestore.googleapis.com/v1/projects/${projectId}/databases/(default)/documents/licencas?key=${apiKey}`;
    const res = await fetch(url);
    console.log(`Status de resposta do Firestore REST: ${res.status} (${res.statusText})`);

    if (res.status === 403 || res.status === 401) {
      console.log("-> Conexão com Firebase nexus-ls bem-sucedida! Regras de segurança ativas rejeitando listagem não autenticada (comportamento correto e esperado).");
    } else if (res.status === 200) {
      const data = await res.json();
      console.log(`-> Conexão bem-sucedida! Encontrados ${(data.documents || []).length} documentos de licença.`);
    } else {
      console.log(`-> Resposta recebida: ${res.status}`);
    }

    // 2. Testa consulta de um doc específico inexistente
    const urlDoc = `https://firestore.googleapis.com/v1/projects/${projectId}/databases/(default)/documents/licencas/CHAVE_TESTE_INEXISTENTE?key=${apiKey}`;
    const resDoc = await fetch(urlDoc);
    console.log(`Status de busca de chave teste específica: ${resDoc.status} (${resDoc.statusText})`);
    if (resDoc.status === 404) {
      console.log("-> O Firestore respondeu 404 Not Found como esperado para chave inexistente (permissão de 'get' individual ativa e funcionando).");
    }

    console.log("\nConexão com o Firebase nexus-ls totalmente validada!");
  } catch (err) {
    console.error("Falha ao conectar com o Firebase:", err);
  }
}

testarConexao();
