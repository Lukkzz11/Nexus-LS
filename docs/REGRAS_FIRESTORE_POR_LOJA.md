# Regras do Firestore por loja (rascunho para o Lucas publicar)

O Nexus **nunca** altera `firestore.rules`. Este texto é só uma sugestão; confira com quem mantém o ERP Life antes de publicar. Nada aqui foi testado no Firestore real.

## Estrutura usada pelo Nexus (nova, não toca nas coleções do ERP Life)
```
empresas/{empresaId}/lojas/{lojaId}
empresas/{empresaId}/lojas/{lojaId}/dias/{AAAA-MM-DD}
empresas/{empresaId}/lojas/{lojaId}/estoque/{produto}
empresas/{empresaId}/lojas/{lojaId}/lancamentos/{id}
empresas/{empresaId}/precos/{produto}/lojas/{lojaId}
```

## Ideia das regras
- Cada computador entra com uma conta própria (e-mail/senha da Firebase Auth) que recebe um *custom claim*: `empresaId`, `lojaId` e `papel` (`admin` ou `loja`).
- Administrador lê e escreve em toda a empresa.
- Conta de loja lê e escreve só em `lojas/{sua lojaId}/...` e só **lê** os preços da sua loja.
- Ninguém de loja escreve em `precos`.

```
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    function daEmpresa(e) { return request.auth != null && request.auth.token.empresaId == e; }
    function admin(e)     { return daEmpresa(e) && request.auth.token.papel == 'admin'; }
    function daLoja(e, l) { return daEmpresa(e) && request.auth.token.lojaId == l; }

    match /empresas/{e}/lojas/{l} {
      allow read, write: if admin(e) || daLoja(e, l);
      match /{sub=**} { allow read, write: if admin(e) || daLoja(e, l); }
    }
    match /empresas/{e}/precos/{produto}/lojas/{l} {
      allow read: if admin(e) || daLoja(e, l);
      allow write: if admin(e);
    }
  }
}
```

## Cuidados
- Mantenha as regras atuais do ERP Life; acrescente estas sem apagar as existentes.
- Dois computadores com o mesmo `lojaId` (banco copiado) escreveriam no mesmo lugar: gere um id novo em cada loja.
- Sem essas regras, a tela não impede uma loja de ler outra: a proteção real é a regra do Firestore.
