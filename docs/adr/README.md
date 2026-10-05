# Registros de decisões de arquitetura (ADRs)

Cada decisão relevante de arquitetura ou de regra de negócio vira um arquivo numerado nesta pasta. O objetivo é que qualquer pessoa nova entenda **o que** foi decidido e **por quê**, sem precisar perguntar.

## Modelo

```markdown
# ADR-NNNN — Título curto

- **Status:** Proposto | Aceito | Substituído por ADR-XXXX
- **Data:** AAAA-MM-DD

## Contexto
O problema e as forças em jogo.

## Decisão
O que foi decidido, em frases diretas.

## Consequências
O que melhora, o que piora, riscos aceitos e como mitigar.
```

## Índice

| ADR | Título | Status |
|---|---|---|
| [0001](0001-dois-repositorios.md) | Dois repositórios (Front e Back) e banco como serviço | Aceito |
| [0002](0002-dotnet-monolito-modular.md) | .NET 10 LTS, monólito modular e módulos de jogo puros | Aceito |
| [0003](0003-login-so-por-telefone.md) | Login só por telefone, sem SMS e sem senha | Aceito |
| [0004](0004-sessoes-e-tokens.md) | Sessões: JWT curto, refresh rotativo e revogação imediata | Aceito |
| [0005](0005-fotos-de-avatar-no-postgres.md) | Fotos de avatar: processadas no servidor e guardadas no PostgreSQL | Aceito |
| [0006](0006-grupos-senha-compartilhada-e-perfis-sem-conta.md) | Grupos: senha compartilhada, perfis sem conta e reivindicação | Aceito |
