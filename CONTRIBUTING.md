# Como contribuir

Obrigado por ajudar! Este repositório é desenvolvido por mais de uma pessoa; **cada pessoa usa a própria conta do GitHub** (nunca compartilhe senha ou token).

## Regras de ouro

1. **Nada de segredos nem dados pessoais no Git.** O repositório é público. Isso inclui `.env`, senhas, chaves de API, tokens, keystores, fotos, nomes e telefones reais. Use `dotnet user-secrets` ou variáveis de ambiente. Se algo vazar, avise imediatamente: o histórico do Git é permanente e o segredo precisa ser trocado.
2. **Nada de commits direto na `main`.** Todo trabalho entra por pull request.
3. **O servidor é a fonte da verdade.** O cliente nunca envia pontuação; ele envia *ações*, e o backend valida e calcula.
4. **Controllers finos.** Regras de negócio ficam em serviços/domínio, nunca nos controllers.

## Fluxo de trabalho

```
Issue → Branch → Implementação → Testes → Commit → Push → Pull Request → Code review → Merge (squash)
```

1. **Issue:** descreva o problema ou a funcionalidade e os critérios de aceite. Use os rótulos: `feature`, `bug`, `backend`, `frontend`, `android`, `database`, `game`, `security`, `refactor`, `documentation`.
2. **Branch** a partir da `main` atualizada:
   - `feature/<tema>` (ex.: `feature/groups`)
   - `fix/<tema>` (ex.: `fix/lobby-reconnect`)
   - `docs/…`, `chore/…`, `refactor/…`
3. **Commits** no padrão [Conventional Commits](https://www.conventionalcommits.org/pt-br/):

   ```
   feat: adiciona criação de grupos
   fix: corrige cálculo de pontuação no roubo
   refactor: separa regras da mímica
   docs: atualiza documentação da API
   test: adiciona testes de partida
   chore: atualiza dependências
   ```

   Evite mensagens como "mudanças", "teste", "final", "agora vai". Como o merge é *squash*, o **título do PR** também deve seguir esse padrão.
4. **Pull request:** preencha o modelo (o que mudou, por quê, como testar, impactos). PRs pequenos são revisados mais rápido.
5. **Revisão:** pelo menos uma pessoa diferente do autor aprova. Resolva todos os comentários.
6. **Merge:** *squash and merge* quando o CI estiver verde.

## Padrões de código

- C# com nullable habilitado; estilo definido em `.editorconfig` (`dotnet format` roda no CI).
- Identificadores em inglês; mensagens ao usuário e documentação em português.
- Toda regra de negócio nova vem com teste. Regras de jogo são testadas isoladamente (sem banco).
- Erros da API seguem `ProblemDetails` com um `code` estável (ex.: `session.not_your_turn`).
- Evite dependências com licença comercial (MediatR, AutoMapper, FluentAssertions 8+). Não são necessárias aqui.

## Banco de dados e migrações

- Alterações de schema entram como **migração do EF Core**, uma por PR.
- Se houver conflito no `ModelSnapshot` após um `rebase`, descarte a migração local e gere de novo.
- Toda tabela do schema `app` precisa ter **RLS habilitado** (há teste que verifica).
- Seeds devem ser idempotentes.

## Decisões de arquitetura

Mudanças relevantes de arquitetura ou de regras de negócio ganham um **ADR** em `docs/adr/` (modelo: contexto, decisão, consequências). Numeração sequencial.

## Segurança

Vulnerabilidades: não abra issue pública com detalhes exploráveis; avise os mantenedores diretamente. Veja `docs/SECURITY.md` (em breve) para o modelo de ameaças e os riscos aceitos.
